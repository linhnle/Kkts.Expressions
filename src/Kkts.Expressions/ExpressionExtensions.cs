using Kkts.Expressions.Internal;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public static class ExpressionExtensions
	{
		/// <summary>
		/// Create OR expression from two expression parameters.
		/// </summary>
		public static Expression<Func<T, bool>> Or<T>(
			this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
		{
			if (left == null) throw new ArgumentNullException(nameof(left));
			if (right == null) throw new ArgumentNullException(nameof(right));
			var invokedExpr = Expression.Invoke(right, left.Parameters);
			return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, invokedExpr), left.Parameters);
		}

		/// <summary>
		/// Create AND expression from two expression parameters.
		/// </summary>
		public static Expression<Func<T, bool>> And<T>(
			this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
		{
			if (left == null) throw new ArgumentNullException(nameof(left));
			if (right == null) throw new ArgumentNullException(nameof(right));
			var invokedExpr = Expression.Invoke(right, left.Parameters);
			return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, invokedExpr), left.Parameters);
		}

		public static string GetPropertyPath<TEntity, TProperty>(this Expression<Func<TEntity, TProperty>> expression)
		{
			if (expression == null) throw new ArgumentNullException(nameof(expression));
			var expressionString = expression.Body.ToString();

			return expressionString.Substring(expressionString.IndexOf('.') + 1);
		}

		public static string GetPropertyPath(this LambdaExpression propertyLambdaExpression)
		{
			if (propertyLambdaExpression == null) throw new ArgumentNullException(nameof(propertyLambdaExpression));
			var expressionString = propertyLambdaExpression.Body.ToString();

			return expressionString.Substring(expressionString.IndexOf('.') + 1);
		}

		public static string GetPropertyName<TEntity, TProperty>(this Expression<Func<TEntity, TProperty>> expression)
		{
			if (expression == null) throw new ArgumentNullException(nameof(expression));

			return ((MemberExpression)expression.Body).Member.Name;
		}

		public static string GetPropertyName(this LambdaExpression propertyLambdaExpression)
		{
			if (propertyLambdaExpression == null) throw new ArgumentNullException(nameof(propertyLambdaExpression));

			return ((MemberExpression)propertyLambdaExpression.Body).Member.Name;
		}

		public static Expression<Func<TTarget, bool>> Convert<TSource, TTarget>(this Expression<Func<TSource, bool>> expression)
		{
			if (expression == null) throw new ArgumentNullException(nameof(expression));

			return (Expression<Func<TTarget, bool>>)new ExpressionConvertVisitor<TTarget>().Visit(expression);
		}

		public static Expression Convert(this Expression expression, Type targetType)
		{
			if (expression == null) throw new ArgumentNullException(nameof(expression));
			if (targetType == null) throw new ArgumentNullException(nameof(targetType));

			return new ExpressionConvertVisitor(targetType).Visit(expression);
		}

		public static LambdaExpression Convert(this LambdaExpression expression, Type targetType)
		{
			return (LambdaExpression)Convert((Expression)expression, targetType);
		}

		internal static MemberExpression CreatePropertyExpression(this ParameterExpression param, string propertyName)
		{
			var body = propertyName.Split('.', StringSplitOptions.RemoveEmptyEntries).Aggregate<string, MemberExpression>(null, (current, p) => (current == null ? Expression.PropertyOrField(param, p) : Expression.PropertyOrField(current, p)));

			return body;
		}

		internal static ParameterExpression CreateParameterExpression(this Type type)
		{
			return Expression.Parameter(type, "p");
		}

		internal static LambdaExpression CreatePropertyLambda(this ParameterExpression param, MemberExpression body)
		{
			return Expression.Lambda(body, param);
		}
	}
}