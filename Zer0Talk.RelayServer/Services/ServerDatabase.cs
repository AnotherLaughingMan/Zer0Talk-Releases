using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Zer0Talk.RelayServer.Services;

/// <summary>
/// SQLite database for the hosted server mode.
/// Stores persistent user accounts, room definitions, memberships, and bans.
/// Messages are NEVER persisted — only in-memory ring buffers.
/// </summary>
public sealed class ServerDatabase : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public ServerDatabase(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, "server.db");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(ct);

        // Enable WAL mode for better concurrency
        await ExecuteNonQueryAsync("PRAGMA journal_mode=WAL;", ct);
        await ExecuteNonQueryAsync("PRAGMA foreign_keys=ON;", ct);
        await ExecuteNonQueryAsync("PRAGMA synchronous=NORMAL;", ct);

        var currentVersion = await GetSchemaVersionAsync(ct);
        if (currentVersion < SchemaVersion)
        {
            await MigrateAsync(currentVersion, ct);
        }
    }

    public SqliteConnection GetConnection()
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized — call InitializeAsync first.");
        return _connection;
    }

    private async Task<int> GetSchemaVersionAsync(CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is long v ? (int)v : 0;
    }

    private async Task SetSchemaVersionAsync(int version, CancellationToken ct)
    {
        // PRAGMA user_version does not support parameters
        await ExecuteNonQueryAsync($"PRAGMA user_version = {version};", ct);
    }

    private async Task MigrateAsync(int fromVersion, CancellationToken ct)
    {
        if (fromVersion < 1)
        {
            await ApplySchema1Async(ct);
        }

        await SetSchemaVersionAsync(SchemaVersion, ct);
    }

    private async Task ApplySchema1Async(CancellationToken ct)
    {
        const string ddl = """
            CREATE TABLE IF NOT EXISTS Users (
                uid          TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                public_key   BLOB NOT NULL,
                auth_token   TEXT NOT NULL,
                registered_at INTEGER NOT NULL,
                last_seen    INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Rooms (
                room_id          TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                display_name     TEXT NOT NULL,
                admin_uid        TEXT NOT NULL COLLATE NOCASE,
                home_server      TEXT NOT NULL,
                created_at       INTEGER NOT NULL,
                member_cap       INTEGER NOT NULL DEFAULT 12,
                room_public_key  BLOB
            );

            CREATE TABLE IF NOT EXISTS RoomMembers (
                room_id      TEXT NOT NULL COLLATE NOCASE,
                uid          TEXT NOT NULL COLLATE NOCASE,
                role         INTEGER NOT NULL DEFAULT 0,
                home_server  TEXT,
                joined_at    INTEGER NOT NULL,
                PRIMARY KEY (room_id, uid),
                FOREIGN KEY (room_id) REFERENCES Rooms(room_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS BannedFingerprints (
                room_id      TEXT NOT NULL COLLATE NOCASE,
                fingerprint  TEXT NOT NULL COLLATE NOCASE,
                banned_at    INTEGER NOT NULL,
                banned_by_uid TEXT NOT NULL COLLATE NOCASE,
                PRIMARY KEY (room_id, fingerprint),
                FOREIGN KEY (room_id) REFERENCES Rooms(room_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS UsedNonces (
                nonce        TEXT NOT NULL PRIMARY KEY,
                used_at      INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_roommembers_uid ON RoomMembers(uid);
            CREATE INDEX IF NOT EXISTS idx_rooms_admin ON Rooms(admin_uid);
            CREATE INDEX IF NOT EXISTS idx_usednonces_used_at ON UsedNonces(used_at);
            """;

        await ExecuteNonQueryAsync(ddl, ct);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Purges nonces older than 10 minutes to bound the UsedNonces table size.</summary>
    public async Task PruneOldNoncesAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "DELETE FROM UsedNonces WHERE used_at < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        try { _connection?.Close(); } catch { }
        _connection?.Dispose();
        _connection = null;
    }
}
