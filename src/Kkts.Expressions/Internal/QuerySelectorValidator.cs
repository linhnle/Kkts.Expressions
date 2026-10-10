using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal
{
    internal static class QuerySelectorValidator
    {
        internal static void Validate(Type entityType, string fieldName, LambdaExpression selector)
        {
            if (entityType == null) throw new ArgumentNullException(nameof(entityType));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (selector.Parameters.Count != 1 ||
                selector.Parameters[0].Type != entityType)
                throw Invalid(fieldName, "the selector must have one parameter of the entity type");

            try
            {
                new SupportedExpressionVisitor(selector.Parameters[0]).Visit(selector.Body);
            }
            catch (UnsupportedExpressionException exception)
            {
                throw Invalid(fieldName, exception.Message);
            }
        }

        private static ArgumentException Invalid(string fieldName, string reason)
        {
            return new ArgumentException(
                $"Selector for field '{fieldName}' is invalid: {reason}.",
                "selector");
        }

        private sealed class SupportedExpressionVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _entityParameter;

            internal SupportedExpressionVisitor(ParameterExpression entityParameter)
            {
                _entityParameter = entityParameter;
            }

            public override Expression Visit(Expression node)
            {
                if (node == null) return null;
                if (!IsSupported(node.NodeType))
                    throw new UnsupportedExpressionException(
                        $"expression node '{node.NodeType}' is not supported");
                return base.Visit(node);
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (!ReferenceEquals(node, _entityParameter))
                    throw new UnsupportedExpressionException(
                        "the selector contains a parameter other than its entity parameter");
                return node;
            }

            protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
            {
                throw new UnsupportedExpressionException(
                    "nested lambda expressions are not supported");
            }

            protected override Expression VisitExtension(Expression node)
            {
                throw new UnsupportedExpressionException(
                    "extension expressions are not supported");
            }

            protected override Expression VisitIndex(IndexExpression node)
            {
                throw new UnsupportedExpressionException(
                    "index access is not supported");
            }

            private static bool IsSupported(ExpressionType nodeType)
            {
                switch (nodeType)
                {
                    case ExpressionType.Constant:
                    case ExpressionType.Parameter:
                    case ExpressionType.MemberAccess:
                    case ExpressionType.Call:
                    case ExpressionType.Convert:
                    case ExpressionType.ConvertChecked:
                    case ExpressionType.TypeAs:
                    case ExpressionType.UnaryPlus:
                    case ExpressionType.Negate:
                    case ExpressionType.NegateChecked:
                    case ExpressionType.Not:
                    case ExpressionType.Add:
                    case ExpressionType.AddChecked:
                    case ExpressionType.Subtract:
                    case ExpressionType.SubtractChecked:
                    case ExpressionType.Multiply:
                    case ExpressionType.MultiplyChecked:
                    case ExpressionType.Divide:
                    case ExpressionType.Modulo:
                    case ExpressionType.And:
                    case ExpressionType.AndAlso:
                    case ExpressionType.Or:
                    case ExpressionType.OrElse:
                    case ExpressionType.ExclusiveOr:
                    case ExpressionType.Equal:
                    case ExpressionType.NotEqual:
                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                    case ExpressionType.Coalesce:
                    case ExpressionType.Conditional:
                    case ExpressionType.Default:
                        return true;
                    default:
                        return false;
                }
            }
        }

        private sealed class UnsupportedExpressionException : Exception
        {
            internal UnsupportedExpressionException(string message)
                : base(message)
            {
            }
        }
    }
}
