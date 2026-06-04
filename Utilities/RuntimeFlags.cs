using System;

namespace Zer0Talk.Utilities
{
    // Global runtime flags that must be readable across layers without taking a dependency on App.
    internal static class RuntimeFlags
    {
        private static volatile bool _safeMode;
        private static volatile bool _showDebugUi;
        private static volatile bool _forceDebugLogging;

        // Raised when a runtime flag changes so view-models can refresh dependent bindings.
        public static event Action? Changed;

        public static bool SafeMode
        {
            get => _safeMode;
            set
            {
                if (_safeMode == value) return;
                _safeMode = value;
                NotifyChanged();
            }
        }

        public static bool ShowDebugUi
        {
            get => _showDebugUi;
            set
            {
                if (_showDebugUi == value) return;
                _showDebugUi = value;
                NotifyChanged();
            }
        }

        public static bool ForceDebugLogging
        {
            get => _forceDebugLogging;
            set
            {
                if (_forceDebugLogging == value) return;
                _forceDebugLogging = value;
                NotifyChanged();
            }
        }

        private static void NotifyChanged()
        {
            try { Changed?.Invoke(); } catch { }
        }
    }
}
