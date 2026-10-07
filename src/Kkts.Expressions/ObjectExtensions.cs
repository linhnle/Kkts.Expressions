using System;

namespace Kkts.Expressions
{
    internal static class ObjectExtensions
    {
        public static object Cast(this object value, Type type)
        {
            if (value is string s)
            {
                return s.Cast(type);
            }

            var conversionType = Nullable.GetUnderlyingType(type);
            if (value is null)
            {
                return conversionType != null ? null : Convert.ChangeType(value, type);
            }

            if (value is TimeSpan && (conversionType ?? type) == typeof(TimeSpan)) return value;

            return Convert.ChangeType(value, conversionType ?? type);
        }
    }
}
