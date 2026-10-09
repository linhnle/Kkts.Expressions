using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Immutable limits and field operator permissions for user-supplied queries.</summary>
    public sealed class QueryPolicy
    {
        private static readonly QueryPolicy RecommendedPolicy = new QueryPolicy(
            maxExpressionLength: 4096,
            maxParenthesisDepth: 16,
            maxAtomicConditions: 64,
            maxInItems: 100,
            maxNavigationDepth: 3,
            allowCollectionAccess: false);

        /// <summary>
        /// Creates an immutable policy. Null limits are unlimited; collection access is allowed
        /// unless explicitly disabled.
        /// </summary>
        public QueryPolicy(
            int? maxExpressionLength = null,
            int? maxParenthesisDepth = null,
            int? maxAtomicConditions = null,
            int? maxInItems = null,
            int? maxNavigationDepth = null,
            bool allowCollectionAccess = true,
            IDictionary<string, IEnumerable<ComparisonOperator>> allowedOperators = null)
        {
            MaxExpressionLength = ValidateLimit(maxExpressionLength, nameof(maxExpressionLength));
            MaxParenthesisDepth = ValidateLimit(maxParenthesisDepth, nameof(maxParenthesisDepth));
            MaxAtomicConditions = ValidateLimit(maxAtomicConditions, nameof(maxAtomicConditions));
            MaxInItems = ValidateLimit(maxInItems, nameof(maxInItems));
            MaxNavigationDepth = ValidateLimit(maxNavigationDepth, nameof(maxNavigationDepth));
            AllowCollectionAccess = allowCollectionAccess;
            AllowedOperators = SnapshotOperators(allowedOperators);
        }

        /// <summary>An opt-in starting policy: 4096 units, depth 16, 64 conditions, 100 items,
        /// navigation depth 3, and entity collection access disabled.</summary>
        public static QueryPolicy Recommended => RecommendedPolicy;

        /// <summary>Maximum original UTF-16 expression length, or null for unlimited.</summary>
        public int? MaxExpressionLength { get; }

        /// <summary>Maximum parenthesis depth, or null for unlimited.</summary>
        public int? MaxParenthesisDepth { get; }

        /// <summary>Maximum atomic predicate count, or null for unlimited.</summary>
        public int? MaxAtomicConditions { get; }

        /// <summary>Maximum IN/NOT IN item count, or null for unlimited.</summary>
        public int? MaxInItems { get; }

        /// <summary>Maximum entity navigation depth, or null for unlimited.</summary>
        public int? MaxNavigationDepth { get; }

        /// <summary>Whether supported operations may traverse entity collection members.</summary>
        public bool AllowCollectionAccess { get; }

        /// <summary>Case-insensitive field paths and their allowed comparison operators.</summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>> AllowedOperators { get; }

        private static int? ValidateLimit(int? value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName, value, "Query policy limits cannot be negative.");
            return value;
        }

        private static IReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>> SnapshotOperators(
            IDictionary<string, IEnumerable<ComparisonOperator>> allowedOperators)
        {
            var snapshot = new Dictionary<string, IReadOnlyCollection<ComparisonOperator>>(StringComparer.OrdinalIgnoreCase);
            if (allowedOperators != null)
            {
                foreach (var rule in allowedOperators)
                {
                    if (string.IsNullOrWhiteSpace(rule.Key))
                        throw new ArgumentException("Operator permission field paths cannot be null or whitespace.", nameof(allowedOperators));

                    var path = ExpressionSchema.NormalizePath(rule.Key);
                    if (snapshot.ContainsKey(path))
                        throw new ArgumentException($"Duplicate operator permission for field path '{rule.Key}'.", nameof(allowedOperators));
                    if (rule.Value == null)
                        throw new ArgumentException($"Operator permissions for '{rule.Key}' cannot be null.", nameof(allowedOperators));

                    var operators = rule.Value.ToArray();
                    foreach (var comparisonOperator in operators)
                    {
                        if (!Enum.IsDefined(typeof(ComparisonOperator), comparisonOperator))
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(allowedOperators),
                                comparisonOperator,
                                $"Operator permissions for '{rule.Key}' contain an undefined comparison operator.");
                        }
                    }

                    snapshot.Add(path, Array.AsReadOnly(operators.Distinct().ToArray()));
                }
            }

            return new ReadOnlyDictionary<string, IReadOnlyCollection<ComparisonOperator>>(snapshot);
        }
    }
}
