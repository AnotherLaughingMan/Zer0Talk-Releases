using System;
using System.Collections.Concurrent;

namespace Zer0Talk.RelayServer.Services;

public sealed class RelayRateLimiter
{
    private readonly ConcurrentDictionary<string, RateState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RateState> _authStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxPerMinute;
    private readonly int _maxAuthenticatedPerMinute;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _banDuration;

    public RelayRateLimiter(int maxPerMinute, int banSeconds)
    {
        _maxPerMinute = Math.Max(1, maxPerMinute);
        _maxAuthenticatedPerMinute = Math.Max(_maxPerMinute, _maxPerMinute * 6);
        _banDuration = TimeSpan.FromSeconds(Math.Max(5, banSeconds));
    }

    public bool ShouldAllow(string ip, out TimeSpan? retryAfter)
    {
        return ShouldAllowCore(_states, ip, _maxPerMinute, out retryAfter);
    }

    public bool ShouldAllowAuthenticated(string uidOrTokenKey, out TimeSpan? retryAfter)
    {
        return ShouldAllowCore(_authStates, uidOrTokenKey, _maxAuthenticatedPerMinute, out retryAfter);
    }

    private bool ShouldAllowCore(ConcurrentDictionary<string, RateState> store, string key, int limit, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        var now = DateTime.UtcNow;
        var state = store.GetOrAdd(key, _ => new RateState(now));

        lock (state.Gate)
        {
            if (state.BanUntilUtc.HasValue && state.BanUntilUtc > now)
            {
                retryAfter = state.BanUntilUtc.Value - now;
                return false;
            }

            if (now - state.WindowStartUtc > _window)
            {
                state.WindowStartUtc = now;
                state.Count = 0;
            }

            state.Count++;
            if (state.Count > limit)
            {
                state.BanUntilUtc = now.Add(_banDuration);
                retryAfter = _banDuration;
                return false;
            }

            return true;
        }
    }

    private sealed class RateState
    {
        public RateState(DateTime now)
        {
            WindowStartUtc = now;
        }

        public object Gate { get; } = new();
        public DateTime WindowStartUtc { get; set; }
        public int Count { get; set; }
        public DateTime? BanUntilUtc { get; set; }
    }
}
