using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using ZTalk.Services;

namespace ZTalk.Utilities
{
    public class UidToAvatarConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2) return null;
            var uid = values[0] as string;
            // values[1] is the version token; not used directly, but forces re-evaluation when it changes
            if (string.IsNullOrWhiteSpace(uid)) return null;
            return AvatarCache.TryLoad(uid);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
