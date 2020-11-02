using System;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal
{
    internal class ExpressionConvertVisitor<T> : ExpressionConvertVisitor
    {
        public ExpressionConvertVisitor(): base(typeof(T)) { }
    }

    internal class ExpressionConvertVisitor : ExpressionVisitor
    {
        protected readonly ParameterExpression ParamExpression;

        public ExpressionConvertVisitor(Type targetType)
        {
            ParamExpression = Expression.Parameter(targetType, "p");
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            return node.ToString().Split('.').Skip(1).Aggregate<string, MemberExpression>(null, (current, p) => (current == null ? Expression.PropertyOrField(ParamExpression, p) : Expression.PropertyOrField(current, p)));
        }

        protected override Expression VisitLambda<TEntity>(Expression<TEntity> node)
        {
            return Expression.Lambda(Visit(node.Body) ?? throw new InvalidOperationException(), ParamExpression);
        }
    }
}
