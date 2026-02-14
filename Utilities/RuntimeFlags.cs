using System;

namespace Zer0Talk.Utilities
{
    // Global runtime flags that must be readable across layers without taking a dependency on App.
    internal static class RuntimeFlags
    {
        public static volatile bool SafeMode;
        public static volatile bool ShowDebugUi;
    }
}
