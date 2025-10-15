using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ZTalk.Utilities;

/// <summary>
/// Converts PresenceStatus enum to a display-friendly string.
/// Maps "Invisible" to "Offline" to preserve privacy (invisible users should appear offline to others).
/// </summary>
public sealed class PresenceToDisplayStringConverter : IValueConverter
{
    public static readonly PresenceToDisplayStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            Logger.Log($"[PresenceConverter] Null value -> returning 'Offline'");
            return "Offline";
        }

        // Handle enum values directly
        if (value is ZTalk.Models.PresenceStatus presenceEnum)
        {
            Logger.Log($"[PresenceConverter] Enum value: {presenceEnum}");
            // Map Invisible to Offline for display purposes to preserve privacy
            if (presenceEnum == ZTalk.Models.PresenceStatus.Invisible)
            {
                Logger.Log($"[PresenceConverter] Invisible detected -> returning 'Offline'");
                return "Offline";
            }
            
            return presenceEnum.ToString();
        }

        // Fallback: handle as string
        var status = value.ToString() ?? "Offline";
        Logger.Log($"[PresenceConverter] String value: {status}");
        
        // Map Invisible to Offline for display purposes to preserve privacy
        if (string.Equals(status, "Invisible", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"[PresenceConverter] Invisible string detected -> returning 'Offline'");
            return "Offline";
        }

        return status;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
