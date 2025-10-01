/*
    UpdateManager: centralizes periodic update loops for UI and background.
    - Views register lightweight UI callbacks with RegisterUiInterval(key, intervalMs, action).
    - Background services can register background callbacks with RegisterBgInterval.
    - Intervals can be updated at runtime; timers are disposed when unregistered.
    - Also provides throttling and debouncing helpers for event-driven coalescing.
    IMPORTANT: Use per-window/per-service keys. Do not create a global app-wide loop.
    Threading: UI callbacks fire on Avalonia Dispatcher; background callbacks on ThreadPool.
*/
using System;
using System.Collections.Generic;
using System.Threading;

using Avalonia.Threading;

namespace ZTalk.Services
{
    public sealed class UpdateManager
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, DispatcherTimer> _uiTimers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Timer> _bgTimers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (DateTime last, bool pending, int intervalMs, Action action)> _throttles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (Timer timer, Action action)> _debounces = new(StringComparer.Ordinal);

        public void RegisterUiInterval(string key, int intervalMs, Action action)
        {
            if (action == null) return;
            lock (_gate)
            {
                if (_uiTimers.TryGetValue(key, out var existing))
                {
                    existing.Stop();
                    existing.Interval = TimeSpan.FromMilliseconds(Math.Max(16, intervalMs));
                    existing.Tick -= OnTick; // ensure single subscription
                    existing.Tick += OnTick;
                    existing.Tag = action; // store delegate in Tag
                    existing.Start();
                    return;
                }
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(16, intervalMs)) };
                timer.Tag = action;
                timer.Tick += OnTick;
                _uiTimers[key] = timer;
                timer.Start();
            }
        }

        public void UpdateUiInterval(string key, int intervalMs)
        {
            lock (_gate)
            {
                if (_uiTimers.TryGetValue(key, out var t))
                {
                    t.Interval = TimeSpan.FromMilliseconds(Math.Max(16, intervalMs));
                }
            }
        }

        public void UnregisterUi(string key)
        {
            lock (_gate)
            {
                if (_uiTimers.Remove(key, out var t))
                {
                    try { t.Stop(); t.Tick -= OnTick; } catch { }
                }
                // Also remove throttle/debounce for this UI key if any
                _throttles.Remove(key);
                if (_debounces.Remove(key, out var d)) { try { d.timer.Dispose(); } catch { } }
            }
        }

        private static void OnTick(object? sender, EventArgs e)
        {
            try
            {
                if (sender is DispatcherTimer dt && dt.Tag is Action a)
                {
                    a();
                }
            }
            catch { }
        }
        public void RegisterBgInterval(string key, int intervalMs, Action action)
        {
            if (action == null) return;
            lock (_gate)
            {
                if (_bgTimers.TryGetValue(key, out var existing))
                {
                    existing.Change(Math.Max(10, intervalMs), Math.Max(10, intervalMs));
                    return;
                }
                var timer = new Timer(_ =>
                {
                    try { action(); } catch { }
                }, null, Math.Max(10, intervalMs), Math.Max(10, intervalMs));
                _bgTimers[key] = timer;
            }
        }

        public void UpdateBgInterval(string key, int intervalMs)
        {
            lock (_gate)
            {
                if (_bgTimers.TryGetValue(key, out var t))
                {
                    t.Change(Math.Max(10, intervalMs), Math.Max(10, intervalMs));
                }
            }
        }

        public void UnregisterBg(string key)
        {
            lock (_gate)
            {
                if (_bgTimers.Remove(key, out var t))
                {
                    try { t.Dispose(); } catch { }
                }
            }
        }

        // Returns an action that coalesces bursts into at most one execution per minIntervalMs
        public Action GetUiThrottled(string key, int minIntervalMs, Action action)
        {
            lock (_gate)
            {
                _throttles[key] = (DateTime.MinValue, false, Math.Max(16, minIntervalMs), action);
            }
            return () =>
            {
                (DateTime last, bool pending, int intervalMs, Action action) state;
                lock (_gate)
                {
                    if (!_throttles.TryGetValue(key, out state))
                    {
                        // Key was unregistered; drop invocation gracefully
                        return;
                    }
                }
                var now = DateTime.UtcNow;
                var elapsed = (now - state.last).TotalMilliseconds;
                if (elapsed >= state.intervalMs && !state.pending)
                {
                    lock (_gate)
                    {
                        if (_throttles.ContainsKey(key))
                            _throttles[key] = (now, false, state.intervalMs, state.action);
                        else
                            return; // unregistered while deciding
                    }
                    Dispatcher.UIThread.Post(() => { try { state.action(); } catch { } });
                }
                else
                {
                    if (!state.pending)
                    {
                        lock (_gate)
                        {
                            if (_throttles.ContainsKey(key))
                                _throttles[key] = (state.last, true, state.intervalMs, state.action);
                            else
                                return; // unregistered; drop scheduling
                        }
                        var due = Math.Max(1, state.intervalMs - (int)Math.Max(0, elapsed));
                        var timer = new Timer(_ =>
                        {
                            lock (_gate)
                            {
                                if (_throttles.TryGetValue(key, out var s2))
                                {
                                    _throttles[key] = (DateTime.UtcNow, false, s2.intervalMs, s2.action);
                                    Dispatcher.UIThread.Post(() => { try { s2.action(); } catch { } });
                                }
                            }
                        }, null, due, Timeout.Infinite);
                        // one-shot; let it GC after fire
                    }
                }
            };
        }

        // Returns an action that executes after quiet period (reschedules on each call)
        public Action GetUiDebounced(string key, int delayMs, Action action)
        {
            lock (_gate)
            {
                if (_debounces.Remove(key, out var old)) { try { old.timer.Dispose(); } catch { } }
                var timer = new Timer(_ => Dispatcher.UIThread.Post(() => { try { action(); } catch { } }), null, Timeout.Infinite, Timeout.Infinite);
                _debounces[key] = (timer, action);
            }
            return () =>
            {
                lock (_gate)
                {
                    if (_debounces.TryGetValue(key, out var d))
                    {
                        d.timer.Change(Math.Max(1, delayMs), Timeout.Infinite);
                    }
                }
            };
        }

        // Graceful shutdown: stop and dispose all timers & clear bookkeeping.
        public void Shutdown()
        {
            lock (_gate)
            {
                foreach (var kv in _uiTimers)
                {
                    try { kv.Value.Stop(); kv.Value.Tick -= OnTick; } catch { }
                }
                _uiTimers.Clear();
                foreach (var kv in _bgTimers)
                {
                    try { kv.Value.Dispose(); } catch { }
                }
                _bgTimers.Clear();
                foreach (var kv in _debounces)
                {
                    try { kv.Value.timer.Dispose(); } catch { }
                }
                _debounces.Clear();
                _throttles.Clear();
            }
        }
    }
}
