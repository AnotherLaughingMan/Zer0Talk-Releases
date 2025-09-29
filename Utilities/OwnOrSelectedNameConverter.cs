using System;
using System.Globalization;
using Avalonia.Data.Converters;
using System.Collections.Generic;

namespace P2PTalk.Utilities
{
    public sealed class OwnOrSelectedNameConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // values[0] = SenderUID (string)
            // values[1] = LoggedInUidFull (string)
            // values[2] = RecipientUID (string)
            // values[3] = SelectedContactUID (string)
            // values[4] = LoggedInUsername (string)
            // values[5] = SelectedContactIdentity (string)
            try
            {
                string s0(object? o) => o as string ?? string.Empty;
                var senderUid = values.Count > 0 ? s0(values[0]) : string.Empty;
                var loggedInUid = values.Count > 1 ? s0(values[1]) : string.Empty;
                var recipientUid = values.Count > 2 ? s0(values[2]) : string.Empty;
                var selectedContactUid = values.Count > 3 ? s0(values[3]) : string.Empty;
                var loggedInUsername = values.Count > 4 ? s0(values[4]) : string.Empty;
                var selectedContactIdentity = values.Count > 5 ? s0(values[5]) : string.Empty;

                // Primary: direct compare sender to logged-in
                if (!string.IsNullOrWhiteSpace(senderUid) && !string.IsNullOrWhiteSpace(loggedInUid) &&
                    string.Equals(senderUid, loggedInUid, StringComparison.OrdinalIgnoreCase))
                    return loggedInUsername;

                // Fallback: if recipient is the selected contact (peer), then sender is me
                if (!string.IsNullOrWhiteSpace(recipientUid) && !string.IsNullOrWhiteSpace(selectedContactUid) &&
                    string.Equals(recipientUid, selectedContactUid, StringComparison.OrdinalIgnoreCase))
                    return loggedInUsername;

                return selectedContactIdentity;
            }
            catch
            {
                return "";
            }
        }
    }
}
