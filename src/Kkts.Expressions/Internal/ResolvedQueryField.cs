using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions.Internal
{
    internal sealed class ResolvedQueryField
    {
        internal ResolvedQueryField(
            string name,
            string identity,
            Type clrType,
            ExpressionNullability nullability,
            bool canQuery,
            bool canFilter,
            bool canSort,
            IReadOnlyCollection<ComparisonOperator> allowedOperators,
            LambdaExpression selector,
            int navigationDepth,
            bool hasCollectionAccess)
        {
            Name = name;
            Identity = identity;
            ClrType = clrType;
            Nullability = nullability;
            CanQuery = canQuery;
            CanFilter = canFilter;
            CanSort = canSort;
            AllowedOperators = allowedOperators;
            Selector = selector;
            NavigationDepth = navigationDepth;
            HasCollectionAccess = hasCollectionAccess;
        }

        internal string Name { get; }

        internal string Identity { get; }

        internal Type ClrType { get; }

        internal ExpressionNullability Nullability { get; }

        internal bool CanQuery { get; }

        internal bool CanFilter { get; }

        internal bool CanSort { get; }

        internal IReadOnlyCollection<ComparisonOperator> AllowedOperators { get; }

        internal LambdaExpression Selector { get; }

        internal int NavigationDepth { get; }

        internal bool HasCollectionAccess { get; }

        internal bool IsExpressionMapped => Selector != null;

        internal Expression Compose(ParameterExpression entityParameter)
        {
            if (entityParameter == null)
                throw new ArgumentNullException(nameof(entityParameter));

            if (Selector == null)
                return entityParameter.CreatePropertyExpression(Identity);
            if (entityParameter.Type != Selector.Parameters[0].Type)
                throw new ArgumentException(
                    "The ordering or predicate parameter type does not match the registered field.",
                    nameof(entityParameter));

            return new ParameterSubstitutionVisitor(
                Selector.Parameters[0],
                entityParameter).Visit(Selector.Body);
        }

        private sealed class ParameterSubstitutionVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _source;
            private readonly ParameterExpression _replacement;

            internal ParameterSubstitutionVisitor(
                ParameterExpression source,
                ParameterExpression replacement)
            {
                _source = source;
                _replacement = replacement;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return ReferenceEquals(node, _source) ? _replacement : node;
            }
        }
    }
}
