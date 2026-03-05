using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Zer0Talk.Utilities
{
    public sealed class VersionMismatchTooltipConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var peerVersion = values?.Count > 0 ? values[0]?.ToString() : null;
            var ourVersion = values?.Count > 1 ? values[1]?.ToString() : null;
            if (string.IsNullOrEmpty(peerVersion)) return null;

            var template = Services.AppServices.Localization.GetString(
                "MainWindow.VersionMismatchTooltip",
                "Version mismatch \u2014 peer is running v{0} (you: v{1})");

            return string.Format(CultureInfo.InvariantCulture, template, peerVersion, ourVersion ?? "?");
        }
    }
}
