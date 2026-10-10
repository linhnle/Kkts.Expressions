using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal
{
    internal static class TypedFilterExpressionBuilder
    {
        internal static Expression ApplyComparison(
            Expression left,
            ComparisonOperator operation,
            Expression right)
        {
            switch (operation)
            {
                case ComparisonOperator.Equal:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonEqual, left, right);
                case ComparisonOperator.NotEqual:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonNotEqual, left, right);
                case ComparisonOperator.LessThan:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonLessThan, left, right);
                case ComparisonOperator.LessThanOrEqual:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonLessThanOrEqual, left, right);
                case ComparisonOperator.GreaterThan:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonGreaterThan, left, right);
                case ComparisonOperator.GreaterThanOrEqual:
                    return ExpressionOperatorRules.ApplyComparison(Interpreter.ComparisonGreaterThanOrEqual, left, right);
                case ComparisonOperator.Contains:
                    return Expression.Call(left, Interpreter.StringContainsMethod, right);
                case ComparisonOperator.StartsWith:
                    return Expression.Call(left, Interpreter.StringStartsWithMethod, right);
                case ComparisonOperator.EndsWith:
                    return Expression.Call(left, Interpreter.StringEndsWithMethod, right);
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported scalar comparison operator.");
            }
        }

        internal static Expression BuildMembership(
            Expression value,
            Expression collection,
            Type elementType,
            bool negate)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            var membership = Expression.Call(
                typeof(System.Linq.Enumerable),
                nameof(System.Linq.Enumerable.Contains),
                new[] { elementType },
                collection,
                value);
            return negate ? (Expression)Expression.Not(membership) : membership;
        }
    }
}
