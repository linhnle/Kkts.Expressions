using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Internal;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Translates predicates, ordering, and pagination for elements of <see cref="ElementType"/> into OpenSearch Query DSL.
    /// Instances are immutable, thread-safe, and reusable.
    /// </summary>
    public class OpenSearchQueryTranslator
    {
        public OpenSearchQueryTranslator(OpenSearchMapping mapping)
        {
            Mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        public OpenSearchMapping Mapping { get; }

        public Type ElementType => Mapping.ElementType;

        /// <summary>Translates a single-parameter Boolean lambda into a <c>bool</c> query with clauses in filter context.</summary>
        public OpenSearchTranslationResult<JsonObject> TranslatePredicate(LambdaExpression predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return TranslatePredicates(new[] { predicate });
        }

        /// <summary>Translates predicates combined with AND into one <c>bool</c> query.</summary>
        public OpenSearchTranslationResult<JsonObject> TranslatePredicates(IEnumerable<LambdaExpression> predicates)
        {
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            var diagnostics = new List<OpenSearchTranslationDiagnostic>();
            var query = BuildQuery(predicates, diagnostics);
            return Complete(query, diagnostics);
        }

        /// <summary>Translates an ordering into an OpenSearch <c>sort</c> array.</summary>
        public OpenSearchTranslationResult<JsonArray> TranslateSort(OrderByClause orderBy)
        {
            if (orderBy == null) throw new ArgumentNullException(nameof(orderBy));
            var diagnostics = new List<OpenSearchTranslationDiagnostic>();
            var sort = BuildSort(orderBy, diagnostics);
            return Complete(sort, diagnostics);
        }

        /// <summary>Translates pagination into an object with <c>from</c> and <c>size</c>.</summary>
        public OpenSearchTranslationResult<JsonObject> TranslatePagination(Pagination pagination)
        {
            if (pagination == null) throw new ArgumentNullException(nameof(pagination));
            var diagnostics = new List<OpenSearchTranslationDiagnostic>();
            var body = new JsonObject();
            AddPagination(body, pagination, diagnostics);
            return Complete(body, diagnostics);
        }

        /// <summary>Composes a search body with <c>query</c> and optional <c>sort</c>, <c>from</c>, and <c>size</c>.</summary>
        public OpenSearchTranslationResult<JsonObject> TranslateSearch(LambdaExpression predicate, OrderByClause orderBy = null, Pagination pagination = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return TranslateSearch(new[] { predicate }, orderBy, pagination);
        }

        public OpenSearchTranslationResult<JsonObject> TranslateSearch(IEnumerable<LambdaExpression> predicates, OrderByClause orderBy = null, Pagination pagination = null)
        {
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            var diagnostics = new List<OpenSearchTranslationDiagnostic>();
            var query = BuildQuery(predicates, diagnostics);
            var sort = orderBy == null ? null : BuildSort(orderBy, diagnostics);
            var body = new JsonObject { ["query"] = query };
            if (sort != null) body["sort"] = sort;
            if (pagination != null) AddPagination(body, pagination, diagnostics);
            return Complete(body, diagnostics);
        }

        /// <summary>Translates a condition's predicates and ordering; an invalid condition fails without translation.</summary>
        public OpenSearchTranslationResult<JsonObject> TranslateSearch(Condition condition, Pagination pagination = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!condition.IsValid) return InvalidCondition();
            return TranslateSearch(condition.Predicates ?? (ICollection<LambdaExpression>)Array.Empty<LambdaExpression>(), condition.OrderByClause, pagination);
        }

        internal static OpenSearchTranslationResult<JsonObject> InvalidCondition()
        {
            return OpenSearchTranslationResult<JsonObject>.Failure(new[]
            {
                new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.InvalidCondition,
                    "The condition is not valid; inspect Condition.Error before translating.")
            });
        }

        private JsonObject BuildQuery(IEnumerable<LambdaExpression> predicates, List<OpenSearchTranslationDiagnostic> diagnostics)
        {
            var clauses = new List<Clause>();
            var failed = false;
            foreach (var predicate in predicates)
            {
                if (predicate == null) throw new ArgumentException("Predicates cannot contain null.", nameof(predicates));
                if (predicate.Parameters.Count != 1 || predicate.Parameters[0].Type != ElementType || predicate.ReturnType != typeof(bool))
                {
                    diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.UnsupportedExpression,
                        $"A predicate must take one {ElementType.Name} parameter and return bool."));
                    failed = true;
                    continue;
                }

                var count = diagnostics.Count;
                var clause = new PredicateTranslator(Mapping, diagnostics).Translate(predicate);
                if (clause == null || diagnostics.Count > count) failed = true;
                else clauses.Add(clause);
            }

            return failed ? null : Clauses.ToRootQuery(Clauses.And(clauses));
        }

        private JsonArray BuildSort(OrderByClause orderBy, List<OpenSearchTranslationDiagnostic> diagnostics)
        {
            var sort = new JsonArray();
            var failed = false;
            foreach (var key in orderBy.GetKeys(ElementType))
            {
                var entry = BuildSortKey(key, diagnostics);
                if (entry == null) failed = true;
                else sort.Add(entry);
            }

            return failed ? null : sort;
        }

        private JsonObject BuildSortKey(OrderByKey key, List<OpenSearchTranslationDiagnostic> diagnostics)
        {
            var selector = key.KeySelector;
            var text = ExpressionText.Render(selector.Body);
            var body = selector.Body;
            var conversions = new List<UnaryExpression>();
            while (body.NodeType == ExpressionType.Convert || body.NodeType == ExpressionType.ConvertChecked)
            {
                conversions.Insert(0, (UnaryExpression)body);
                body = ((UnaryExpression)body).Operand;
            }

            var members = new List<MemberInfo>();
            while (body is MemberExpression member && member.Expression != null && (member.Member is PropertyInfo || member.Member is FieldInfo))
            {
                members.Insert(0, member.Member);
                body = member.Expression;
            }

            if (body != selector.Parameters[0] || members.Count == 0 ||
                members.Take(members.Count - 1).Any(m => !TypeClassifier.IsOrdinaryObject(MemberChain.GetMemberType(m))))
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.UnmappedField,
                    "Sort keys must be property or field chains; computed sort keys cannot be mapped to OpenSearch fields.", null, text));
                return null;
            }

            var resolved = Mapping.Resolve(new MemberChain(members));
            if (resolved.IsCollection || resolved.IsOrdinaryObject)
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Only scalar fields can be used as sort keys.", resolved.MemberPath, text));
                return null;
            }

            if (!NumericConversion.TryCreate(resolved.ValueType, conversions, out var conversion, out var error) || !IsOrderPreserving(conversion))
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.UnsupportedConversion,
                    "The sort key conversion cannot be translated: " + (error ?? "it can merge distinct values, which changes tie ordering") + ".",
                    resolved.MemberPath, text));
                return null;
            }

            if (resolved.ValueType.IsEnum && resolved.EnumFormat == EnumFormat.Name)
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.UnsupportedValue,
                    "Enums indexed by name sort alphabetically, not by value; use the numeric enum format to sort.", resolved.MemberPath, text));
                return null;
            }

            if (resolved.IsStringLike && resolved.ExactPath == null)
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.TextFieldExactMatch,
                    "Sorting on strings requires a keyword field or keyword subfield; the field is mapped as text only.", resolved.MemberPath, text));
                return null;
            }

            var options = new JsonObject
            {
                ["order"] = key.Descending ? "desc" : "asc",
                // LINQ orders null before all values.
                ["missing"] = key.Descending ? "_last" : "_first"
            };

            JsonObject nested = null;
            foreach (var scope in resolved.EnclosingNested.Reverse())
            {
                var current = new JsonObject { ["path"] = scope.Path };
                if (nested != null) current["nested"] = nested;
                nested = current;
            }

            if (nested != null) options["nested"] = nested;
            return new JsonObject { [resolved.IsStringLike ? resolved.ExactPath : resolved.Path] = options };
        }

        private static bool IsOrderPreserving(NumericConversion conversion)
        {
            if (conversion.IsIdentity) return true;
            var target = conversion.TargetType;
            return target != typeof(float) && target != typeof(double);
        }

        private void AddPagination(JsonObject body, Pagination pagination, List<OpenSearchTranslationDiagnostic> diagnostics)
        {
            long window = (long)pagination.Offset + pagination.Limit;
            if (window > Mapping.MaxResultWindow)
            {
                diagnostics.Add(new OpenSearchTranslationDiagnostic(OpenSearchDiagnosticCodes.ResultWindowExceeded,
                    $"from + size is {window}, which exceeds the maximum result window of {Mapping.MaxResultWindow}; use search_after or a larger index.max_result_window."));
                return;
            }

            body["from"] = pagination.Offset;
            body["size"] = pagination.Limit;
        }

        private static OpenSearchTranslationResult<TOutput> Complete<TOutput>(TOutput value, List<OpenSearchTranslationDiagnostic> diagnostics)
            where TOutput : JsonNode
        {
            return diagnostics.Count > 0 || value == null
                ? OpenSearchTranslationResult<TOutput>.Failure(diagnostics)
                : OpenSearchTranslationResult<TOutput>.Success(value);
        }
    }

    /// <summary>Strongly typed translator for elements of <typeparamref name="T"/>.</summary>
    public sealed class OpenSearchQueryTranslator<T> : OpenSearchQueryTranslator
    {
        public OpenSearchQueryTranslator()
            : this(OpenSearchMapping<T>.Default)
        {
        }

        public OpenSearchQueryTranslator(OpenSearchMapping<T> mapping)
            : base(mapping)
        {
        }

        public OpenSearchTranslationResult<JsonObject> TranslatePredicate(Expression<Func<T, bool>> predicate) =>
            base.TranslatePredicate(predicate);

        public OpenSearchTranslationResult<JsonObject> TranslatePredicates(IEnumerable<Expression<Func<T, bool>>> predicates)
        {
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            return base.TranslatePredicates(predicates.Cast<LambdaExpression>());
        }

        public OpenSearchTranslationResult<JsonObject> TranslateSearch(Expression<Func<T, bool>> predicate, OrderByClause orderBy = null, Pagination pagination = null) =>
            base.TranslateSearch(predicate, orderBy, pagination);

        public OpenSearchTranslationResult<JsonObject> TranslateSearch(IEnumerable<Expression<Func<T, bool>>> predicates, OrderByClause orderBy = null, Pagination pagination = null)
        {
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            return base.TranslateSearch(predicates.Cast<LambdaExpression>(), orderBy, pagination);
        }

        public OpenSearchTranslationResult<JsonObject> TranslateSearch(Condition<T> condition, Pagination pagination = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!condition.IsValid) return InvalidCondition();
            return TranslateSearch(condition.Predicates ?? (IEnumerable<Expression<Func<T, bool>>>)Array.Empty<Expression<Func<T, bool>>>(), condition.OrderByClause, pagination);
        }
    }
}
