using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Kkts.Expressions
{
    /// <summary>Identifies the logical shape of a nested filter node.</summary>
    public enum FilterNodeKind
    {
        And,
        Or,
        Not,
        Condition
    }

    /// <summary>An immutable AND, OR, NOT, or atomic condition node.</summary>
    [JsonConverter(typeof(FilterTreeJsonConverter))]
    public sealed class FilterNode
    {
        private FilterNode(
            FilterNodeKind kind,
            IReadOnlyList<FilterNode> children = null,
            FilterNode child = null,
            string field = null,
            string @operator = null,
            FilterValue value = null)
        {
            Kind = kind;
            Children = children ?? Array.AsReadOnly(Array.Empty<FilterNode>());
            Child = child;
            Field = field;
            Operator = @operator;
            Value = value;
        }

        /// <summary>The logical node shape.</summary>
        public FilterNodeKind Kind { get; }

        /// <summary>The ordered operands of an AND or OR node; empty for other node kinds.</summary>
        public IReadOnlyList<FilterNode> Children { get; }

        /// <summary>The single operand of a NOT node; null for other node kinds.</summary>
        public FilterNode Child { get; }

        /// <summary>The entity field of a condition node; null for logical nodes.</summary>
        public string Field { get; }

        /// <summary>The comparison operator of a condition node; null for logical nodes.</summary>
        public string Operator { get; }

        /// <summary>The typed value of a condition node; null for logical nodes.</summary>
        public FilterValue Value { get; }

        /// <summary>Creates an AND node with at least one non-null child.</summary>
        public static FilterNode And(IEnumerable<FilterNode> children) =>
            Group(FilterNodeKind.And, children, nameof(children));

        /// <summary>Creates an OR node with at least one non-null child.</summary>
        public static FilterNode Or(IEnumerable<FilterNode> children) =>
            Group(FilterNodeKind.Or, children, nameof(children));

        /// <summary>Creates a NOT node with exactly one non-null child.</summary>
        public static FilterNode Not(FilterNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            return new FilterNode(FilterNodeKind.Not, child: child);
        }

        /// <summary>Creates an atomic field/operator/value condition.</summary>
        /// <exception cref="ArgumentException">The field is blank or the operator is unsupported.</exception>
        public static FilterNode Condition(string field, string @operator, FilterValue value)
        {
            if (string.IsNullOrWhiteSpace(field))
                throw new ArgumentException("A condition field is required.", nameof(field));
            if (string.IsNullOrWhiteSpace(@operator) ||
                !Interpreter.ComparisonOperators.Contains(@operator.Trim(), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("The condition operator is not supported.", nameof(@operator));
            if (value == null) throw new ArgumentNullException(nameof(value));
            var normalizedOperator = Interpreter.NormalizeComparisonOperator(@operator.Trim());
            if (value.Kind == FilterValueKind.Collection && !Interpreter.IsMembership(normalizedOperator))
                throw new ArgumentException("Collection values are valid only for IN and NOT IN conditions.", nameof(value));
            return new FilterNode(FilterNodeKind.Condition, field: field, @operator: normalizedOperator, value: value);
        }

        private static FilterNode Group(FilterNodeKind kind, IEnumerable<FilterNode> children, string parameterName)
        {
            if (children == null) throw new ArgumentNullException(parameterName);
            var values = children.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("Logical groups must contain at least one child.", parameterName);
            if (values.Any(child => child == null))
                throw new ArgumentException("Logical groups cannot contain null children.", parameterName);
            return new FilterNode(kind, children: new ReadOnlyCollection<FilterNode>(values));
        }
    }
}
