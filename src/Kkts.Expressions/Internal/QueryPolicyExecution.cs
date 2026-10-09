using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
    internal sealed class QueryPolicyExecution
    {
        internal QueryPolicyExecution(QueryPolicy policy)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            Diagnostics = new QueryPolicyDiagnosticCollector();
        }

        internal QueryPolicy Policy { get; }

        internal QueryPolicyDiagnosticCollector Diagnostics { get; }

        internal QueryPolicyCounter ConditionCount { get; } = new QueryPolicyCounter();

        internal QueryPolicyCounter CreateMembershipCounter() => new QueryPolicyCounter();

        internal bool TryAdmitExpression(string expression, string inputPath = null)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            if (!Policy.MaxExpressionLength.HasValue ||
                expression.Length <= Policy.MaxExpressionLength.Value)
                return true;

            var limit = Policy.MaxExpressionLength.Value;
            Diagnostics.Add(
                "query-policy-expression-length-exceeded",
                "The expression exceeds the configured UTF-16 length limit.",
                limit,
                expression.Length - limit,
                limit,
                expression.Length,
                inputPath: inputPath);
            return false;
        }

        internal bool TryCountCondition(int start, int length, string inputPath = null)
        {
            var observed = ConditionCount.Increment();
            if (!Policy.MaxAtomicConditions.HasValue || observed <= Policy.MaxAtomicConditions.Value)
                return true;

            Diagnostics.Add(
                "query-policy-condition-count-exceeded",
                "The query contains more atomic conditions than the configured limit.",
                start,
                length,
                Policy.MaxAtomicConditions.Value,
                observed,
                inputPath: inputPath);
            return false;
        }

        internal bool TryCountMembershipItem(
            QueryPolicyCounter counter,
            int start,
            int length,
            string inputPath = null)
        {
            if (counter == null) throw new ArgumentNullException(nameof(counter));

            var observed = counter.Increment();
            if (!Policy.MaxInItems.HasValue || observed <= Policy.MaxInItems.Value)
                return true;

            Diagnostics.Add(
                "query-policy-in-items-exceeded",
                "The membership collection contains more items than the configured limit.",
                start,
                length,
                Policy.MaxInItems.Value,
                observed,
                observedValueIsLowerBound: true,
                inputPath: inputPath);
            return false;
        }
    }

    internal sealed class QueryPolicyCounter
    {
        private long _value;

        internal QueryPolicyCounter(long initialValue = 0)
        {
            if (initialValue < 0) throw new ArgumentOutOfRangeException(nameof(initialValue));
            _value = initialValue;
        }

        internal long Value => _value;

        internal long Increment()
        {
            if (_value < long.MaxValue) ++_value;
            return _value;
        }
    }

    internal sealed class QueryPolicyDiagnosticCollector
    {
        private const int MaximumDiagnostics = 32;

        private readonly List<ExpressionDiagnostic> _diagnostics = new List<ExpressionDiagnostic>(MaximumDiagnostics);
        private readonly HashSet<Tuple<string, int, int, string>> _reported =
            new HashSet<Tuple<string, int, int, string>>();
        private bool _truncated;

        internal bool IsTruncated => _truncated;

        internal void Add(
            string code,
            string message,
            int start,
            int length,
            long? configuredLimit = null,
            long? observedValue = null,
            bool observedValueIsLowerBound = false,
            string inputPath = null)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (_truncated) return;

            var identity = Tuple.Create(code, start, length, inputPath ?? string.Empty);
            if (!_reported.Add(identity)) return;

            if (_diagnostics.Count < MaximumDiagnostics)
            {
                _diagnostics.Add(new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    code,
                    message,
                    start,
                    length,
                    configuredLimit: configuredLimit,
                    observedValue: observedValue,
                    observedValueIsLowerBound: observedValueIsLowerBound,
                    inputPath: inputPath));
                return;
            }

            _diagnostics.RemoveAt(MaximumDiagnostics - 1);
            _diagnostics.Add(new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "query-policy-diagnostics-truncated",
                "Additional diagnostics were omitted; fix the reported issues before retrying.",
                start,
                0,
                inputPath: inputPath));
            _truncated = true;
        }

        internal IReadOnlyList<ExpressionDiagnostic> ToReadOnlyList(bool sourceOrder = false)
        {
            var diagnostics = sourceOrder
                ? _diagnostics.OrderBy(diagnostic => diagnostic.Start)
                    .ThenBy(diagnostic => diagnostic.Length)
                    .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                    .ToArray()
                : _diagnostics.ToArray();
            return Array.AsReadOnly(diagnostics);
        }
    }
}
