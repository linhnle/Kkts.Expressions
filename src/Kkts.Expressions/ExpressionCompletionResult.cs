using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Stable categories for UI-independent expression completions.</summary>
    public enum ExpressionCompletionKind
    {
        Field = 0,
        Operator = 1,
        Value = 2,
        Variable = 3,
        LogicalOperator = 4,
        Delimiter = 5
    }

    /// <summary>Distinguishes usable, empty, ambiguous, and resource-limited completion results.</summary>
    public enum ExpressionCompletionStatus
    {
        Available = 0,
        NoMatches = 1,
        ContextUnavailable = 2,
        LimitExceeded = 3
    }

    /// <summary>Immutable presentation and exact-source edit for one completion.</summary>
    public sealed class ExpressionCompletionItem
    {
        internal ExpressionCompletionItem(
            ExpressionCompletionKind kind,
            string label,
            string insertionText,
            int start,
            int length,
            string description = "",
            ExpressionTypeInfo typeInfo = null)
        {
            if (!Enum.IsDefined(typeof(ExpressionCompletionKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Kind = kind;
            Label = label ?? throw new ArgumentNullException(nameof(label));
            InsertionText = insertionText ?? throw new ArgumentNullException(nameof(insertionText));
            Start = start;
            Length = length;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            TypeInfo = typeInfo;
        }

        public ExpressionCompletionKind Kind { get; }
        public string Label { get; }
        public string InsertionText { get; }

        /// <summary>Zero-based UTF-16 start in the exact original input.</summary>
        public int Start { get; }

        /// <summary>UTF-16 length to replace; zero denotes an insertion.</summary>
        public int Length { get; }
        public string Description { get; }

        /// <summary>Public or contextual type metadata; null for untyped constructs.</summary>
        public ExpressionTypeInfo TypeInfo { get; }
    }

    /// <summary>An owned, immutable completion snapshot, not a predicate-validity assertion.</summary>
    public sealed class ExpressionCompletionResult
    {
        internal ExpressionCompletionResult(
            IEnumerable<ExpressionCompletionItem> items,
            IEnumerable<ExpressionDiagnostic> diagnostics,
            ExpressionCompletionStatus status,
            bool isIncomplete = false)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            if (!Enum.IsDefined(typeof(ExpressionCompletionStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            var itemSnapshot = items.ToArray();
            var diagnosticSnapshot = diagnostics.ToArray();
            if (itemSnapshot.Any(item => item == null))
                throw new ArgumentException("Completion items cannot contain null.", nameof(items));
            if (diagnosticSnapshot.Any(diagnostic => diagnostic == null))
                throw new ArgumentException("Completion diagnostics cannot contain null.", nameof(diagnostics));
            Items = Array.AsReadOnly(itemSnapshot);
            Diagnostics = Array.AsReadOnly(diagnosticSnapshot);
            Status = status;
            IsIncomplete = isIncomplete;
        }

        public IReadOnlyList<ExpressionCompletionItem> Items { get; }
        public IReadOnlyList<ExpressionDiagnostic> Diagnostics { get; }
        public ExpressionCompletionStatus Status { get; }

        /// <summary>Whether eligible results or processing work were omitted.</summary>
        public bool IsIncomplete { get; }
    }

    /// <summary>Immutable result-size options; completion safety ceilings are independent.</summary>
    public sealed class ExpressionCompletionOptions
    {
        public ExpressionCompletionOptions(int maxResults = 50)
        {
            if (maxResults < 1 || maxResults > 200)
                throw new ArgumentOutOfRangeException(nameof(maxResults), "The result limit must be between 1 and 200.");
            MaxResults = maxResults;
        }

        public int MaxResults { get; }
    }
}
