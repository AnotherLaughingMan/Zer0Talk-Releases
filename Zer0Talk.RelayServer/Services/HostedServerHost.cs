using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sodium;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// TCP listener for the hosted server mode.
/// Handles persistent user account registration (ACCOUNT-REG / ACCOUNT-AUTH)
/// and room lifecycle (ROOM-CREATE / ROOM-INVITE / ROOM-JOIN / ROOM-LEAVE /
/// ROOM-KICK / ROOM-BAN / ROOM-TRANSFER-ADMIN / ROOM-MEMBERS / ROOM-MSG).
///
/// Phase 2 — Challenge-response account registration
/// Phase 3 — Room management protocol
/// Phase 4 — Hybrid room message routing
/// Phase 5 — Group key management signals
/// </summary>
public sealed class HostedServerHost : IDisposable
{
    private readonly RelayConfig _config;
    private readonly ServerDatabase _db;
    private readonly UserRepository _users;
    private readonly RoomRepository _rooms;
    private readonly string _serverAddress;

    // Connected + authenticated sessions: uid → context
    private readonly ConcurrentDictionary<string, HostedSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    // S2S room federation (null when PeerHostedServers is empty)
    private readonly RoomFederationManager? _federation;

    // Per-room in-memory ring buffer for offline-admin message routing
    private readonly ConcurrentDictionary<string, RoomMessageQueue> _messageQueues = new(StringComparer.OrdinalIgnoreCase);

    // Per-room admin online state: room_id → admin uid (null = offline)
    private readonly ConcurrentDictionary<string, string?> _adminOnline = new(StringComparer.OrdinalIgnoreCase);

    // Pending room invites: target_uid → List<(roomId, inviterUid)>
    private readonly ConcurrentDictionary<string, List<(string roomId, string inviterUid)>> _pendingInvites = new(StringComparer.OrdinalIgnoreCase);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _maintenanceTask;

    public event Action<string>? Log;

    public bool IsRunning { get; private set; }

    public HostedServerHost(RelayConfig config, ServerDatabase db, string serverAddress, RoomFederationManager? federation = null)
    {
        _config = config;
        _db = db;
        _users = new UserRepository(db);
        _rooms = new RoomRepository(db, serverAddress);
        _serverAddress = serverAddress;
        _federation = federation;

        if (_federation != null)
        {
            _federation.IncomingNotification += DeliverNotificationAsync;
            _federation.IncomingInvite        += QueueFederatedInviteAsync;
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        await _db.InitializeAsync(ct);
        Log?.Invoke($"Hosted server DB initialized | path={RelayConfigStore.GetDataDirectoryPath(_config)}");

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _config.HostingPort);
        _listener.Start();
        IsRunning = true;

        Log?.Invoke($"Hosted server listening on port {_config.HostingPort}");

        _acceptTask = AcceptLoopAsync(_cts.Token);
        _maintenanceTask = MaintenanceLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        IsRunning = false;
        // Clear in-memory session and routing state so restart sees clean slate
        _sessions.Clear();
        _messageQueues.Clear();
        _adminOnline.Clear();
        _pendingInvites.Clear();
        Log?.Invoke("Hosted server stopped");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Accept loop
    // ═══════════════════════════════════════════════════════════════

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch
            {
                if (!IsRunning) break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        string? authenticatedUid = null;
        var stream = client.GetStream();
        try
        {
            client.ReceiveTimeout = 30_000;
            client.SendTimeout = 10_000;

            // Phase 2: Challenge-response authentication
            var nonce = GenerateNonce();
            await WriteLineAsync(stream, $"CHALLENGE {nonce}", ct);

            var firstLine = await ReadLineAsync(stream, ct);
            if (string.IsNullOrWhiteSpace(firstLine)) return;

            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var cmd = parts[0].ToUpperInvariant();

            // ── ACCOUNT-REG <uid> <pubkeyHex> <sig-of-nonce-hex> ──
            if (cmd == "ACCOUNT-REG")
            {
                authenticatedUid = await HandleAccountRegAsync(parts, nonce, stream, ct);
            }
            // ── ACCOUNT-AUTH <uid> <authToken> ──
            else if (cmd == "ACCOUNT-AUTH")
            {
                authenticatedUid = await HandleAccountAuthAsync(parts, stream, ct);
            }
            else
            {
                await WriteLineAsync(stream, "ERR unauthenticated", ct);
                return;
            }

            if (authenticatedUid == null) return;

            // Register session
            var session = new HostedSession(authenticatedUid, stream, client);
            _sessions[authenticatedUid] = session;
            await _users.UpdateLastSeenAsync(authenticatedUid, ct);

            Log?.Invoke($"Hosted session started | uid={Fingerprint(authenticatedUid, 8)}");

            // Notify admin-online state for any rooms this user admins
            await NotifyAdminOnlineAsync(authenticatedUid, ct);

            // Deliver any pending invites
            await DeliverPendingInvitesAsync(authenticatedUid, ct);

            // Command loop
            await CommandLoopAsync(session, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke($"Hosted session error | uid={Fingerprint(authenticatedUid ?? "?", 8)} | {ex.Message}");
        }
        finally
        {
            if (authenticatedUid != null)
            {
                _sessions.TryRemove(authenticatedUid, out _);
                await NotifyAdminOfflineAsync(authenticatedUid, ct);
                Log?.Invoke($"Hosted session ended | uid={Fingerprint(authenticatedUid, 8)}");
                try { await _users.UpdateLastSeenAsync(authenticatedUid, ct); } catch { }
            }
            SafeClose(client);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 2: Account registration / authentication
    // ═══════════════════════════════════════════════════════════════

    private async Task<string?> HandleAccountRegAsync(string[] parts, string nonce, NetworkStream stream, CancellationToken ct)
    {
        // ACCOUNT-REG <uid> <pubkeyHex> <sig-of-nonce-hex>
        if (parts.Length < 4)
        {
            await WriteLineAsync(stream, "ERR bad-reg", ct);
            return null;
        }

        var uid = parts[1].Trim();
        var pubKeyHex = parts[2].Trim();
        var sigHex = parts[3].Trim();

        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(pubKeyHex) || string.IsNullOrWhiteSpace(sigHex))
        {
            await WriteLineAsync(stream, "ERR bad-reg", ct);
            return null;
        }

        byte[] publicKey, sig;
        try
        {
            publicKey = Convert.FromHexString(pubKeyHex);
            sig = Convert.FromHexString(sigHex);
        }
        catch
        {
            await WriteLineAsync(stream, "ERR bad-reg", ct);
            return null;
        }

        // Replay protection: consume the nonce
        if (!await _users.TryConsumeNonceAsync(nonce, ct))
        {
            await WriteLineAsync(stream, "ERR nonce-replayed", ct);
            return null;
        }

        // Verify Ed25519 signature of nonce
        if (!VerifyEd25519Signature(publicKey, Encoding.UTF8.GetBytes(nonce), sig))
        {
            await WriteLineAsync(stream, "ERR bad-signature", ct);
            return null;
        }

        // Enforce user cap
        var userCount = await _users.CountAsync(ct);
        if (userCount >= _config.MaxRegisteredUsers)
        {
            await WriteLineAsync(stream, "ERR server-full", ct);
            return null;
        }

        // Register (INSERT OR IGNORE — existing registration returns null token)
        var token = await _users.RegisterAsync(uid, publicKey, ct);
        if (token == null)
        {
            // Already registered — re-auth them if they provide a valid token later
            // Here we respond with ERR already-registered so client knows to use ACCOUNT-AUTH
            await WriteLineAsync(stream, "ERR already-registered", ct);
            return null;
        }

        await WriteLineAsync(stream, $"OK {token}", ct);
        return uid;
    }

    private async Task<string?> HandleAccountAuthAsync(string[] parts, NetworkStream stream, CancellationToken ct)
    {
        // ACCOUNT-AUTH <uid> <authToken>
        if (parts.Length < 3)
        {
            await WriteLineAsync(stream, "ERR bad-auth", ct);
            return null;
        }

        var uid = parts[1].Trim();
        var token = parts[2].Trim();

        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(token))
        {
            await WriteLineAsync(stream, "ERR bad-auth", ct);
            return null;
        }

        if (!await _users.AuthenticateAsync(uid, token, ct))
        {
            await WriteLineAsync(stream, "ERR invalid", ct);
            return null;
        }

        await WriteLineAsync(stream, "OK", ct);
        return uid;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 3 + 4: Command loop
    // ═══════════════════════════════════════════════════════════════

    private async Task CommandLoopAsync(HostedSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && session.Client.Connected)
        {
            var line = await ReadLineAsync(session.Stream, ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var cmd = parts[0].ToUpperInvariant();

            try
            {
                switch (cmd)
                {
                    case "ROOM-CREATE":
                        await HandleRoomCreateAsync(session, parts, ct);
                        break;
                    case "ROOM-INVITE":
                        await HandleRoomInviteAsync(session, parts, ct);
                        break;
                    case "ROOM-JOIN":
                        await HandleRoomJoinAsync(session, parts, ct);
                        break;
                    case "ROOM-LEAVE":
                        await HandleRoomLeaveAsync(session, parts, ct);
                        break;
                    case "ROOM-MEMBERS":
                        await HandleRoomMembersAsync(session, parts, ct);
                        break;
                    case "ROOM-KICK":
                        await HandleRoomKickAsync(session, parts, ct);
                        break;
                    case "ROOM-BAN":
                        await HandleRoomBanAsync(session, parts, ct);
                        break;
                    case "ROOM-TRANSFER-ADMIN":
                        await HandleRoomTransferAdminAsync(session, parts, ct);
                        break;
                    case "ROOM-MSG":
                        await HandleRoomMsgAsync(session, parts, ct);
                        break;
                    case "PING":
                        await WriteLineAsync(session.Stream, "PONG", ct);
                        break;
                    default:
                        await WriteLineAsync(session.Stream, "ERR unknown-command", ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Command error | uid={Fingerprint(session.Uid, 8)} | cmd={cmd} | {ex.Message}");
                try { await WriteLineAsync(session.Stream, "ERR internal", ct); } catch { }
            }
        }
    }

    // ── ROOM-CREATE <name> <memberCap> ──
    private async Task HandleRoomCreateAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(session.Stream, "ERR bad-args", ct);
            return;
        }

        var displayName = parts[1].Trim();
        if (!int.TryParse(parts[2], out var cap))
        {
            await WriteLineAsync(session.Stream, "ERR bad-cap", ct);
            return;
        }

        cap = Math.Clamp(cap, 2, _config.MaxMembersPerRoom);

        // Enforce room cap per user
        var existingRoomCount = await _rooms.CountRoomsForUserAsync(session.Uid, ct);
        if (existingRoomCount >= _config.MaxRoomsPerUser)
        {
            await WriteLineAsync(session.Stream, "ERR too-many-rooms", ct);
            return;
        }

        var roomId = await _rooms.CreateRoomAsync(session.Uid, displayName, cap, ct);
        if (roomId == null)
        {
            await WriteLineAsync(session.Stream, "ERR create-failed", ct);
            return;
        }

        _adminOnline[roomId] = session.Uid;
        Log?.Invoke($"Room created | room={roomId} admin={Fingerprint(session.Uid, 8)} cap={cap}");
        await WriteLineAsync(session.Stream, $"OK {roomId}", ct);
    }

    // ── ROOM-INVITE <roomId> <targetUid> ──
    private async Task HandleRoomInviteAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3)
        {
            await WriteLineAsync(session.Stream, "ERR bad-args", ct);
            return;
        }

        var roomId = parts[1].Trim();
        var targetUid = parts[2].Trim();

        var room = await _rooms.GetRoomAsync(roomId, ct);
        if (room == null)
        {
            await WriteLineAsync(session.Stream, "ERR no-room", ct);
            return;
        }

        // Only Admin/Moderator can invite
        var role = await _rooms.GetMemberRoleAsync(roomId, session.Uid, ct);
        if (role == null || role < RoomRole.Moderator)
        {
            await WriteLineAsync(session.Stream, "ERR no-permission", ct);
            return;
        }

        if (await _rooms.IsBannedAsync(roomId, targetUid, ct))
        {
            await WriteLineAsync(session.Stream, "ERR banned", ct);
            return;
        }

        if (await _rooms.IsMemberAsync(roomId, targetUid, ct))
        {
            await WriteLineAsync(session.Stream, "ERR already-member", ct);
            return;
        }

        // Queue the invite
        var list = _pendingInvites.GetOrAdd(targetUid, _ => new List<(string, string)>());
        lock (list) { list.Add((roomId, session.Uid)); }

        // Push to target if currently connected locally
        if (_sessions.TryGetValue(targetUid, out var targetSession))
        {
            try { await WriteLineAsync(targetSession.Stream, $"ROOM-INVITED {roomId} {session.Uid}", ct); }
            catch { }
        }

        // Route to foreign server if target specified a home server (S2S cross-server invite)
        // ROOM-INVITE <roomId> <targetUid> [targetHomeServer]
        if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) && _federation != null)
        {
            var targetHomeServer = parts[3].Trim();
            await _federation.InviteMemberAsync(targetHomeServer, targetUid, roomId, session.Uid, ct);
        }

        await WriteLineAsync(session.Stream, "OK", ct);
    }

    // ── ROOM-JOIN <roomId> ──
    private async Task HandleRoomJoinAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2)
        {
            await WriteLineAsync(session.Stream, "ERR bad-args", ct);
            return;
        }

        var roomId = parts[1].Trim();
        var room = await _rooms.GetRoomAsync(roomId, ct);
        if (room == null) { await WriteLineAsync(session.Stream, "ERR no-room", ct); return; }

        if (await _rooms.IsBannedAsync(roomId, session.Uid, ct)) { await WriteLineAsync(session.Stream, "ERR banned", ct); return; }
        if (await _rooms.IsMemberAsync(roomId, session.Uid, ct)) { await WriteLineAsync(session.Stream, "ERR already-member", ct); return; }

        // Verify invite exists (or user is admin joining own room)
        var hasInvite = false;
        if (_pendingInvites.TryGetValue(session.Uid, out var invites))
        {
            lock (invites) { hasInvite = invites.Exists(i => i.roomId == roomId); }
        }

        if (!hasInvite)
        {
            await WriteLineAsync(session.Stream, "ERR no-invite", ct);
            return;
        }

        var count = await _rooms.GetMemberCountAsync(roomId, ct);
        if (count >= room.MemberCap) { await WriteLineAsync(session.Stream, "ERR full", ct); return; }

        await _rooms.AddMemberAsync(roomId, session.Uid, RoomRole.Member, null, ct);

        // Remove invite
        if (_pendingInvites.TryGetValue(session.Uid, out var inv))
            lock (inv) { inv.RemoveAll(i => i.roomId == roomId); }

        // Broadcast member-joined to room
        await BroadcastToRoomAsync(roomId, $"ROOM-MEMBER-JOINED {roomId} {session.Uid}", ct);

        // Tell admin to distribute group key to new member
        if (_adminOnline.TryGetValue(roomId, out var adminUid) && adminUid != null &&
            _sessions.TryGetValue(adminUid, out var adminSession))
        {
            try { await WriteLineAsync(adminSession.Stream, $"ROOM-AWAITING-KEY {roomId} {session.Uid}", ct); }
            catch { }
        }

        // Inform joining member of current admin mode
        await WriteAdminModeToSessionAsync(session, roomId, ct);

        await WriteLineAsync(session.Stream, "OK", ct);
        Log?.Invoke($"Room join | room={roomId} uid={Fingerprint(session.Uid, 8)}");
    }

    // ── ROOM-LEAVE <roomId> ──
    private async Task HandleRoomLeaveAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        if (!await _rooms.IsMemberAsync(roomId, session.Uid, ct)) { await WriteLineAsync(session.Stream, "ERR not-member", ct); return; }

        await _rooms.RemoveMemberAsync(roomId, session.Uid, ct);
        await BroadcastToRoomAsync(roomId, $"ROOM-MEMBER-LEFT {roomId} {session.Uid}", ct);
        await SignalRekeyAsync(roomId, ct);
        await WriteLineAsync(session.Stream, "OK", ct);
    }

    // ── ROOM-MEMBERS <roomId> ──
    private async Task HandleRoomMembersAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 2) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        if (!await _rooms.IsMemberAsync(roomId, session.Uid, ct)) { await WriteLineAsync(session.Stream, "ERR not-member", ct); return; }

        var members = await _rooms.GetMembersAsync(roomId, ct);
        var sb = new StringBuilder("MEMBERS ");
        for (var i = 0; i < members.Count; i++)
        {
            var m = members[i];
            sb.Append($"{m.Uid}:{(int)m.Role}:{m.HomeServer ?? "local"}");
            if (i < members.Count - 1) sb.Append('|');
        }
        await WriteLineAsync(session.Stream, sb.ToString(), ct);
    }

    // ── ROOM-KICK <roomId> <targetUid> ──
    private async Task HandleRoomKickAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        var targetUid = parts[2].Trim();

        var myRole = await _rooms.GetMemberRoleAsync(roomId, session.Uid, ct);
        if (myRole == null || myRole < RoomRole.Moderator) { await WriteLineAsync(session.Stream, "ERR no-permission", ct); return; }

        var targetRole = await _rooms.GetMemberRoleAsync(roomId, targetUid, ct);
        if (targetRole == null) { await WriteLineAsync(session.Stream, "ERR not-member", ct); return; }

        // Moderators cannot kick Admins or other Moderators
        if (myRole == RoomRole.Moderator && targetRole >= RoomRole.Moderator)
        {
            await WriteLineAsync(session.Stream, "ERR no-permission", ct);
            return;
        }

        await _rooms.RemoveMemberAsync(roomId, targetUid, ct);
        await BroadcastToRoomAsync(roomId, $"ROOM-MEMBER-LEFT {roomId} {targetUid}", ct);
        await SignalRekeyAsync(roomId, ct);
        await WriteLineAsync(session.Stream, "OK", ct);
        Log?.Invoke($"Room kick | room={roomId} by={Fingerprint(session.Uid, 8)} target={Fingerprint(targetUid, 8)}");
    }

    // ── ROOM-BAN <roomId> <fingerprint> ──
    private async Task HandleRoomBanAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        var fingerprint = parts[2].Trim();

        var myRole = await _rooms.GetMemberRoleAsync(roomId, session.Uid, ct);
        if (myRole == null || myRole < RoomRole.Admin) { await WriteLineAsync(session.Stream, "ERR no-permission", ct); return; }

        await _rooms.BanFingerprintAsync(roomId, fingerprint, session.Uid, ct);
        await WriteLineAsync(session.Stream, "OK", ct);
    }

    // ── ROOM-TRANSFER-ADMIN <roomId> <targetUid> ──
    private async Task HandleRoomTransferAdminAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        var targetUid = parts[2].Trim();

        var room = await _rooms.GetRoomAsync(roomId, ct);
        if (room == null) { await WriteLineAsync(session.Stream, "ERR no-room", ct); return; }

        if (!string.Equals(room.AdminUid, session.Uid, StringComparison.OrdinalIgnoreCase))
        {
            await WriteLineAsync(session.Stream, "ERR no-permission", ct);
            return;
        }

        if (!await _rooms.IsMemberAsync(roomId, targetUid, ct)) { await WriteLineAsync(session.Stream, "ERR not-member", ct); return; }

        await _rooms.SetMemberRoleAsync(roomId, targetUid, RoomRole.Admin, ct);
        await _rooms.SetMemberRoleAsync(roomId, session.Uid, RoomRole.Moderator, ct);
        await _rooms.UpdateAdminAsync(roomId, targetUid, ct);

        // Update in-memory admin tracking
        _adminOnline.TryUpdate(roomId, _sessions.ContainsKey(targetUid) ? targetUid : null, room.AdminUid);

        await BroadcastToRoomAsync(roomId, $"ROOM-ADMIN-CHANGED {roomId} {targetUid}", ct);
        await WriteLineAsync(session.Stream, "OK", ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 4: Hybrid room message routing
    // ═══════════════════════════════════════════════════════════════

    // ── ROOM-MSG <roomId> <ciphertextHex> ──
    private async Task HandleRoomMsgAsync(HostedSession session, string[] parts, CancellationToken ct)
    {
        if (parts.Length < 3) { await WriteLineAsync(session.Stream, "ERR bad-args", ct); return; }
        var roomId = parts[1].Trim();
        var ciphertext = parts[2].Trim();

        if (!await _rooms.IsMemberAsync(roomId, session.Uid, ct)) { await WriteLineAsync(session.Stream, "ERR not-member", ct); return; }

        // Only accept server-routed messages when admin is offline
        if (_adminOnline.TryGetValue(roomId, out var adminUid) && adminUid != null)
        {
            await WriteLineAsync(session.Stream, "ERR wrong-mode", ct);
            return;
        }

        // Enqueue to ring buffer
        var queue = _messageQueues.GetOrAdd(roomId, _ => new RoomMessageQueue(_config.RoomMessageQueueDepth));
        queue.Enqueue(session.Uid, ciphertext);

        // Fan out to connected members
        await BroadcastToRoomAsync(roomId, $"ROOM-DELIVER {roomId} {session.Uid} {ciphertext}", ct, excludeUid: session.Uid);
        await WriteLineAsync(session.Stream, "OK", ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Admin online / offline state machine
    // ═══════════════════════════════════════════════════════════════

    private async Task NotifyAdminOnlineAsync(string uid, CancellationToken ct)
    {
        // Find rooms where this uid is admin
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT room_id FROM Rooms WHERE admin_uid = @uid;";
        cmd.Parameters.AddWithValue("@uid", uid);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var roomId = reader.GetString(0);
            _adminOnline[roomId] = uid;
            // Build relay session key that members can use to connect P2P to admin
            var relayKey = $"room:{roomId}:admin:{uid}";
            await BroadcastToRoomAsync(roomId, $"ROOM-ADMIN-ONLINE {roomId} {relayKey}", ct, excludeUid: uid);
            Log?.Invoke($"Room admin online | room={roomId} admin={Fingerprint(uid, 8)}");
        }
    }

    private async Task NotifyAdminOfflineAsync(string uid, CancellationToken ct)
    {
        var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT room_id FROM Rooms WHERE admin_uid = @uid;";
        cmd.Parameters.AddWithValue("@uid", uid);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var roomId = reader.GetString(0);
            _adminOnline.TryUpdate(roomId, null, uid);
            await BroadcastToRoomAsync(roomId, $"ROOM-ADMIN-OFFLINE {roomId}", ct);
            Log?.Invoke($"Room admin offline | room={roomId} admin={Fingerprint(uid, 8)}");
        }
    }

    private async Task WriteAdminModeToSessionAsync(HostedSession session, string roomId, CancellationToken ct)
    {
        if (_adminOnline.TryGetValue(roomId, out var adminUid) && adminUid != null)
        {
            var relayKey = $"room:{roomId}:admin:{adminUid}";
            try { await WriteLineAsync(session.Stream, $"ROOM-ADMIN-ONLINE {roomId} {relayKey}", ct); }
            catch { }
        }
        else
        {
            try { await WriteLineAsync(session.Stream, $"ROOM-ADMIN-OFFLINE {roomId}", ct); }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 5: Group key management signals
    // ═══════════════════════════════════════════════════════════════

    private async Task SignalRekeyAsync(string roomId, CancellationToken ct)
    {
        await BroadcastToRoomAsync(roomId, $"ROOM-REKEY {roomId}", ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Broadcast helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task BroadcastToRoomAsync(string roomId, string message, CancellationToken ct, string? excludeUid = null)
    {
        var members = await _rooms.GetMembersAsync(roomId, ct);
        foreach (var m in members)
        {
            if (excludeUid != null && string.Equals(m.Uid, excludeUid, StringComparison.OrdinalIgnoreCase)) continue;

            if (m.HomeServer != null)
            {
                // Foreign member — route via S2S
                if (_federation != null)
                    await _federation.NotifyMemberAsync(m.HomeServer, m.Uid, message, ct);
            }
            else if (_sessions.TryGetValue(m.Uid, out var s))
            {
                try { await WriteLineAsync(s.Stream, message, ct); }
                catch { }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  S2S federation event handlers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Deliver an incoming S2S notification to a locally-connected user.
    /// Subscribed to RoomFederationManager.IncomingNotification.
    /// </summary>
    private Task DeliverNotificationAsync(string targetUid, string message, CancellationToken ct)
    {
        if (_sessions.TryGetValue(targetUid, out var session))
        {
            try { return WriteLineAsync(session.Stream, message, ct); }
            catch { }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Queue a federated room invite for a locally-registered user.
    /// Subscribed to RoomFederationManager.IncomingInvite.
    /// </summary>
    private async Task QueueFederatedInviteAsync(string targetUid, string roomId, string inviterUid, CancellationToken ct)
    {
        var list = _pendingInvites.GetOrAdd(targetUid, _ => new List<(string, string)>());
        lock (list) { list.Add((roomId, inviterUid)); }

        if (_sessions.TryGetValue(targetUid, out var session))
        {
            try { await WriteLineAsync(session.Stream, $"ROOM-INVITED {roomId} {inviterUid}", ct); }
            catch { }
        }
    }

    private async Task DeliverPendingInvitesAsync(string uid, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(uid, out var session)) return;
        if (!_pendingInvites.TryGetValue(uid, out var invites)) return;
        List<(string roomId, string inviterUid)> snapshot;
        lock (invites) { snapshot = new List<(string, string)>(invites); }
        foreach (var (roomId, inviterUid) in snapshot)
        {
            try { await WriteLineAsync(session.Stream, $"ROOM-INVITED {roomId} {inviterUid}", ct); }
            catch { break; }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Maintenance loop
    // ═══════════════════════════════════════════════════════════════

    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
                await _db.PruneOldNoncesAsync(ct);
            }
            catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cryptography
    // ═══════════════════════════════════════════════════════════════

    private static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifyEd25519Signature(byte[] publicKey, byte[] message, byte[] signature)
    {
        try
        {
            // Ed25519 public key is 32 bytes; signature is 64 bytes
            if (publicKey.Length != 32 || signature.Length != 64) return false;
            return PublicKeyAuth.VerifyDetached(signature, message, publicKey);
        }
        catch
        {
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  I/O helpers
    // ═══════════════════════════════════════════════════════════════

    private static async Task WriteLineAsync(NetworkStream stream, string line, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            var c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
            if (sb.Length > 4096) return null; // Guard against oversized lines
        }
        return sb.ToString();
    }

    private static void SafeClose(TcpClient client) { try { client.Close(); } catch { } }
    private static string Fingerprint(string value, int chars) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))[..Math.Min(chars * 2, 64)].ToLowerInvariant();

    public void Dispose()
    {
        Stop();
        _db.Dispose();
    }
}

// ── Per-session state ─────────────────────────────────────────────
internal sealed class HostedSession
{
    public string Uid { get; }
    public NetworkStream Stream { get; }
    public TcpClient Client { get; }

    public HostedSession(string uid, NetworkStream stream, TcpClient client)
    {
        Uid = uid;
        Stream = stream;
        Client = client;
    }
}
