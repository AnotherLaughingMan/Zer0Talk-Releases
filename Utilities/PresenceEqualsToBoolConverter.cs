using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities;

/// <summary>
/// Returns true when the bound value (PresenceStatus) string matches the converter parameter (case-insensitive).
/// Used to toggle visibility of presence glyph overlays.
/// </summary>
public sealed class PresenceEqualsToBoolConverter : IValueConverter
{
    public static readonly PresenceEqualsToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

    var valString = value is Enum ? value.ToString() : value?.ToString();
        var paramString = parameter.ToString();
        if (string.IsNullOrWhiteSpace(valString) || string.IsNullOrWhiteSpace(paramString))
            return false;

        return string.Equals(valString, paramString, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
