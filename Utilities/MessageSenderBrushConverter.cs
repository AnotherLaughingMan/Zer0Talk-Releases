using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ZTalk.Utilities
{
    /// <summary>
    /// Selects a theme brush for a message sender based on whether the sender is the local user.
    /// </summary>
    public sealed class MessageSenderBrushConverter : IMultiValueConverter
    {
        public static readonly MessageSenderBrushConverter Instance = new();

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

            if (Application.Current is not { } app)
                return Brushes.White;

            var senderBrush = ResolveBrush(app, "App.ChatSenderName", Brushes.DeepSkyBlue);
            var receiverBrush = ResolveBrush(app, "App.ChatReceiverName", Brushes.Goldenrod);

            return string.Equals(sender, loggedIn, StringComparison.OrdinalIgnoreCase)
                ? senderBrush
                : receiverBrush;
        }

        private static IBrush ResolveBrush(Application app, string resourceKey, IBrush fallback)
        {
            var theme = app.ActualThemeVariant ?? app.RequestedThemeVariant ?? ThemeVariant.Default;

            if (app.Resources.TryGetResource(resourceKey, theme, out var resource) && resource is IBrush themedBrush)
                return themedBrush;

            if (theme != ThemeVariant.Default && app.Resources.TryGetResource(resourceKey, ThemeVariant.Default, out var defaultResource) && defaultResource is IBrush defaultBrush)
                return defaultBrush;

            return fallback;
        }
    }
}
