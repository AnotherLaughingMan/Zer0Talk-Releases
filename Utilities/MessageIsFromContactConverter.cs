using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    /// <summary>
    /// Returns true if the message is from a contact (not from the logged-in user).
    /// Used to show the "Message received" indicator only on incoming messages.
    /// </summary>
    public sealed class MessageIsFromContactConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            static string Normalize(object? value)
            {
                if (value is not string s || string.IsNullOrWhiteSpace(s))
                    return string.Empty;

                s = s.Trim();
                return s.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && s.Length > 4
                    ? s.Substring(4)
                    : s;
            }

            var sender = values.Count > 0 ? Normalize(values[0]) : string.Empty;
            var loggedIn = values.Count > 1 ? Normalize(values[1]) : string.Empty;

            // Return true if message is NOT from the logged-in user (i.e., from a contact)
            return !string.Equals(sender, loggedIn, StringComparison.OrdinalIgnoreCase);
        }

        public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
