using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal
{
    internal static class ExpressionOperatorRules
    {
        internal static bool CanApplyComparison(string op, Type leftType, Type rightType)
        {
            try
            {
                ApplyComparison(
                    op,
                    Expression.Parameter(leftType, "left"),
                    Expression.Parameter(rightType, "right"));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static Expression ApplyComparison(string op, Expression left, Expression right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            switch (ExpressionGrammar.NormalizeOperator(op))
            {
                case Interpreter.ComparisonEqual2:
                    return Expression.Equal(left, right);
                case Interpreter.ComparisonNotEqual:
                    return Expression.NotEqual(left, right);
                case Interpreter.ComparisonGreaterThan:
                    return Expression.GreaterThan(left, right);
                case Interpreter.ComparisonGreaterThanOrEqual:
                    return Expression.GreaterThanOrEqual(left, right);
                case Interpreter.ComparisonLessThan:
                    return Expression.LessThan(left, right);
                case Interpreter.ComparisonLessThanOrEqual:
                    return Expression.LessThanOrEqual(left, right);
                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op, "Unsupported comparison operator.");
            }
        }
    }
}
