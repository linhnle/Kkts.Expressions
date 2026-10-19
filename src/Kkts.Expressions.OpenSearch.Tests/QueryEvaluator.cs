using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kkts.Expressions.OpenSearch.Tests
{
    /// <summary>
    /// Minimal evaluator of the Query DSL subset emitted by the translator over flat (non-nested) documents,
    /// used to compare translated queries with CLR evaluation without a running OpenSearch instance.
    /// </summary>
    internal static class QueryEvaluator
    {
        public static bool Matches(JsonNode query, IReadOnlyDictionary<string, JsonNode[]> document)
        {
            var obj = query.AsObject();
            var (kind, body) = obj.Single();
            switch (kind)
            {
                case "match_all": return true;
                case "match_none": return false;
                case "bool": return MatchesBool(body.AsObject(), document);
                case "exists": return Values(document, body["field"].GetValue<string>()).Any();
                case "term":
                {
                    var (field, spec) = body.AsObject().Single();
                    var (value, ci) = Unwrap(spec);
                    return Values(document, field).Any(v => Same(v, value, ci));
                }
                case "terms":
                {
                    var (field, list) = body.AsObject().Single();
                    return Values(document, field).Any(v => list.AsArray().Any(t => Same(v, t, false)));
                }
                case "range":
                {
                    var (field, bounds) = body.AsObject().Single();
                    return Values(document, field).Any(v => bounds.AsObject().All(b =>
                    {
                        var c = Compare(v, b.Value);
                        return b.Key switch { "gt" => c > 0, "gte" => c >= 0, "lt" => c < 0, "lte" => c <= 0, _ => throw new NotSupportedException(b.Key) };
                    }));
                }
                case "prefix":
                {
                    var (field, spec) = body.AsObject().Single();
                    var (value, ci) = Unwrap(spec);
                    var prefix = value.GetValue<string>();
                    return Values(document, field).Any(v => v.GetValue<string>().StartsWith(prefix, ci ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                }
                case "wildcard":
                {
                    var (field, spec) = body.AsObject().Single();
                    var (value, ci) = Unwrap(spec);
                    var regex = new Regex("^" + WildcardToRegex(value.GetValue<string>()) + "$", (ci ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Singleline);
                    return Values(document, field).Any(v => regex.IsMatch(v.GetValue<string>()));
                }
                default: throw new NotSupportedException("Unsupported clause " + kind);
            }
        }

        private static bool MatchesBool(JsonObject body, IReadOnlyDictionary<string, JsonNode[]> document)
        {
            foreach (var property in body)
            {
                if (property.Key == "minimum_should_match") continue;
                var clauses = property.Value.AsArray();
                switch (property.Key)
                {
                    case "filter":
                    case "must":
                        if (!clauses.All(c => Matches(c, document))) return false;
                        break;
                    case "should":
                        if (!clauses.Any(c => Matches(c, document))) return false;
                        break;
                    case "must_not":
                        if (clauses.Any(c => Matches(c, document))) return false;
                        break;
                    default: throw new NotSupportedException(property.Key);
                }
            }

            return true;
        }

        private static IEnumerable<JsonNode> Values(IReadOnlyDictionary<string, JsonNode[]> document, string field) =>
            document.TryGetValue(field, out var values) ? values.Where(v => v != null) : Enumerable.Empty<JsonNode>();

        private static (JsonNode Value, bool CaseInsensitive) Unwrap(JsonNode spec) =>
            spec is JsonObject o ? (o["value"], o["case_insensitive"]?.GetValue<bool>() ?? false) : (spec, false);

        private static bool Same(JsonNode a, JsonNode b, bool caseInsensitive)
        {
            var ka = a.GetValueKind();
            if (ka == JsonValueKind.String && b.GetValueKind() == JsonValueKind.String)
                return string.Equals(a.GetValue<string>(), b.GetValue<string>(), caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            return Compare(a, b) == 0;
        }

        private static int Compare(JsonNode a, JsonNode b)
        {
            if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
                return Number(a).CompareTo(Number(b));
            if (a.GetValueKind() == JsonValueKind.String && b.GetValueKind() == JsonValueKind.String)
                return string.CompareOrdinal(a.GetValue<string>(), b.GetValue<string>());
            if (a.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
                return a.GetValueKind() == b.GetValueKind() ? 0 : 1;
            return 1;
        }

        private static decimal Number(JsonNode node) => decimal.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string WildcardToRegex(string pattern)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (c == '\\' && i + 1 < pattern.Length) builder.Append(Regex.Escape(pattern[++i].ToString()));
                else if (c == '*') builder.Append(".*");
                else if (c == '?') builder.Append('.');
                else builder.Append(Regex.Escape(c.ToString()));
            }

            return builder.ToString();
        }
    }
}
