using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Identifies the explicit JSON-compatible representation of a filter value.</summary>
    public enum FilterValueKind
    {
        Null,
        Boolean,
        Number,
        String,
        Variable,
        Collection
    }

    /// <summary>
    /// An immutable literal, variable reference, or flat membership collection used by a filter condition.
    /// </summary>
    public sealed class FilterValue
    {
        private static readonly FilterValue NullValue = new FilterValue(FilterValueKind.Null);

        private FilterValue(
            FilterValueKind kind,
            bool booleanValue = false,
            string text = null,
            IReadOnlyList<FilterValue> items = null)
        {
            Kind = kind;
            BooleanValue = booleanValue;
            Text = text;
            Items = items ?? Array.AsReadOnly(Array.Empty<FilterValue>());
        }

        /// <summary>The representation kind.</summary>
        public FilterValueKind Kind { get; }

        /// <summary>The value for <see cref="FilterValueKind.Boolean"/>.</summary>
        public bool BooleanValue { get; }

        /// <summary>
        /// The invariant JSON number token, string contents, or variable name, depending on <see cref="Kind"/>.
        /// </summary>
        public string Text { get; }

        /// <summary>The items for <see cref="FilterValueKind.Collection"/>; empty for all other kinds.</summary>
        public IReadOnlyList<FilterValue> Items { get; }

        /// <summary>Gets the null literal value.</summary>
        public static FilterValue Null => NullValue;

        /// <summary>Creates a boolean literal.</summary>
        public static FilterValue Boolean(bool value) => new FilterValue(FilterValueKind.Boolean, booleanValue: value);

        /// <summary>Creates a number literal from a valid JSON number token.</summary>
        /// <exception cref="ArgumentException">The token is not a valid JSON number.</exception>
        public static FilterValue Number(string invariantJsonNumber)
        {
            if (!IsJsonNumber(invariantJsonNumber))
                throw new ArgumentException("The value must be a valid JSON number token.", nameof(invariantJsonNumber));
            return new FilterValue(FilterValueKind.Number, text: invariantJsonNumber);
        }

        /// <summary>Creates a string literal without interpreting it as a variable reference.</summary>
        public static FilterValue String(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new FilterValue(FilterValueKind.String, text: value);
        }

        /// <summary>Creates an explicit variable reference using a dotted identifier path.</summary>
        /// <exception cref="ArgumentException">The name is not a valid dotted identifier path.</exception>
        public static FilterValue Variable(string name)
        {
            if (!IsVariableName(name))
                throw new ArgumentException("The variable name must be a non-empty dotted identifier path.", nameof(name));
            return new FilterValue(FilterValueKind.Variable, text: name);
        }

        /// <summary>Creates a flat collection for membership conditions.</summary>
        /// <exception cref="ArgumentException">The collection contains null or another collection.</exception>
        public static FilterValue Collection(IEnumerable<FilterValue> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var values = items.ToArray();
            if (values.Any(value => value == null ||
                                    value.Kind == FilterValueKind.Collection))
                throw new ArgumentException("A membership collection cannot contain null references or nested collections.", nameof(items));
            return new FilterValue(
                FilterValueKind.Collection,
                items: new ReadOnlyCollection<FilterValue>(values));
        }

        /// <summary>
        /// Converts a supported CLR scalar to its JSON-compatible filter representation.
        /// Arbitrary objects are rejected rather than converted through <c>ToString()</c>.
        /// </summary>
        /// <exception cref="NotSupportedException">The CLR type has no supported filter-value encoding.</exception>
        public static FilterValue FromObject(object value)
        {
            if (value == null) return Null;
            if (value is string stringValue) return String(stringValue);
            if (value is char character) return String(character.ToString());
            if (value is bool boolean) return Boolean(boolean);
            if (value is Enum enumValue)
                return String(Enum.Format(enumValue.GetType(), enumValue, "G"));
            if (value is Guid guid) return String(guid.ToString("D"));
            if (value is DateTime dateTime) return String(dateTime.ToString("O", CultureInfo.InvariantCulture));
            if (value is DateTimeOffset dateTimeOffset) return String(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
            if (value is TimeSpan timeSpan) return String(timeSpan.ToString("c", CultureInfo.InvariantCulture));

            var type = value.GetType();
            if (type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) ||
                type == typeof(long) || type == typeof(ulong))
                return Number(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (value is decimal decimalValue)
                return Number(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
            if (value is float singleValue)
            {
                if (float.IsNaN(singleValue) || float.IsInfinity(singleValue))
                    throw new NotSupportedException("NaN and infinity do not have JSON number representations.");
                return Number(singleValue.ToString("R", CultureInfo.InvariantCulture));
            }
            if (value is double doubleValue)
            {
                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                    throw new NotSupportedException("NaN and infinity do not have JSON number representations.");
                return Number(doubleValue.ToString("R", CultureInfo.InvariantCulture));
            }

            throw new NotSupportedException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "CLR type '{0}' has no supported filter-value encoding.",
                    type.FullName));
        }

        private static bool IsJsonNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var index = 0;
            if (value[index] == '-') index++;
            if (index == value.Length) return false;

            if (value[index] == '0')
            {
                index++;
                if (index < value.Length && IsDigit(value[index])) return false;
            }
            else
            {
                if (value[index] < '1' || value[index] > '9') return false;
                do { index++; }
                while (index < value.Length && IsDigit(value[index]));
            }

            if (index < value.Length && value[index] == '.')
            {
                index++;
                var fractionStart = index;
                while (index < value.Length && IsDigit(value[index])) index++;
                if (index == fractionStart) return false;
            }

            if (index < value.Length && (value[index] == 'e' || value[index] == 'E'))
            {
                index++;
                if (index < value.Length && (value[index] == '+' || value[index] == '-')) index++;
                var exponentStart = index;
                while (index < value.Length && IsDigit(value[index])) index++;
                if (index == exponentStart) return false;
            }

            return index == value.Length;
        }

        private static bool IsVariableName(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var segmentStart = true;
            foreach (var character in value)
            {
                if (character == '.')
                {
                    if (segmentStart) return false;
                    segmentStart = true;
                    continue;
                }

                if (segmentStart)
                {
                    if (!char.IsLetter(character) && character != '_') return false;
                    segmentStart = false;
                }
                else if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    return false;
                }
            }
            return !segmentStart;
        }

        private static bool IsDigit(char value) => value >= '0' && value <= '9';
    }
}
