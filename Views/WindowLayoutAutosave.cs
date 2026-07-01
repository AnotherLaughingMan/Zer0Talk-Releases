using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Zer0Talk.Services;

namespace Zer0Talk.Views;

internal sealed class WindowLayoutAutosave : IDisposable
{
    private readonly Window _window;
    private readonly Func<string> _keyFactory;
    private readonly Func<LayoutCache.WindowLayout> _layoutFactory;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _isRestoring;
    private bool _isReady;

    public WindowLayoutAutosave(Window window, Func<string> keyFactory, Func<LayoutCache.WindowLayout>? layoutFactory = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _keyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
        _layoutFactory = layoutFactory ?? CreateDefaultLayout;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _timer.Tick += (_, __) =>
        {
            _timer.Stop();
            SaveNow();
        };
    }

    public void BeginRestore()
    {
        _isRestoring = true;
        _timer.Stop();
    }

    public void EndRestore()
    {
        _isRestoring = false;
        _isReady = true;
    }

    public void ScheduleSave()
    {
        if (_disposed || _isRestoring || !_isReady) return;
        if (_window.WindowState == WindowState.Minimized) return;

        _timer.Stop();
        _timer.Start();
    }

    public void SaveNow()
    {
        if (_disposed || _isRestoring) return;
        if (_window.WindowState == WindowState.Minimized) return;

        try
        {
            var key = _keyFactory();
            if (string.IsNullOrWhiteSpace(key)) return;
            LayoutCache.Save(key, _layoutFactory());
        }
        catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private LayoutCache.WindowLayout CreateDefaultLayout()
    {
        var width = _window.Width > 0 ? _window.Width : _window.Bounds.Width;
        var height = _window.Height > 0 ? _window.Height : _window.Bounds.Height;
        return new LayoutCache.WindowLayout(width, height, _window.Position.X, _window.Position.Y, (int)_window.WindowState);
    }
}
