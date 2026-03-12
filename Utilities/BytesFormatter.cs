/*
    BytesFormatter: IValueConverter that formats a long byte count into a human-readable string.
    E.g. 1234567 → "1.2 MB", 81920 → "80.0 KB", 512 → "512 B"
*/
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities;

public sealed class BytesFormatter : IValueConverter
{
    public static readonly BytesFormatter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long l => l,
            int i => (long)i,
            _ => -1L
        };
        if (bytes < 0) return "—";
        if (bytes == 0) return "0 B";
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024L) return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
