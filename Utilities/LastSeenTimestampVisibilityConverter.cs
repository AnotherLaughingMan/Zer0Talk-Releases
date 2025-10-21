using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Zer0Talk.Models;

namespace Zer0Talk.Utilities
{
    /// <summary>
    /// MultiValueConverter that returns true only when:
    /// 1. The contact's Presence is Offline
    /// 2. The contact's LastPresenceUtc is not null and not MinValue (has been seen before)
    /// 
    /// Bindings: [0] = PresenceStatus, [1] = DateTime? LastPresenceUtc
    /// </summary>
    public sealed class LastSeenTimestampVisibilityConverter : IMultiValueConverter
    {
        public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2)
                return false;

            // First binding: Presence status
            var presence = values[0] as PresenceStatus?;
            if (presence == null || presence.Value != PresenceStatus.Offline)
                return false;

            // Second binding: LastPresenceUtc
            if (values[1] is not DateTime lastPresenceUtc)
                return false;

            // Only show if contact has been seen before (not MinValue or default)
            if (lastPresenceUtc == DateTime.MinValue || lastPresenceUtc == default)
                return false;

            // Show the timestamp: contact is offline AND has been seen before
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
