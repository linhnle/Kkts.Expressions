using System;
using System.Globalization;
using System.Numerics;
using System.Linq;
using System.Text;

namespace Kkts.Expressions.Internal
{
    internal static class ExpressionConversionRules
    {
        internal static bool CanConvertLiteral(
            object value,
            Type sourceType,
            bool isNull,
            Type targetType,
            ExpressionConversionContext context)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (isNull) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
            if (sourceType == null) throw new ArgumentNullException(nameof(sourceType));

            var nullableTarget = Nullable.GetUnderlyingType(targetType) != null;
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (sourceType == targetType || targetType == typeof(string)) return true;
            if (CanConvertNumericLiteral(value, sourceType, targetType)) return true;
            if (value is string text)
            {
                if (targetType == typeof(char)) return text.Length == 1;
                if (targetType.IsEnum)
                {
                    try
                    {
                        Enum.Parse(targetType, text, true);
                        return true;
                    }
                    catch (ArgumentException) { return false; }
                    catch (OverflowException) { return false; }
                }
                if (nullableTarget && string.IsNullOrWhiteSpace(text)) return true;
                if (targetType == typeof(Guid)) return Guid.TryParse(text, out _);
                if (targetType == typeof(DateTime))
                {
                    if (RequiresDateDefaults(text)) return false;
                    return MachineDateTimeParser.IsValid(text) ||
                        DateTime.TryParse(text, context.Culture, DateTimeStyles.None, out _) ||
                        DateTime.TryParseExact(text, context.DateTimeFormats.ToArray(),
                            context.Culture, DateTimeStyles.None, out _);
                }
                if (targetType == typeof(DateTimeOffset))
                {
                    if (!HasExplicitOffset(text)) return false;
                    return MachineDateTimeParser.IsValid(text) ||
                        DateTimeOffset.TryParse(text, context.Culture, DateTimeStyles.None, out _) ||
                        DateTimeOffset.TryParseExact(text, context.DateTimeFormats.ToArray(),
                            context.Culture, DateTimeStyles.None, out _);
                }
                if (targetType == typeof(TimeSpan)) return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out _);
                if (targetType == typeof(bool)) return bool.TryParse(text, out _);
                if (NumericOperands.IsNumeric(targetType))
                {
                    try
                    {
                        Convert.ChangeType(text, targetType, context.Culture);
                        return true;
                    }
                    catch (FormatException) { return false; }
                    catch (OverflowException) { return false; }
                    catch (InvalidCastException) { return false; }
                }
            }
            if (NumericOperands.IsNumeric(sourceType) && NumericOperands.IsNumeric(targetType))
            {
                try
                {
                    Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (FormatException) { return false; }
                catch (OverflowException) { return false; }
                catch (InvalidCastException) { return false; }
            }
            return false;
        }

        internal static bool TryConvertJsonNumber(string token, Type targetType, out object converted)
        {
            converted = null;
            if (string.IsNullOrEmpty(token) || targetType == null) return false;
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (targetType == typeof(decimal))
            {
                if (!TryParseExactDecimal(token, out var decimalValue)) return false;
                converted = decimalValue;
                return true;
            }
            if (targetType == typeof(double))
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) ||
                    double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                    return false;
                converted = doubleValue;
                return true;
            }
            if (targetType == typeof(float))
            {
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var singleValue) ||
                    float.IsNaN(singleValue) || float.IsInfinity(singleValue))
                    return false;
                converted = singleValue;
                return true;
            }
            if (!TryParseExactInteger(token, out var integerValue)) return false;
            if (targetType == typeof(sbyte) && InRange(integerValue, sbyte.MinValue, sbyte.MaxValue)) converted = (sbyte)integerValue;
            else if (targetType == typeof(byte) && InRange(integerValue, byte.MinValue, byte.MaxValue)) converted = (byte)integerValue;
            else if (targetType == typeof(short) && InRange(integerValue, short.MinValue, short.MaxValue)) converted = (short)integerValue;
            else if (targetType == typeof(ushort) && InRange(integerValue, ushort.MinValue, ushort.MaxValue)) converted = (ushort)integerValue;
            else if (targetType == typeof(int) && InRange(integerValue, int.MinValue, int.MaxValue)) converted = (int)integerValue;
            else if (targetType == typeof(uint) && InRange(integerValue, uint.MinValue, uint.MaxValue)) converted = (uint)integerValue;
            else if (targetType == typeof(long) && InRange(integerValue, long.MinValue, long.MaxValue)) converted = (long)integerValue;
            else if (targetType == typeof(ulong) && InRange(integerValue, ulong.MinValue, ulong.MaxValue)) converted = (ulong)integerValue;
            return converted != null;
        }

        internal static bool IsContextDependent(object value, Type targetType)
        {
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (!(value is string text)) return false;
            if (targetType == typeof(DateTime)) return RequiresDateDefaults(text);
            if (targetType == typeof(DateTimeOffset)) return !HasExplicitOffset(text);
            return false;
        }

        internal static bool CanConvertNumericLiteral(object value, Type sourceType, Type targetType)
        {
            if (value == null || (sourceType != typeof(int) && sourceType != typeof(long)))
                return false;

            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (targetType != typeof(uint) && targetType != typeof(ulong)) return false;
            var numericValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return NumericOperands.CanConvertIntegralConstant(sourceType, numericValue, targetType);
        }

        private static bool RequiresDateDefaults(string value)
        {
            if (MachineDateTimeParser.HasCompactCalendarDate(value)) return false;
            if (value.IndexOfAny(new[] { '-', '/' }) < 0) return true;
            var parts = value.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && !parts.Any(part => part.Length == 4);
        }

        private static bool HasExplicitOffset(string value)
        {
            if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
            var separator = value.IndexOf('T');
            if (separator < 0) separator = value.IndexOf(' ');
            return separator >= 0 &&
                (value.IndexOf('+', separator) >= 0 || value.IndexOf('-', separator + 1) >= 0);
        }

        private static bool TryParseExactInteger(string token, out BigInteger value)
        {
            value = BigInteger.Zero;
            if (!TryDecomposeNumber(token, out var negative, out var digits, out var scale)) return false;
            if (digits == "0") return true;
            if (scale > 0)
            {
                if (scale >= digits.Length) return false;
                var trailingZeroCount = 0;
                for (var index = digits.Length - 1; index >= 0 && digits[index] == '0'; index--)
                    trailingZeroCount++;
                if (trailingZeroCount < scale) return false;
                digits = digits.Substring(0, digits.Length - scale);
            }
            else if (scale < 0)
            {
                var zerosToAppend = -scale;
                if (digits.Length + zerosToAppend > 40) return false;
                digits += new string('0', zerosToAppend);
            }

            if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                return false;
            if (negative) value = BigInteger.Negate(value);
            return true;
        }

        private static bool TryParseExactDecimal(string token, out decimal value)
        {
            value = decimal.Zero;
            if (!TryDecomposeNumber(token, out var negative, out var digits, out var scale)) return false;
            if (digits == "0") return true;
            while (scale > 0 && digits.Length > 1 && digits[digits.Length - 1] == '0')
            {
                digits = digits.Substring(0, digits.Length - 1);
                scale--;
            }
            if (scale < 0)
            {
                var zerosToAppend = -scale;
                if (digits.Length + zerosToAppend > 29) return false;
                digits += new string('0', zerosToAppend);
                scale = 0;
            }
            if (scale > 28 || digits.Length > 29 ||
                !BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var coefficient))
                return false;
            var maximum = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);
            if (coefficient > maximum) return false;
            var low = (int)(uint)(coefficient & uint.MaxValue);
            var middle = (int)(uint)((coefficient >> 32) & uint.MaxValue);
            var high = (int)(uint)((coefficient >> 64) & uint.MaxValue);
            value = new decimal(low, middle, high, negative, (byte)scale);
            return true;
        }

        private static bool TryDecomposeNumber(
            string token,
            out bool negative,
            out string digits,
            out int scale)
        {
            negative = false;
            digits = null;
            scale = 0;
            if (string.IsNullOrEmpty(token)) return false;
            var index = 0;
            if (token[index] == '-')
            {
                negative = true;
                index++;
            }

            var exponentIndex = token.IndexOfAny(new[] { 'e', 'E' }, index);
            var mantissaEnd = exponentIndex < 0 ? token.Length : exponentIndex;
            var exponent = 0;
            if (exponentIndex >= 0 &&
                (!int.TryParse(
                    token.Substring(exponentIndex + 1),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out exponent) ||
                 exponent < -1000 || exponent > 1000))
                return false;

            var decimalIndex = token.IndexOf('.', index, mantissaEnd - index);
            var fractionLength = decimalIndex < 0 ? 0 : mantissaEnd - decimalIndex - 1;
            var digitBuilder = new StringBuilder(mantissaEnd - index);
            for (; index < mantissaEnd; index++)
                if (token[index] != '.') digitBuilder.Append(token[index]);
            digits = digitBuilder.ToString().TrimStart('0');
            if (digits.Length == 0) digits = "0";
            scale = fractionLength - exponent;
            return true;
        }

        private static bool InRange(BigInteger value, long minimum, long maximum) =>
            value >= minimum && value <= maximum;

        private static bool InRange(BigInteger value, ulong minimum, ulong maximum) =>
            value >= minimum && value <= maximum;
    }
}
