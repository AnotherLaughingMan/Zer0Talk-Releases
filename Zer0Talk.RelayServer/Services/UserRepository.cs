using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// Role values stored in RoomMembers.role — kept in sync with schema.
/// </summary>
public enum RoomRole
{
    Member = 0,
    Moderator = 1,
    Admin = 2
}

/// <summary>
/// Persistent user account operations for hosted server mode.
/// All queries use parameterized statements — no string interpolation in SQL.
/// </summary>
public sealed class UserRepository
{
    private readonly ServerDatabase _db;

    public UserRepository(ServerDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Checks whether a nonce has already been used, then marks it consumed.
    /// Returns false if the nonce was already consumed (replay attack).
    /// </summary>
    public async Task<bool> TryConsumeNonceAsync(string nonce, CancellationToken ct = default)
    {
        var conn = _db.GetConnection();
        // Check if already used
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM UsedNonces WHERE nonce = @nonce;";
            check.Parameters.AddWithValue("@nonce", nonce);
            var count = (long)(await check.ExecuteScalarAsync(ct))!;
            if (count > 0) return false;
        }
        // Mark as used
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT OR IGNORE INTO UsedNonces(nonce, used_at) VALUES(@nonce, @used_at);";
            insert.Parameters.AddWithValue("@nonce", nonce);
            insert.Parameters.AddWithValue("@used_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await insert.ExecuteNonQueryAsync(ct);
        }
        return true;
    }

    /// <summary>
    /// Registers a new user account. Returns the issued auth token, or null if UID already exists.
    /// </summary>
    public async Task<string?> RegisterAsync(string uid, byte[] publicKey, CancellationToken ct = default)
    {
        var token = GenerateToken();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = """
            INSERT INTO Users(uid, public_key, auth_token, registered_at, last_seen)
            VALUES(@uid, @pk, @token, @now, @now)
            ON CONFLICT(uid) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("@uid", uid);
        cmd.Parameters.AddWithValue("@pk", publicKey);
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@now", now);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0 ? token : null;
    }

    /// <summary>Returns the stored public key for a UID, or null if not found.</summary>
    public async Task<byte[]?> GetPublicKeyAsync(string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT public_key FROM Users WHERE uid = @uid;";
        cmd.Parameters.AddWithValue("@uid", uid);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is byte[] key ? key : null;
    }

    /// <summary>Returns true if the uid + token pair is valid.</summary>
    public async Task<bool> AuthenticateAsync(string uid, string token, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Users WHERE uid = @uid AND auth_token = @token;";
        cmd.Parameters.AddWithValue("@uid", uid);
        cmd.Parameters.AddWithValue("@token", token);
        var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return count > 0;
    }

    /// <summary>Updates last_seen timestamp for a user.</summary>
    public async Task UpdateLastSeenAsync(string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "UPDATE Users SET last_seen = @now WHERE uid = @uid;";
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@uid", uid);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Returns true if a user with this UID exists.</summary>
    public async Task<bool> ExistsAsync(string uid, CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Users WHERE uid = @uid;";
        cmd.Parameters.AddWithValue("@uid", uid);
        var count = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return count > 0;
    }

    /// <summary>Counts total registered users.</summary>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        using var cmd = _db.GetConnection().CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Users;";
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
