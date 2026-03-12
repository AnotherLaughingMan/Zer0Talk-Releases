using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Zer0Talk.Models;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

/// <summary>
/// Client-side room lifecycle service for the hosted server mode.
///
/// Room messaging works in two modes:
///   Admin-ONLINE  → members connect P2P to admin via relay session (ROOM-ADMIN-ONLINE push delivers relay key)
///   Admin-OFFLINE → members send ROOM-MSG to server which fans out ciphertext to all connected members
///
/// All commands are forwarded to the home server via ServerAccountService.
/// Incoming server pushes (ROOM-INVITED, ROOM-ADMIN-ONLINE/OFFLINE, ROOM-DELIVER, …)
/// are dispatched via events for ViewModels to consume.
/// </summary>
public sealed class RoomService
{
    private readonly ServerAccountService _account;
    private readonly RoomKeyStore         _keyStore;
    private readonly SettingsService      _settings;
    private readonly IdentityService      _identity;

    // Admin online state per room: roomId → relayKey (null = offline)
    private readonly ConcurrentDictionary<string, string?> _adminRelayKey = new(StringComparer.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════════
    //  Events
    // ═══════════════════════════════════════════════════════════════

    /// <summary>New room invite received from the server.</summary>
    public event Action<string /*roomId*/, string /*inviterUid*/>? RoomInviteReceived;

    /// <summary>Admin came online — members can now connect P2P via relay key.</summary>
    public event Action<string /*roomId*/, string /*relayKey*/>? AdminOnline;

    /// <summary>Admin went offline — server-routed mode is now active.</summary>
    public event Action<string /*roomId*/>? AdminOffline;

    /// <summary>A message was delivered via the server (offline routing), ciphertext already decoded by caller.</summary>
    public event Action<string /*roomId*/, string /*senderUid*/, string /*ciphertextHex*/>? MessageDelivered;

    /// <summary>A member joined the room.</summary>
    public event Action<string /*roomId*/, string /*uid*/>? MemberJoined;

    /// <summary>A member left the room.</summary>
    public event Action<string /*roomId*/, string /*uid*/>? MemberLeft;

    /// <summary>Server requests a group rekey (member left/kicked; admin must redistribute key).</summary>
    public event Action<string /*roomId*/>? RekeyRequired;

    /// <summary>Admin should distribute the current room key to a newly-joined member.</summary>
    public event Action<string /*roomId*/, string /*newMemberUid*/>? KeyDeliveryRequired;

    public RoomService(ServerAccountService account, RoomKeyStore keyStore,
                       SettingsService settings, IdentityService identity)
    {
        _account  = account;
        _keyStore = keyStore;
        _settings = settings;
        _identity = identity;

        _account.PushReceived    += OnPush;
        _account.ConnectionLost  += OnConnectionLost;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Room management commands
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a new room. Returns the server-assigned room ID on success, or null.
    /// </summary>
    public async Task<string?> CreateRoomAsync(string displayName, int memberCap, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-CREATE {displayName} {memberCap}", ct);
        if (response == null || !response.StartsWith("OK ", StringComparison.Ordinal))
        {
            Logger.Log($"RoomService: ROOM-CREATE failed: {response}");
            return null;
        }
        var roomId = response[3..].Trim();
        TrackRoom(roomId);
        Logger.Log($"RoomService: room created | id={roomId}");
        return roomId;
    }

    /// <summary>
    /// Invite a user to a room. Optionally specify their home server for cross-server routing.
    /// </summary>
    public async Task<bool> InviteAsync(string roomId, string targetUid,
                                        string? targetHomeServer = null, CancellationToken ct = default)
    {
        var cmd = string.IsNullOrWhiteSpace(targetHomeServer)
            ? $"ROOM-INVITE {roomId} {targetUid}"
            : $"ROOM-INVITE {roomId} {targetUid} {targetHomeServer}";
        var response = await _account.SendCommandAsync(cmd, ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (!ok) Logger.Log($"RoomService: ROOM-INVITE failed: {response}");
        return ok;
    }

    /// <summary>Accept a pending invite and join the room.</summary>
    public async Task<bool> JoinAsync(string roomId, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-JOIN {roomId}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (ok)
        {
            TrackRoom(roomId);
            Logger.Log($"RoomService: joined room {roomId}");
        }
        else
        {
            Logger.Log($"RoomService: ROOM-JOIN failed: {response}");
        }
        return ok;
    }

    /// <summary>Leave a room.</summary>
    public async Task<bool> LeaveAsync(string roomId, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-LEAVE {roomId}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (ok)
        {
            UntrackRoom(roomId);
            _keyStore.DeleteKey(roomId);
            _adminRelayKey.TryRemove(roomId, out _);
            Logger.Log($"RoomService: left room {roomId}");
        }
        else
        {
            Logger.Log($"RoomService: ROOM-LEAVE failed: {response}");
        }
        return ok;
    }

    /// <summary>Retrieve the current member list. Returns empty list on failure.</summary>
    public async Task<IReadOnlyList<RoomMember>> GetMembersAsync(string roomId, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-MEMBERS {roomId}", ct);
        if (response == null || !response.StartsWith("MEMBERS ", StringComparison.Ordinal))
        {
            Logger.Log($"RoomService: ROOM-MEMBERS failed: {response}");
            return Array.Empty<RoomMember>();
        }
        return ParseMembers(response[8..]);
    }

    /// <summary>Kick a member (requires Moderator or Admin role).</summary>
    public async Task<bool> KickAsync(string roomId, string targetUid, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-KICK {roomId} {targetUid}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (!ok) Logger.Log($"RoomService: ROOM-KICK failed: {response}");
        return ok;
    }

    /// <summary>Ban a fingerprint from the room (Admin only).</summary>
    public async Task<bool> BanAsync(string roomId, string fingerprint, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-BAN {roomId} {fingerprint}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (!ok) Logger.Log($"RoomService: ROOM-BAN failed: {response}");
        return ok;
    }

    /// <summary>Transfer admin role to another member (Admin only).</summary>
    public async Task<bool> TransferAdminAsync(string roomId, string newAdminUid, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-TRANSFER-ADMIN {roomId} {newAdminUid}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (!ok) Logger.Log($"RoomService: ROOM-TRANSFER-ADMIN failed: {response}");
        return ok;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Messaging (server-routed / offline-admin mode only)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Send an encrypted message via the server (offline-admin mode).
    /// When admin is online, callers should use the P2P relay session instead.
    /// Returns false if the server rejects the message (e.g. admin is online → wrong-mode).
    /// </summary>
    public async Task<bool> SendServerMessageAsync(string roomId, string ciphertextHex, CancellationToken ct = default)
    {
        var response = await _account.SendCommandAsync($"ROOM-MSG {roomId} {ciphertextHex}", ct);
        var ok = response?.StartsWith("OK", StringComparison.Ordinal) == true;
        if (!ok) Logger.Log($"RoomService: ROOM-MSG failed: {response}");
        return ok;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Admin mode helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// True when the room admin is online; members should prefer P2P via the relay key.
    /// </summary>
    public bool IsAdminOnline(string roomId) =>
        _adminRelayKey.TryGetValue(roomId, out var key) && key != null;

    /// <summary>
    /// Get the relay session key to use for P2P connection to the admin.
    /// Returns null when admin is offline or room is unknown.
    /// </summary>
    public string? GetAdminRelayKey(string roomId) =>
        _adminRelayKey.TryGetValue(roomId, out var key) ? key : null;

    // ═══════════════════════════════════════════════════════════════
    //  Key store pass-through
    // ═══════════════════════════════════════════════════════════════

    public byte[]? GetRoomKey(string roomId)    => _keyStore.GetKey(roomId);
    public void    SetRoomKey(string roomId, byte[] key) => _keyStore.SetKey(roomId, key);
    public void    DeleteRoomKey(string roomId)  => _keyStore.DeleteKey(roomId);

    // ═══════════════════════════════════════════════════════════════
    //  Incoming push dispatcher
    // ═══════════════════════════════════════════════════════════════

    private void OnPush(string line)
    {
        try
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            switch (parts[0].ToUpperInvariant())
            {
                case "ROOM-INVITED" when parts.Length >= 3:
                    RoomInviteReceived?.Invoke(parts[1], parts[2]);
                    break;

                case "ROOM-ADMIN-ONLINE" when parts.Length >= 3:
                    _adminRelayKey[parts[1]] = parts[2];
                    AdminOnline?.Invoke(parts[1], parts[2]);
                    break;

                case "ROOM-ADMIN-OFFLINE" when parts.Length >= 2:
                    _adminRelayKey[parts[1]] = null;
                    AdminOffline?.Invoke(parts[1]);
                    break;

                // ROOM-DELIVER <roomId> <senderUid> <ciphertextHex>
                case "ROOM-DELIVER" when parts.Length >= 4:
                    MessageDelivered?.Invoke(parts[1], parts[2], parts[3]);
                    break;

                case "ROOM-MEMBER-JOINED" when parts.Length >= 3:
                    MemberJoined?.Invoke(parts[1], parts[2]);
                    break;

                case "ROOM-MEMBER-LEFT" when parts.Length >= 3:
                    MemberLeft?.Invoke(parts[1], parts[2]);
                    break;

                case "ROOM-REKEY" when parts.Length >= 2:
                    RekeyRequired?.Invoke(parts[1]);
                    break;

                // ROOM-AWAITING-KEY <roomId> <newMemberUid>
                case "ROOM-AWAITING-KEY" when parts.Length >= 3:
                    KeyDeliveryRequired?.Invoke(parts[1], parts[2]);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"RoomService: push dispatch error: {ex.Message}");
        }
    }

    private void OnConnectionLost()
    {
        // Mark all rooms' admin state as unknown until we reconnect
        foreach (var roomId in _adminRelayKey.Keys.ToList())
            _adminRelayKey[roomId] = null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private void TrackRoom(string roomId)
    {
        var ids = _settings.Settings.JoinedRoomIds;
        if (!ids.Contains(roomId, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(roomId);
            try { _settings.Save(AppServices.Passphrase); } catch { }
        }
    }

    private void UntrackRoom(string roomId)
    {
        var ids = _settings.Settings.JoinedRoomIds;
        if (ids.RemoveAll(id => string.Equals(id, roomId, StringComparison.OrdinalIgnoreCase)) > 0)
            try { _settings.Save(AppServices.Passphrase); } catch { }
    }

    private static IReadOnlyList<RoomMember> ParseMembers(string payload)
    {
        // Format: uid:role:homeServer|uid:role:homeServer|…
        var result = new List<RoomMember>();
        foreach (var token in payload.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = token.Split(':');
            if (fields.Length < 2) continue;
            var uid        = fields[0].Trim();
            var role       = int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
            var homeServer = fields.Length >= 3 && fields[2] != "local" ? fields[2] : null;
            result.Add(new RoomMember(uid, role, homeServer));
        }
        return result;
    }
}

/// <summary>Lightweight room member info returned by RoomService.GetMembersAsync.</summary>
public sealed record RoomMember(string Uid, int Role, string? HomeServer)
{
    public bool IsAdmin     => Role == 2;
    public bool IsModerator => Role == 1;
}
