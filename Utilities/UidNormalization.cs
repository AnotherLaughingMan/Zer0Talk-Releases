using System;

namespace Zer0Talk.Utilities
{
    public static class UidNormalization
    {
        public static string TrimPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) && uid.Length > 4
                ? uid.Substring(4)
                : uid;
        }

        public static bool EqualsNormalized(string left, string right)
        {
            var l = TrimPrefix(left);
            var r = TrimPrefix(right);
            if (string.IsNullOrWhiteSpace(l) || string.IsNullOrWhiteSpace(r)) return false;
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }
    }
}
