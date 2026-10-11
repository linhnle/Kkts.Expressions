using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public sealed class OrderByClause
	{
		private readonly OrderByParser _parser;
		internal OrderByClause(OrderByParser parser)
		{
			_parser = parser;
		}

		public IOrderedEnumerable<T> Sort<T>(IEnumerable<T> source)
		{
			var result = Sort(
				source,
				_parser.PropertyName,
				_parser.PropertyField,
				_parser.Descending,
				false);
			return _parser.ThenBys.Aggregate(
				result,
				(current, thenByItem) => Sort(
					current,
					thenByItem.Name,
					thenByItem.Field,
					thenByItem.Descending,
					true));
		}

		public IOrderedQueryable<T> Sort<T>(IQueryable<T> source)
		{
			return (IOrderedQueryable<T>)Sort(source as IQueryable);
		}

		public IOrderedQueryable Sort(IQueryable source)
		{
			var result = Sort(
				source,
				_parser.PropertyName,
				_parser.PropertyField,
				_parser.Descending,
				false);
			return _parser.ThenBys.Aggregate(
				result,
				(current, thenByItem) => Sort(
					current,
					thenByItem.Name,
					thenByItem.Field,
					thenByItem.Descending,
					true));
		}

		private static IOrderedQueryable Sort(
			IQueryable source,
			string propertyName,
			ResolvedQueryField field,
			bool descending,
			bool thenBy)
		{
			return (IOrderedQueryable)source.Provider.CreateQuery(
				CreateMethodCallExpression(source, propertyName, field, descending, thenBy));
		}

		private static IOrderedEnumerable<T> Sort<T>(
			IEnumerable<T> source,
			string propertyName,
			ResolvedQueryField field,
			bool descending,
			bool thenBy)
		{
			var type = typeof(T);
			var param = Expression.Parameter(type, "p");
			var keyExpression = CreateKeyExpression(param, propertyName, field);
			var sort = Expression.Lambda(keyExpression, param);
			var sourceParam = Expression.Parameter(
				thenBy ? typeof(IOrderedEnumerable<T>) : typeof(IEnumerable<T>),
				"source");
			var sortParam = Expression.Parameter(
				typeof(Func<,>).MakeGenericType(type, keyExpression.Type),
				"keySelector");
			var call = Expression.Call(
				typeof(Enumerable),
				(!thenBy ? nameof(Enumerable.OrderBy) : nameof(Enumerable.ThenBy)) + (descending ? "Descending" : string.Empty),
				new[] { type, keyExpression.Type },
				sourceParam,
				sortParam);

			return (IOrderedEnumerable<T>)call.Method.Invoke(null, new object[] { source, sort.Compile() });
		}

		private static MethodCallExpression CreateMethodCallExpression(
			IQueryable source,
			string propertyName,
			ResolvedQueryField field,
			bool descending,
			bool thenBy)
		{
			var param = Expression.Parameter(source.ElementType, "p");
			var keyExpression = CreateKeyExpression(param, propertyName, field);
			var sort = Expression.Lambda(keyExpression, param);
			var call = Expression.Call(
				typeof(Queryable),
				(!thenBy ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy)) + (descending ? "Descending" : string.Empty),
				new[] { source.ElementType, keyExpression.Type },
				source.Expression,
				Expression.Quote(sort));

			return call;
		}

		private static Expression CreateKeyExpression(
			ParameterExpression parameter,
			string propertyName,
			ResolvedQueryField field)
		{
			return field == null
				? parameter.CreatePropertyExpression(propertyName)
				: field.Compose(parameter);
		}
	}
}
