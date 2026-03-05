using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Zer0Talk.Models;

namespace Zer0Talk.Utilities
{
    /// <summary>
    /// Returns an MDL2 glyph string for outbound message delivery status.
    /// Pending = clock (E823), Sent = single check (E8FB), Delivered = double check (E73E).
    /// Returns empty string for None (incoming messages).
    /// </summary>
    public sealed class DeliveryStatusToGlyphConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not MessageDeliveryStatus status)
                return string.Empty;

            return status switch
            {
                MessageDeliveryStatus.Pending => "\uE823",   // Clock
                MessageDeliveryStatus.Sent => "\uE8FB",      // Accept (single check)
                MessageDeliveryStatus.Delivered => "\uE73E",  // CheckMark (filled check)
                _ => string.Empty
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns true if message has a delivery status to display (outbound messages only).
    /// </summary>
    public sealed class DeliveryStatusVisibleConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // values[0] = SenderUID, values[1] = LoggedInUidFull
            // Show delivery status only for messages sent by the logged-in user
            static string Normalize(object? v)
            {
                if (v is not string s || string.IsNullOrWhiteSpace(s)) return string.Empty;
                s = s.Trim();
                return s.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && s.Length > 4
                    ? s.Substring(4) : s;
            }

            var sender = values.Count > 0 ? Normalize(values[0]) : string.Empty;
            var loggedIn = values.Count > 1 ? Normalize(values[1]) : string.Empty;

            if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(loggedIn))
                return false;

            return string.Equals(sender, loggedIn, StringComparison.OrdinalIgnoreCase);
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns a tooltip string for the delivery status.
    /// </summary>
    public sealed class DeliveryStatusToTooltipConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not MessageDeliveryStatus status)
                return null;

            return status switch
            {
                MessageDeliveryStatus.Pending => Services.AppServices.Localization.GetString("MainWindow.DeliveryPending", "Sending..."),
                MessageDeliveryStatus.Sent => Services.AppServices.Localization.GetString("MainWindow.DeliverySent", "Sent"),
                MessageDeliveryStatus.Delivered => Services.AppServices.Localization.GetString("MainWindow.DeliveryDelivered", "Delivered"),
                _ => null
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
