using System;
using System.Globalization;

namespace Kkts.Expressions.Internal
{
    internal static class MachineDateTimeParser
    {
        private static readonly string[] Fractions =
        {
            "", ".f", ".ff", ".fff", ".ffff", ".fffff", ".ffffff", ".fffffff"
        };

        internal static bool TryParseDateTime(string value, out DateTime result)
        {
            result = DateTime.MinValue;
            return TryGetFormat(value, out var normalized, out var format, out _) &&
                DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        internal static bool TryParseDateTimeOffset(string value, out DateTimeOffset result)
        {
            result = DateTimeOffset.MinValue;
            return TryGetFormat(value, out var normalized, out var format, out _) &&
                DateTimeOffset.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        internal static bool IsValid(string value)
        {
            if (!TryGetFormat(value, out var normalized, out var format, out var hasOffset)) return false;
            // Zoned semantic checks must not convert an instant to machine-local DateTime.
            return hasOffset
                ? DateTimeOffset.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                : DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        internal static bool HasCompactCalendarDate(string value)
        {
            return value != null && value.Length >= 8 && HasDigits(value, 0, 8) &&
                (value.Length == 8 || value[8] == 'T');
        }

        private static bool TryGetFormat(string value, out string normalized, out string format, out bool hasOffset)
        {
            normalized = value;
            format = null;
            hasOffset = false;
            if (string.IsNullOrEmpty(value)) return false;

            var compact = HasCompactCalendarDate(value);
            if (compact)
            {
                format = "yyyyMMdd";
                if (value.Length == 8) return true;
            }
            else
            {
                if (value.Length < 10 || !HasDigits(value, 0, 4) ||
                    value[4] != '-' || !HasDigits(value, 5, 2) ||
                    value[7] != '-' || !HasDigits(value, 8, 2)) return false;
                format = "yyyy-MM-dd";
                if (value.Length == 10) return true;
            }

            var dateLength = compact ? 8 : 10;
            var timestampLength = compact ? 15 : 19;
            if (value.Length < timestampLength || value[dateLength] != 'T') return false;
            if (compact)
            {
                if (!HasDigits(value, 9, 6)) return false;
                format += "'T'HHmmss";
            }
            else
            {
                if (!HasDigits(value, 11, 2) || value[13] != ':' ||
                    !HasDigits(value, 14, 2) || value[16] != ':' ||
                    !HasDigits(value, 17, 2)) return false;
                format += "'T'HH:mm:ss";
            }

            var index = timestampLength;
            if (index < value.Length && value[index] == '.')
            {
                var start = ++index;
                while (index < value.Length && value[index] >= '0' && value[index] <= '9') ++index;
                var digits = index - start;
                if (digits < 1 || digits > 7) return false;
                format += Fractions[digits];
            }

            if (index == value.Length) return true;
            hasOffset = true;
            format += "zzz";
            if (value[index] == 'Z' && index == value.Length - 1)
            {
                normalized = value.Substring(0, index) + "+00:00";
                return true;
            }

            if (value[index] != '+' && value[index] != '-') return false;
            if (compact)
            {
                if (value.Length - index != 5 || !HasDigits(value, index + 1, 4)) return false;
                normalized = value.Insert(index + 3, ":");
                return true;
            }

            return value.Length - index == 6 && HasDigits(value, index + 1, 2) &&
                value[index + 3] == ':' && HasDigits(value, index + 4, 2);
        }

        private static bool HasDigits(string value, int start, int length)
        {
            for (var index = start; index < start + length; ++index)
            {
                if (value[index] < '0' || value[index] > '9') return false;
            }
            return true;
        }
    }
}
