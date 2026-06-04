using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using Zer0Talk.Models;

namespace Zer0Talk.Utilities;

public sealed class PresenceToOpacityConverter : IValueConverter
{
    public static readonly PresenceToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PresenceStatus status)
        {
            return status == PresenceStatus.Online ? 1.0 : ResolveDimOpacity();
        }

        if (value is string text && Enum.TryParse<PresenceStatus>(text, ignoreCase: true, out var parsed))
        {
            return parsed == PresenceStatus.Online ? 1.0 : ResolveDimOpacity();
        }

        return ResolveDimOpacity();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static double ResolveDimOpacity()
    {
        if (Application.Current is not { } app)
        {
            return 0.56;
        }

        var theme = app.ActualThemeVariant ?? app.RequestedThemeVariant ?? ThemeVariant.Default;

        if (app.Resources.TryGetResource("App.ContactOfflineOpacity", theme, out var resource)
            && resource is IConvertible themed)
        {
            try { return Math.Clamp(themed.ToDouble(CultureInfo.InvariantCulture), 0.2, 1.0); } catch { }
        }

        if (theme != ThemeVariant.Default
            && app.Resources.TryGetResource("App.ContactOfflineOpacity", ThemeVariant.Default, out var fallback)
            && fallback is IConvertible fallbackValue)
        {
            try { return Math.Clamp(fallbackValue.ToDouble(CultureInfo.InvariantCulture), 0.2, 1.0); } catch { }
        }

        // Light variants need slightly less dimming for readability.
        return theme == ThemeVariant.Light ? 0.72 : 0.56;
    }
}
