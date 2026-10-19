using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>Formats CLR values as JSON without the current culture.</summary>
    internal static class ValueFormatter
    {
        public static bool TryFormat(object value, ResolvedMember field, out JsonNode node, out string error)
        {
            node = null;
            error = null;
            var converter = field?.Converter;
            if (converter != null)
            {
                node = converter(value);
                if (node == null) error = "the configured value converter returned null";
                return node != null;
            }

            switch (value)
            {
                case string s: node = JsonValue.Create(s); return true;
                case char c: node = JsonValue.Create(c.ToString()); return true;
                case bool b: node = JsonValue.Create(b); return true;
                case sbyte v: node = JsonValue.Create(v); return true;
                case byte v: node = JsonValue.Create(v); return true;
                case short v: node = JsonValue.Create(v); return true;
                case ushort v: node = JsonValue.Create(v); return true;
                case int v: node = JsonValue.Create(v); return true;
                case uint v: node = JsonValue.Create(v); return true;
                case long v: node = JsonValue.Create(v); return true;
                case ulong v: node = JsonValue.Create(v); return true;
                case decimal v: node = JsonValue.Create(v); return true;
                case float v:
                    if (float.IsNaN(v) || float.IsInfinity(v)) return Fail("non-finite floating-point values cannot be represented in JSON", out error);
                    node = JsonValue.Create(v);
                    return true;
                case double v:
                    if (double.IsNaN(v) || double.IsInfinity(v)) return Fail("non-finite floating-point values cannot be represented in JSON", out error);
                    node = JsonValue.Create(v);
                    return true;
                case Guid g: node = JsonValue.Create(g.ToString("D", CultureInfo.InvariantCulture)); return true;
                case DateTime d: node = JsonValue.Create(d.ToString("o", CultureInfo.InvariantCulture)); return true;
                case DateTimeOffset d: node = JsonValue.Create(d.ToString("o", CultureInfo.InvariantCulture)); return true;
                case Enum e:
                    if ((field?.EnumFormat ?? EnumFormat.Number) == EnumFormat.Name)
                    {
                        var name = Enum.GetName(e.GetType(), e);
                        if (name == null) return Fail($"the value is not a defined member of {e.GetType().Name}, so it has no name to match", out error);
                        node = JsonValue.Create(name);
                        return true;
                    }

                    return TryFormat(ValueConversions.ToEnumUnderlying(e), null, out node, out error);
                default:
                    return Fail($"values of type {value?.GetType().Name ?? "null"} require a value converter registered for the type or field", out error);
            }
        }

        public static bool IsNonFinite(object value)
        {
            return (value is double d && (double.IsNaN(d) || double.IsInfinity(d))) ||
                (value is float f && (float.IsNaN(f) || float.IsInfinity(f)));
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
