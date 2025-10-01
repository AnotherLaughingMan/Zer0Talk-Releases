using System;
using System.Globalization;
using Avalonia.Data.Converters;
using ZTalk.Services;

namespace ZTalk.Utilities
{
    // Maps a message SenderUID to a display label:
    // - "You" when sender is the local user
    // - Contact.DisplayName if known
    // - Fallback to short UID
    public sealed class MessageSenderNameConverter : IValueConverter
    {
        public static readonly MessageSenderNameConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                var senderUid = (value as string) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(senderUid)) return string.Empty;
                var me = Trim(AppServices.Identity.UID ?? string.Empty);
                var uid = Trim(senderUid);
                if (string.Equals(uid, me, StringComparison.OrdinalIgnoreCase))
                    return "You";
                // Try to find a contact display name
                foreach (var c in AppServices.Contacts.Contacts)
                {
                    if (string.Equals(Trim(c.UID), uid, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(c.DisplayName) ? Short(uid) : c.DisplayName;
                }
                return Short(uid);
            }
            catch { return string.Empty; }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Trim(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }

        private static string Short(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.Length > 12 ? uid[..12] : uid;
        }
    }
}
