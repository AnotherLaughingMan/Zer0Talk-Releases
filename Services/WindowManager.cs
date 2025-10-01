using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ZTalk.Services
{
    // Centralized helper to enforce singleton behavior for windows.
    public static class WindowManager
    {
        // Show or focus a single instance of the given window type.
        public static T? ShowSingleton<T>() where T : Window, new()
        {
            try
            {
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (desktop == null)
                {
                    var nw = new T();
                    // No desktop lifetime available; show without owner as a fallback
                    nw.Show();
                    return nw;
                }
                var existing = desktop.Windows?.OfType<T>().FirstOrDefault();
                if (existing != null)
                {
                    if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                    existing.Activate();
                    existing.BringIntoView();
                    return existing;
                }
                var w = new T();
                try
                {
                    w.Closed += (_, __) =>
                    {
                        try { if (w.DataContext is System.IDisposable d) d.Dispose(); } catch { }
                        try { w.DataContext = null; } catch { }
                    };
                }
                catch { }
                // Ensure MainWindow owns all secondary windows so they close with it
                var owner = desktop.MainWindow as Window;
                if (owner != null)
                {
                    w.Show(owner);
                }
                else
                {
                    w.Show();
                }
                return w;
            }
            catch { return null; }
        }
    }
}
