using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kkts.Expressions
{
	public static class StringExtensions
	{
		public static IList<string> DateTimeFormats { get; } = new List<string> {
			"d/M/yyyy", "d-M-yyyy", "yyyy/M/d", "yyyy-M-d", "M/d/yyyy", "M-d-yyyy",
		};

		public static object Cast(this string value, Type conversionType, IFormatProvider provider = null)
		{
			if (conversionType == null) throw new ArgumentNullException(nameof(conversionType));
			if (conversionType == typeof(string)) return value;
			var originType = conversionType;
			conversionType = Nullable.GetUnderlyingType(conversionType) ?? conversionType;
			var isNullable = conversionType != originType;
			if (string.IsNullOrWhiteSpace(value))
			{
				if (isNullable) return null;
				throw new FormatException();
			}

			provider = provider ?? CultureInfo.InvariantCulture;

			if (conversionType.IsEnum) return Enum.Parse(conversionType, value, true);

			if (conversionType == typeof(DateTime)) return ToDateTime(value, provider);

			if (conversionType == typeof(DateTimeOffset)) return ToDateTimeOffset(value, provider);

			if (conversionType == typeof(Guid)) return Guid.Parse(value);

			return Convert.ChangeType(value, conversionType, provider);
		}

		public static T Cast<T>(this string value, IFormatProvider provider = null) where T : struct
		{
			return (T)Cast(value, typeof(T), provider);
		}

		public static bool TryCast(this string value, Type conversionType, out object result, IFormatProvider provider = null)
		{
			try
			{
				result = Cast(value, conversionType, provider);
				return true;
			}
			catch (Exception)
			{
				result = null;
				return false;
			}
		}

		public static bool TryCast<T>(this string value, out T result, IFormatProvider provider = null) where T : struct
		{
			try
			{
				result = Cast<T>(value, provider);
				return true;
			}
			catch (Exception)
			{
				result = default(T);
				return false;
			}
		}

		public static DateTime ToDateTime(this string value, IFormatProvider provider = null)
		{
			var succeeded = DateTime.TryParse(value, out var result) || DateTime.TryParseExact(value, DateTimeFormats.ToArray(), provider ?? CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

			return succeeded ? result : throw new FormatException(value == null ? "Can not parse null value to DateTime" : $"String '{value}' was not recognized as a valid DateTime.");
		}

		public static bool TryParseDateTime(this string value, out DateTime dateTime, IFormatProvider provider = null)
		{
			try
			{
				dateTime = ToDateTime(value, provider);
				return true;
			}
			catch (FormatException)
			{
				dateTime = DateTime.MinValue;
				return false;
			}
		}

		public static DateTimeOffset ToDateTimeOffset(this string value, IFormatProvider provider = null)
		{
			var succeeded = DateTimeOffset.TryParse(value, out var result) || DateTimeOffset.TryParseExact(value, DateTimeFormats.ToArray(), provider ?? CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

			return succeeded ? result : throw new FormatException(value == null ? "Can not parse null value to DateTimeOffset" : $"String '{value}' was not recognized as a valid DateTimeOffset.");
		}

		public static bool TryParseDateTimeOffset(this string value, out DateTimeOffset dateTimeOffset, IFormatProvider provider = null)
		{
			try
			{
				dateTimeOffset = ToDateTimeOffset(value, provider);
				return true;
			}
			catch (FormatException)
			{
				dateTimeOffset = DateTimeOffset.MinValue;
				return false;
			}
		}

		/// <summary>
		/// Returns the input string with the first character converted to uppercase
		/// </summary>
		public static string ToPascalCase(this string str)
		{
			if (string.IsNullOrEmpty(str)) return str;

			var a = str.ToCharArray();
			a[0] = char.ToUpper(a[0]);

			return new string(a);
		}

		/// <summary>
		/// Returns the input string with the first character converted to lowercase
		/// </summary>
		public static string ToCamelCase(this string str)
		{
			if (string.IsNullOrEmpty(str)) return str;

			var a = str.ToCharArray();
			a[0] = char.ToLower(a[0]);

			return new string(a);
		}
	}
}
