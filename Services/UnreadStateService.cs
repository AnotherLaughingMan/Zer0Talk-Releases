using System;
using System.Collections.Generic;

namespace Zer0Talk.Services
{
    /// <summary>
    /// Stable unread read-model facade used by UI layers and future shell/IPC adapters.
    /// It decouples consumers from NotificationService internals while preserving current behavior.
    /// </summary>
    public sealed class UnreadStateService : IDisposable
    {
        private readonly NotificationService _notifications;
        private readonly Action<IReadOnlyDictionary<string, int>> _snapshotHandler;
        private bool _disposed;

        public event Action<IReadOnlyDictionary<string, int>>? SnapshotChanged;

        public UnreadStateService(NotificationService notifications)
        {
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _snapshotHandler = snapshot =>
            {
                try
                {
                    SnapshotChanged?.Invoke(snapshot);
                }
                catch { }
            };

            try
            {
                _notifications.UnreadSnapshotChanged += _snapshotHandler;
            }
            catch { }
        }

        public IReadOnlyDictionary<string, int> GetSnapshot()
        {
            try
            {
                return _notifications.GetUnreadCountsSnapshot();
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _notifications.UnreadSnapshotChanged -= _snapshotHandler;
            }
            catch { }
        }
    }
}
