using System;

namespace Zer0Talk.RelayServer.Services;

public readonly struct RelayStats
{
    public RelayStats(int pending, int active, long totalConnections, long offerCommands = 0, long pollCommands = 0, long waitPollCommands = 0, long ackCommands = 0, long ackMisses = 0, int registeredClients = 0)
    {
        Pending = pending;
        Active = active;
        TotalConnections = totalConnections;
        OfferCommands = offerCommands;
        PollCommands = pollCommands;
        WaitPollCommands = waitPollCommands;
        AckCommands = ackCommands;
        AckMisses = ackMisses;
        RegisteredClients = registeredClients;
    }

    public int Pending { get; }
    public int Active { get; }
    public long TotalConnections { get; }
    public long OfferCommands { get; }
    public long PollCommands { get; }
    public long WaitPollCommands { get; }
    public long AckCommands { get; }
    public long AckMisses { get; }
    public int RegisteredClients { get; }
}

public sealed class RelayRequest
{
    public string Uid { get; init; } = string.Empty;
    public string TargetUid { get; init; } = string.Empty;
    public string SessionKey { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;

    public static bool TryParse(string line, out RelayRequest request)
    {
        request = new RelayRequest();
        if (string.IsNullOrWhiteSpace(line)) return false;
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!string.Equals(parts[0], "RELAY", StringComparison.OrdinalIgnoreCase)) return false;

        // Phase 2 protocol:
        // - Preferred: RELAY <sessionKey> [I|R]
        // - Legacy:   RELAY <uid> <targetUid> <sessionKey> [I|R]
        if (parts.Length == 2)
        {
            request = new RelayRequest
            {
                SessionKey = parts[1]
            };
            return !string.IsNullOrWhiteSpace(request.SessionKey);
        }

        if (parts.Length == 3)
        {
            request = new RelayRequest
            {
                SessionKey = parts[1],
                Role = NormalizeRole(parts[2])
            };
            return !string.IsNullOrWhiteSpace(request.SessionKey);
        }

        if (parts.Length < 4) return false;

        request = new RelayRequest
        {
            Uid = parts[1],
            TargetUid = parts[2],
            SessionKey = parts[3],
            Role = parts.Length >= 5 ? NormalizeRole(parts[4]) : string.Empty
        };
        return !string.IsNullOrWhiteSpace(request.SessionKey);
    }

    private static string NormalizeRole(string? raw)
    {
        var role = (raw ?? string.Empty).Trim();
        if (string.Equals(role, "I", StringComparison.OrdinalIgnoreCase)) return "I";
        if (string.Equals(role, "R", StringComparison.OrdinalIgnoreCase)) return "R";
        return string.Empty;
    }
}

public sealed class RelaySessionInfo
{
    public RelaySessionInfo(string sessionKey, string leftUid, string rightUid)
    {
        SessionKey = sessionKey;
        LeftUid = leftUid;
        RightUid = rightUid;
    }

    public string SessionKey { get; }
    public string LeftUid { get; }
    public string RightUid { get; }
    public string Display => string.IsNullOrWhiteSpace(LeftUid) && string.IsNullOrWhiteSpace(RightUid)
        ? $"session {SessionKey}"
        : $"{LeftUid} <-> {RightUid}";

    public override string ToString() => Display;
}

public sealed class RelayClientInfo
{
    public RelayClientInfo(string moderationHandle, string uid, string publicKey)
    {
        ModerationHandle = moderationHandle;
        Uid = uid;
        PublicKey = publicKey;
    }

    public string ModerationHandle { get; }

    public string Uid { get; }
    public string PublicKey { get; }

    public string ShortUid => string.IsNullOrWhiteSpace(Uid)
        ? string.Empty
        : Uid.Length <= 3 ? Uid : Uid[..3];

    public string ShortPublicKey => string.IsNullOrWhiteSpace(PublicKey)
        ? string.Empty
        : PublicKey.Length <= 8 ? PublicKey : PublicKey[..8];

    public string Display => string.IsNullOrWhiteSpace(PublicKey)
        ? $"{ModerationHandle} | {Uid}"
        : $"{ModerationHandle} | {Uid} | {PublicKey}";

    public override string ToString() => Display;
}
