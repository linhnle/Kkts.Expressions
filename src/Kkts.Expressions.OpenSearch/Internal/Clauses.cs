using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>Immutable OpenSearch query clause; serialized with a fixed property order.</summary>
    internal abstract class Clause
    {
        public abstract JsonObject ToJson();
    }

    internal sealed class MatchAllClause : Clause
    {
        public static readonly MatchAllClause Instance = new MatchAllClause();

        public override JsonObject ToJson() => new JsonObject { ["match_all"] = new JsonObject() };
    }

    internal sealed class MatchNoneClause : Clause
    {
        public static readonly MatchNoneClause Instance = new MatchNoneClause();

        public override JsonObject ToJson() => new JsonObject { ["match_none"] = new JsonObject() };
    }

    internal sealed class TermClause : Clause
    {
        public TermClause(string field, JsonNode value, bool caseInsensitive = false)
        {
            Field = field;
            Value = value;
            // OpenSearch's case-insensitive term query never matches an empty value; an empty string has no case anyway.
            CaseInsensitive = caseInsensitive && !(value is JsonValue text && text.TryGetValue(out string s) && s.Length == 0);
        }

        public string Field { get; }

        public JsonNode Value { get; }

        public bool CaseInsensitive { get; }

        public override JsonObject ToJson()
        {
            JsonNode body = CaseInsensitive
                ? new JsonObject { ["value"] = Value.DeepClone(), ["case_insensitive"] = true }
                : Value.DeepClone();
            return new JsonObject { ["term"] = new JsonObject { [Field] = body } };
        }
    }

    internal sealed class TermsClause : Clause
    {
        public TermsClause(string field, IReadOnlyList<JsonNode> values)
        {
            Field = field;
            Values = values;
        }

        public string Field { get; }

        public IReadOnlyList<JsonNode> Values { get; }

        public override JsonObject ToJson()
        {
            var array = new JsonArray();
            foreach (var value in Values) array.Add(value.DeepClone());
            return new JsonObject { ["terms"] = new JsonObject { [Field] = array } };
        }
    }

    internal sealed class RangeClause : Clause
    {
        public RangeClause(string field, JsonNode gt = null, JsonNode gte = null, JsonNode lt = null, JsonNode lte = null)
        {
            Field = field;
            Gt = gt;
            Gte = gte;
            Lt = lt;
            Lte = lte;
        }

        public string Field { get; }

        public JsonNode Gt { get; }

        public JsonNode Gte { get; }

        public JsonNode Lt { get; }

        public JsonNode Lte { get; }

        public override JsonObject ToJson()
        {
            var bounds = new JsonObject();
            if (Gt != null) bounds["gt"] = Gt.DeepClone();
            if (Gte != null) bounds["gte"] = Gte.DeepClone();
            if (Lt != null) bounds["lt"] = Lt.DeepClone();
            if (Lte != null) bounds["lte"] = Lte.DeepClone();
            return new JsonObject { ["range"] = new JsonObject { [Field] = bounds } };
        }
    }

    internal sealed class ExistsClause : Clause
    {
        public ExistsClause(string field)
        {
            Field = field;
        }

        public string Field { get; }

        public override JsonObject ToJson() => new JsonObject { ["exists"] = new JsonObject { ["field"] = Field } };
    }

    internal sealed class PrefixClause : Clause
    {
        public PrefixClause(string field, string value, bool caseInsensitive)
        {
            Field = field;
            Value = value;
            CaseInsensitive = caseInsensitive;
        }

        public string Field { get; }

        public string Value { get; }

        public bool CaseInsensitive { get; }

        public override JsonObject ToJson()
        {
            JsonNode body = CaseInsensitive
                ? new JsonObject { ["value"] = Value, ["case_insensitive"] = true }
                : JsonValue.Create(Value);
            return new JsonObject { ["prefix"] = new JsonObject { [Field] = body } };
        }
    }

    internal sealed class WildcardClause : Clause
    {
        public WildcardClause(string field, string pattern, bool caseInsensitive)
        {
            Field = field;
            Pattern = pattern;
            CaseInsensitive = caseInsensitive;
        }

        public string Field { get; }

        public string Pattern { get; }

        public bool CaseInsensitive { get; }

        public override JsonObject ToJson()
        {
            JsonNode body = CaseInsensitive
                ? new JsonObject { ["value"] = Pattern, ["case_insensitive"] = true }
                : JsonValue.Create(Pattern);
            return new JsonObject { ["wildcard"] = new JsonObject { [Field] = body } };
        }
    }

    internal sealed class NestedClause : Clause
    {
        public NestedClause(string path, Clause query)
        {
            Path = path;
            Query = query;
        }

        public string Path { get; }

        public Clause Query { get; }

        public override JsonObject ToJson() =>
            new JsonObject { ["nested"] = new JsonObject { ["path"] = Path, ["query"] = Query.ToJson() } };
    }

    internal sealed class BoolClause : Clause
    {
        private static readonly Clause[] None = new Clause[0];

        public BoolClause(IReadOnlyList<Clause> filter = null, IReadOnlyList<Clause> should = null, IReadOnlyList<Clause> mustNot = null)
        {
            Filter = filter ?? None;
            Should = should ?? None;
            MustNot = mustNot ?? None;
        }

        public IReadOnlyList<Clause> Filter { get; }

        public IReadOnlyList<Clause> Should { get; }

        public IReadOnlyList<Clause> MustNot { get; }

        public bool IsConjunction => Should.Count == 0 && MustNot.Count == 0;

        public bool IsDisjunction => Filter.Count == 0 && MustNot.Count == 0 && Should.Count > 0;

        public bool IsNegation => Filter.Count == 0 && Should.Count == 0 && MustNot.Count == 1;

        public override JsonObject ToJson()
        {
            var body = new JsonObject();
            if (Filter.Count > 0) body["filter"] = ToArray(Filter);
            if (Should.Count > 0)
            {
                body["should"] = ToArray(Should);
                body["minimum_should_match"] = 1;
            }
            if (MustNot.Count > 0) body["must_not"] = ToArray(MustNot);
            return new JsonObject { ["bool"] = body };
        }

        private static JsonArray ToArray(IEnumerable<Clause> clauses)
        {
            var array = new JsonArray();
            foreach (var clause in clauses) array.Add(clause.ToJson());
            return array;
        }
    }

    /// <summary>Builds clauses while applying equivalence-preserving simplifications.</summary>
    internal static class Clauses
    {
        public static Clause And(IEnumerable<Clause> clauses)
        {
            var items = new List<Clause>();
            foreach (var clause in clauses)
            {
                if (clause is MatchAllClause) continue;
                if (clause is MatchNoneClause) return MatchNoneClause.Instance;
                if (clause is BoolClause b && b.IsConjunction) items.AddRange(b.Filter);
                else items.Add(clause);
            }

            if (items.Count == 0) return MatchAllClause.Instance;
            return items.Count == 1 ? items[0] : new BoolClause(filter: items);
        }

        public static Clause Or(IEnumerable<Clause> clauses)
        {
            var items = new List<Clause>();
            foreach (var clause in clauses)
            {
                if (clause is MatchNoneClause) continue;
                if (clause is MatchAllClause) return MatchAllClause.Instance;
                if (clause is BoolClause b && b.IsDisjunction) items.AddRange(b.Should);
                else items.Add(clause);
            }

            if (items.Count == 0) return MatchNoneClause.Instance;
            return items.Count == 1 ? items[0] : new BoolClause(should: items);
        }

        public static Clause Not(Clause clause)
        {
            switch (clause)
            {
                case MatchAllClause _:
                    return MatchNoneClause.Instance;
                case MatchNoneClause _:
                    return MatchAllClause.Instance;
                case BoolClause b when b.IsNegation:
                    return b.MustNot[0];
                default:
                    return new BoolClause(mustNot: new[] { clause });
            }
        }

        /// <summary>Wraps a clause in nested queries, given enclosing paths outermost first.</summary>
        public static Clause WrapNested(Clause clause, IEnumerable<string> pathsOutermostFirst)
        {
            foreach (var path in pathsOutermostFirst.Reverse()) clause = new NestedClause(path, clause);
            return clause;
        }

        /// <summary>The top-level query is always a bool query with clauses in filter context.</summary>
        public static JsonObject ToRootQuery(Clause clause)
        {
            if (clause is BoolClause b && b.IsConjunction) return b.ToJson();
            return new BoolClause(filter: new[] { clause }).ToJson();
        }
    }
}
