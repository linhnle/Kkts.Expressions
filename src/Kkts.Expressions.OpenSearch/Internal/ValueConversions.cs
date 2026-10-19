using System;
using System.Numerics;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>
    /// Reproduces C# built-in conversions between primitive, enum, and nullable values without compiling expressions or
    /// invoking user-defined operators. Floating-point targets use typed casts so rounding matches the CLR.
    /// </summary>
    internal static class ValueConversions
    {
        public static bool TryConvert(object value, Type targetType, bool isChecked, out object result)
        {
            result = null;
            if (value == null) return TypeClassifier.IsNullable(targetType);

            var target = TypeClassifier.Underlying(targetType);
            var valueType = value.GetType();
            if (valueType == target || (!target.IsValueType && target.IsAssignableFrom(valueType)))
            {
                result = value;
                return true;
            }

            if (valueType.IsEnum) value = ToEnumUnderlying(value);

            if (target.IsEnum)
            {
                if (!TryConvert(value, Enum.GetUnderlyingType(target), isChecked, out var underlying)) return false;
                result = Enum.ToObject(target, underlying);
                return true;
            }

            if (TryToBigInteger(value, out var integer))
            {
                if (target == typeof(float)) return Set(ToSingle(value), out result);
                if (target == typeof(double)) return Set(ToDouble(value), out result);
                if (target == typeof(decimal)) return Set((decimal)integer, out result);
                return TryIntegralFromBigInteger(integer, target, isChecked, out result);
            }

            switch (value)
            {
                case float f:
                    if (target == typeof(double)) return Set((double)f, out result);
                    if (target == typeof(decimal)) return TryDecimal(() => (decimal)f, out result);
                    return TryIntegralFromFloating(f, target, out result);
                case double d:
                    if (target == typeof(float)) return Set((float)d, out result);
                    if (target == typeof(decimal)) return TryDecimal(() => (decimal)d, out result);
                    return TryIntegralFromFloating(d, target, out result);
                case decimal m:
                    if (target == typeof(float)) return Set((float)m, out result);
                    if (target == typeof(double)) return Set((double)m, out result);
                    // decimal to integral conversions always throw on overflow, even when unchecked.
                    return TryIntegralFromBigInteger(new BigInteger(decimal.Truncate(m)), target, true, out result);
            }

            return false;
        }

        public static object ToEnumUnderlying(object value)
        {
            return Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryToBigInteger(object value, out BigInteger integer)
        {
            switch (value)
            {
                case sbyte v: integer = v; return true;
                case byte v: integer = v; return true;
                case short v: integer = v; return true;
                case ushort v: integer = v; return true;
                case int v: integer = v; return true;
                case uint v: integer = v; return true;
                case long v: integer = v; return true;
                case ulong v: integer = v; return true;
                case char v: integer = v; return true;
                default:
                    if (value != null && value.GetType().IsEnum) return TryToBigInteger(ToEnumUnderlying(value), out integer);
                    integer = default;
                    return false;
            }
        }

        /// <summary>The inclusive value range of an integral, char, or enum type.</summary>
        public static bool TryGetIntegralRange(Type type, out BigInteger min, out BigInteger max)
        {
            type = TypeClassifier.Underlying(type);
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            if (type == typeof(sbyte)) return Range(sbyte.MinValue, sbyte.MaxValue, out min, out max);
            if (type == typeof(byte)) return Range(byte.MinValue, byte.MaxValue, out min, out max);
            if (type == typeof(short)) return Range(short.MinValue, short.MaxValue, out min, out max);
            if (type == typeof(ushort)) return Range(ushort.MinValue, ushort.MaxValue, out min, out max);
            if (type == typeof(char)) return Range(char.MinValue, char.MaxValue, out min, out max);
            if (type == typeof(int)) return Range(int.MinValue, int.MaxValue, out min, out max);
            if (type == typeof(uint)) return Range(uint.MinValue, uint.MaxValue, out min, out max);
            if (type == typeof(long)) return Range(long.MinValue, long.MaxValue, out min, out max);
            if (type == typeof(ulong)) return Range(ulong.MinValue, ulong.MaxValue, out min, out max);
            min = max = default;
            return false;
        }

        /// <summary>Creates a boxed value of an integral, char, or enum type from an in-range integer.</summary>
        public static object FromBigInteger(BigInteger value, Type type)
        {
            type = TypeClassifier.Underlying(type);
            if (type.IsEnum) return Enum.ToObject(type, FromBigInteger(value, Enum.GetUnderlyingType(type)));
            if (!TryIntegralFromBigInteger(value, type, true, out var result))
                throw new OverflowException("The value is outside the range of " + type + ".");
            return result;
        }

        private static float ToSingle(object value)
        {
            switch (value)
            {
                case sbyte v: return v;
                case byte v: return v;
                case short v: return v;
                case ushort v: return v;
                case int v: return v;
                case uint v: return v;
                case long v: return v;
                case ulong v: return v;
                case char v: return v;
                default: throw new InvalidCastException();
            }
        }

        private static double ToDouble(object value)
        {
            switch (value)
            {
                case sbyte v: return v;
                case byte v: return v;
                case short v: return v;
                case ushort v: return v;
                case int v: return v;
                case uint v: return v;
                case long v: return v;
                case ulong v: return v;
                case char v: return v;
                default: throw new InvalidCastException();
            }
        }

        private static bool TryIntegralFromFloating(double value, Type target, out object result)
        {
            result = null;
            // Out-of-range floating-point to integral conversions are unspecified in unchecked contexts, so refuse them.
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            var truncated = Math.Truncate(value);
            return TryIntegralFromBigInteger(new BigInteger(truncated), target, true, out result);
        }

        private static bool TryIntegralFromBigInteger(BigInteger value, Type target, bool isChecked, out object result)
        {
            result = null;
            if (!TryGetIntegralRange(target, out var min, out var max)) return false;
            if (value < min || value > max)
            {
                if (isChecked) return false;
                var size = max - min + 1;
                value = ((value - min) % size + size) % size + min;
            }

            if (target == typeof(sbyte)) result = (sbyte)value;
            else if (target == typeof(byte)) result = (byte)value;
            else if (target == typeof(short)) result = (short)value;
            else if (target == typeof(ushort)) result = (ushort)value;
            else if (target == typeof(char)) result = (char)(ushort)value;
            else if (target == typeof(int)) result = (int)value;
            else if (target == typeof(uint)) result = (uint)value;
            else if (target == typeof(long)) result = (long)value;
            else if (target == typeof(ulong)) result = (ulong)value;
            else return false;
            return true;
        }

        private static bool TryDecimal(Func<decimal> convert, out object result)
        {
            try
            {
                result = convert();
                return true;
            }
            catch (OverflowException)
            {
                result = null;
                return false;
            }
        }

        private static bool Set(object value, out object result)
        {
            result = value;
            return true;
        }

        private static bool Range(BigInteger low, BigInteger high, out BigInteger min, out BigInteger max)
        {
            min = low;
            max = high;
            return true;
        }
    }
}
