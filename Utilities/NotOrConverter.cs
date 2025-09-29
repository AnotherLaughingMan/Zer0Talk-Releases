using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;

namespace P2PTalk.Utilities
{
    // Returns true only when all boolean inputs are false; otherwise false.
    // Useful for IsVisible bindings where the element should appear only if
    // no input condition is true (i.e., NOT(OR(values))).
    public sealed class NotOrConverter : IMultiValueConverter
    {
        public static readonly NotOrConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values is null || values.Count == 0)
                    return false;

                foreach (var v in values)
                {
                    if (v is bool b && b)
                        return false; // some true => NOT(OR)=false
                }
                return true; // all false => NOT(OR)=true
            }
            catch
            {
                return false;
            }
        }
    }
}
