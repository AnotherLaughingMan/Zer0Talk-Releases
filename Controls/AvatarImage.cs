using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Zer0Talk.Services;

namespace Zer0Talk.Controls;

public class AvatarImage : Image
{
    private bool _subscribed;
    private string? _lastApplied;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryApplyInterpolation();
        Subscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        // Listen to the global UI pulse; re-apply only if value changed.
        try { AppServices.Events.UiPulse += OnUiPulse; _subscribed = true; } catch { }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        try { AppServices.Events.UiPulse -= OnUiPulse; } catch { }
        _subscribed = false;
    }

    private void OnUiPulse()
    {
        TryApplyInterpolation();
    }

    private void TryApplyInterpolation()
    {
        try
        {
            var app = Application.Current;
            var mode = BitmapInterpolationMode.HighQuality;
            string current = "HighQuality";

            if (app?.Resources.TryGetValue("App.AvatarInterpolation", out var value) == true)
            {
                if (value is string str)
                {
                    // Accept "None", "LowQuality", "MediumQuality", "HighQuality"
                    current = str;
                    if (Enum.TryParse<BitmapInterpolationMode>(str, ignoreCase: true, out var parsed))
                        mode = parsed;
                }
            }

            if (!string.Equals(current, _lastApplied, StringComparison.Ordinal))
            {
                RenderOptions.SetBitmapInterpolationMode(this, mode);
                _lastApplied = current;
            }
        }
        catch
        {
            // Best-effort; ignore
        }
    }
}
