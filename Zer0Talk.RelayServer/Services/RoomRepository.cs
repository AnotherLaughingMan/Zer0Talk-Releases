using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Zer0Talk.RelayServer.Services;

public sealed record RoomInfo(
    string RoomId,
    string DisplayName,
    string AdminUid,
    string HomeServer,
    long CreatedAt,
    int MemberCap,
    byte[]? RoomPublicKey
);

public sealed record RoomMemberInfo(
    string Uid,
    RoomRole Role,
    string? HomeServer,
    long JoinedAt
);

/// <summary>
/// Persistent room, membership, and ban operations for hosted server mode.
/// All queries use parameterized statements — no string interpolation in SQL.
/// </summary>
public sealed class RoomRepository
{
    private readonly ServerDatabase _db;
    private readonly string _homeServerAddress;

    public RoomRepository(ServerDatabase db, string homeServerAddress)
    {
        _db = db;
        _homeServerAddress = homeServerAddress;
    }

    // ── Room CRUD ───────────────────────────────────────────────

    /// <summary>Creates a new room. Returns the generated room ID, or null if creation failed.</summary>
    public async Task<string?> CreateRoomAsync(string adminUid, string displayName, int memberCap, CancellationToken ct = default)
    {
        var roomId = GenerateRoomId();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO Rooms(room_id, display_name, admin_uid, home_server, created_at, member_cap)
            VALUES(@room_id, @display_name, @admin_uid, @home_server, @created_at, @member_cap);
            """;
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@display_name", displayName);
        cmd.Parameters.AddWithValue("@admin_uid", adminUid);
        cmd.Parameters.AddWithValue("@home_server", _homeServerAddress);
        cmd.Parameters.AddWithValue("@created_at", now);
        cmd.Parameters.AddWithValue("@member_cap", memberCap);

        try
        {
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows <= 0) return null;

            // Add admin as a member with Admin role
            await AddMemberAsync(roomId, adminUid, RoomRole.Admin, null, ct);
            return roomId;
        }
        catch
        {
            return null;
        }
    }

    public async Task<RoomInfo?> GetRoomAsync(string roomId, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT room_id, display_name, admin_uid, home_server, created_at, member_cap, room_public_key FROM Rooms WHERE room_id = @room_id;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new RoomInfo(
            RoomId: reader.GetString(0),
            DisplayName: reader.GetString(1),
            AdminUid: reader.GetString(2),
            HomeServer: reader.GetString(3),
            CreatedAt: reader.GetInt64(4),
            MemberCap: reader.GetInt32(5),
            RoomPublicKey: reader.IsDBNull(6) ? null : (byte[])reader[6]
        );
    }

    public async Task<bool> RoomExistsAsync(string roomId, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Rooms WHERE room_id = @room_id;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        return (long)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    public async Task<bool> UpdateAdminAsync(string roomId, string newAdminUid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "UPDATE Rooms SET admin_uid = @admin_uid WHERE room_id = @room_id;";
        cmd.Parameters.AddWithValue("@admin_uid", newAdminUid);
        cmd.Parameters.AddWithValue("@room_id", roomId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task DeleteRoomAsync(string roomId, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        // Cascade deletes RoomMembers and BannedFingerprints via FK
        cmd.CommandText = "DELETE FROM Rooms WHERE room_id = @room_id;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Membership ──────────────────────────────────────────────

    public async Task<int> GetMemberCountAsync(string roomId, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM RoomMembers WHERE room_id = @room_id;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<bool> IsMemberAsync(string roomId, string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM RoomMembers WHERE room_id = @room_id AND uid = @uid;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@uid", uid);
        return (long)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    public async Task<RoomRole?> GetMemberRoleAsync(string roomId, string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT role FROM RoomMembers WHERE room_id = @room_id AND uid = @uid;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@uid", uid);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long role ? (RoomRole)(int)role : null;
    }

    public async Task<bool> AddMemberAsync(string roomId, string uid, RoomRole role, string? homeServer, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO RoomMembers(room_id, uid, role, home_server, joined_at)
            VALUES(@room_id, @uid, @role, @home_server, @joined_at)
            ON CONFLICT(room_id, uid) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@uid", uid);
        cmd.Parameters.AddWithValue("@role", (int)role);
        cmd.Parameters.AddWithValue("@home_server", homeServer is null ? DBNull.Value : (object)homeServer);
        cmd.Parameters.AddWithValue("@joined_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> RemoveMemberAsync(string roomId, string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "DELETE FROM RoomMembers WHERE room_id = @room_id AND uid = @uid;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@uid", uid);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SetMemberRoleAsync(string roomId, string uid, RoomRole role, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "UPDATE RoomMembers SET role = @role WHERE room_id = @room_id AND uid = @uid;";
        cmd.Parameters.AddWithValue("@role", (int)role);
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@uid", uid);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<List<RoomMemberInfo>> GetMembersAsync(string roomId, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT uid, role, home_server, joined_at FROM RoomMembers WHERE room_id = @room_id ORDER BY joined_at ASC;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<RoomMemberInfo>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new RoomMemberInfo(
                Uid: reader.GetString(0),
                Role: (RoomRole)reader.GetInt32(1),
                HomeServer: reader.IsDBNull(2) ? null : reader.GetString(2),
                JoinedAt: reader.GetInt64(3)
            ));
        }
        return results;
    }

    /// <summary>Count rooms where this user is a member (any role).</summary>
    public async Task<int> CountRoomsForUserAsync(string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM RoomMembers WHERE uid = @uid;";
        cmd.Parameters.AddWithValue("@uid", uid);
        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // ── Bans ────────────────────────────────────────────────────

    /// <summary>Bans a fingerprint from a room. Fingerprint = SHA-256(uid)[..32 hex chars].</summary>
    public async Task BanFingerprintAsync(string roomId, string fingerprint, string bannedByUid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO BannedFingerprints(room_id, fingerprint, banned_at, banned_by_uid)
            VALUES(@room_id, @fingerprint, @banned_at, @banned_by_uid);
            """;
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@fingerprint", fingerprint);
        cmd.Parameters.AddWithValue("@banned_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@banned_by_uid", bannedByUid);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> IsBannedAsync(string roomId, string uid, CancellationToken ct = default)
    {
        var fingerprint = BuildUidFingerprint(uid);
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM BannedFingerprints WHERE room_id = @room_id AND fingerprint = @fingerprint;";
        cmd.Parameters.AddWithValue("@room_id", roomId);
        cmd.Parameters.AddWithValue("@fingerprint", fingerprint);
        return (long)(await cmd.ExecuteScalarAsync(ct))! > 0;
    }

    public static string BuildUidFingerprint(string uid)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uid ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateRoomId()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
