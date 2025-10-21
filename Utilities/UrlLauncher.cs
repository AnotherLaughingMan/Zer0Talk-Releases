using System;
using System.Diagnostics;

namespace Zer0Talk.Utilities
{
    public static class UrlLauncher
    {
        public static bool TryOpen(string? url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return false;
                var trimmed = url.Trim();
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;

                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true });
                    return true;
                }

                if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", trimmed);
                    return true;
                }

                if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", trimmed);
                    return true;
                }

                Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
