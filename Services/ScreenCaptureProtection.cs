using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ZTalk.Services
{
    public static class ScreenCaptureProtection
    {
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        public static bool SetExcludeFromCapture(Window window, bool enabled)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) return false;
                var handle = window.TryGetPlatformHandle();
                if (handle is null || !string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
                    return false;

                var mode = enabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
                if (enabled && !SetWindowDisplayAffinity(handle.Handle, mode))
                {
                    // Fallback for older Windows
                    return SetWindowDisplayAffinity(handle.Handle, WDA_MONITOR);
                }
                return SetWindowDisplayAffinity(handle.Handle, mode);
            }
            catch { return false; }
        }
    }
}
