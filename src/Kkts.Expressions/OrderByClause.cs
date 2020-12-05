using Kkts.Expressions.Internal;
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
			var result = Sort(source, _parser.PropertyName, _parser.Descending, false);
			return _parser.ThenBys.Any() ? _parser.ThenBys.Aggregate(result, (current, thenByItem) => Sort(current, thenByItem.Name, thenByItem.Descending, true)) : result;
		}

		public IOrderedQueryable<T> Sort<T>(IQueryable<T> source)
		{
			return (IOrderedQueryable<T>)Sort(source as IQueryable);
		}

		public IOrderedQueryable Sort(IQueryable source)
		{
			var result = Sort(source, _parser.PropertyName, _parser.Descending, false);
			return _parser.ThenBys.Any() ? _parser.ThenBys.Aggregate(result, (current, thenByItem) => Sort(current, thenByItem.Name, thenByItem.Descending, true)) : result;
		}

		internal static IOrderedQueryable Sort(IQueryable source, string propertyName, bool descending, bool thenBy)
		{
			return (IOrderedQueryable)source.Provider.CreateQuery(CreateMethodCallExpression(source, propertyName, descending, thenBy));
		}

		private static IOrderedEnumerable<T> Sort<T>(IEnumerable<T> source, string propertyName, bool descending, bool thenBy)
		{
			var type = typeof(T);
			var param = Expression.Parameter(type, "p");
			var propertyExpression = param.CreatePropertyExpression(propertyName);
			var sort = param.CreatePropertyLambda(propertyExpression);
			var sourceParam = Expression.Parameter(typeof(IEnumerable<T>), "source");
			var sortParam = Expression.Parameter(sort.Type, "keySelector");
			var call = Expression.Call(
				typeof(Enumerable),
				(!thenBy ? nameof(Enumerable.OrderBy) : nameof(Enumerable.ThenBy)) + (descending ? "Descending" : string.Empty),
				new[] { type, propertyExpression.Type },
				sourceParam,
				sortParam);

			return (IOrderedEnumerable<T>)call.Method.Invoke(null, new object[] { source, sort.Compile() });
		}

		private static MethodCallExpression CreateMethodCallExpression(IQueryable source, string propertyName, bool descending, bool thenBy)
		{
			var param = Expression.Parameter(source.ElementType, "p");
			var propertyExpression = param.CreatePropertyExpression(propertyName);
			var sort = param.CreatePropertyLambda(propertyExpression);
			var call = Expression.Call(
				typeof(Queryable),
				(!thenBy ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy)) + (descending ? "Descending" : string.Empty),
				new[] { source.ElementType, propertyExpression.Type },
				source.Expression,
				Expression.Quote(sort));

			return call;
		}
	}
}
