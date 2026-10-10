using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Public metadata for a registered query field.</summary>
    public sealed class ExpressionFieldDefinition
    {
        internal ExpressionFieldDefinition(
            string name,
            Type clrType,
            ExpressionNullability nullability,
            bool canFilter,
            bool canSort,
            IEnumerable<ComparisonOperator> allowedOperators,
            string displayName,
            string description)
        {
            Name = name;
            ClrType = clrType;
            Nullability = nullability;
            CanFilter = canFilter;
            CanSort = canSort;
            AllowedOperators = allowedOperators == null
                ? null
                : new ReadOnlyCollection<ComparisonOperator>(allowedOperators.ToArray());
            DisplayName = displayName;
            Description = description;
        }

        /// <summary>The public field name accepted in queries.</summary>
        public string Name { get; }

        /// <summary>The CLR result type of the registered selector.</summary>
        public Type ClrType { get; }

        /// <summary>The declared nullability of the selector result.</summary>
        public ExpressionNullability Nullability { get; }

        /// <summary>Whether clients may filter on this field.</summary>
        public bool CanFilter { get; }

        /// <summary>Whether clients may sort on this field.</summary>
        public bool CanSort { get; }

        /// <summary>
        /// The allowed comparison operators, or null when all applicable operators are allowed.
        /// </summary>
        public IReadOnlyCollection<ComparisonOperator> AllowedOperators { get; }

        /// <summary>An optional application-facing label.</summary>
        public string DisplayName { get; }

        /// <summary>An optional application-facing description.</summary>
        public string Description { get; }
    }
}
