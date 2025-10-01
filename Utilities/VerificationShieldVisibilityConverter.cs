using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia.Data.Converters;

namespace ZTalk.Utilities
{
    // Policy: Show shield for verified or live-verified contacts (non-simulated).
    // Accepted bindings (by position):
    //  - [IsVerified]
    //  - [IsVerified, IsSimulated]
    //  - [PublicKeyVerified, IsVerified, IsSimulated] => show when PublicKeyVerified || IsVerified
    public sealed class VerificationShieldVisibilityConverter : IMultiValueConverter
    {
        public static readonly VerificationShieldVisibilityConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (values is null || values.Count < 1) return false;
                bool verified;
                bool isSim;

                if (values.Count >= 3)
                {
                    // [v0=PublicKeyVerified, v1=IsVerified, v2=IsSimulated]
                    var pub = values[0] is bool bv0 && bv0;
                    var isv = values[1] is bool bv1 && bv1;
                    verified = pub || isv;
                    isSim = values[2] is bool b2 && b2;
                }
                else if (values.Count == 2)
                {
                    // [IsVerified, IsSimulated]
                    verified = values[0] is bool b0 && b0;
                    isSim = values[1] is bool b1 && b1;
                }
                else
                {
                    // [IsVerified]
                    verified = values[0] is bool b0 && b0;
                    isSim = false;
                }
                // Real contacts only (hide for simulated in all builds)
                if (isSim) return false;
                return verified;
            }
            catch { return false; }
        }
    }
}
