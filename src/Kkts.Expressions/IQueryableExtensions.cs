using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public static class IQueryableExtensions
	{
		public static IQueryable<T> Take<T>(this IQueryable<T> source, Condition<T> condition, Pagination pagination)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));

			source = Where(source, condition);

			return source.Skip(pagination.Offset).Take(pagination.Limit);
		}

		public static PagedResult<TResult> TakePage<T, TResult>(this IQueryable<T> source, Condition<T> condition, Pagination pagination, Expression<Func<T, TResult>> select)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));
			if (select == null) throw new ArgumentNullException(nameof(select));

			source = Where(source, condition);

			var result = new PagedResult<TResult>(pagination)
			{
				TotalRecords = source.Count(),
				Records = source.Skip(pagination.Offset).Take(pagination.Limit).Select(@select).ToArray()
			};

			return result;
		}

		public static PagedResult<T> TakePage<T>(this IQueryable<T> source, Condition<T> condition, Pagination pagination)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));

			source = Where(source, condition);

			var result = new PagedResult<T>(pagination)
			{
				TotalRecords = source.Count(),
				Records = source.Skip(pagination.Offset).Take(pagination.Limit).ToArray()
			};

			return result;
		}

		public static IQueryable<T> Where<T>(this IQueryable<T> source, Condition<T> condition)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (!condition.IsValid) throw new InvalidOperationException("Condition is not valid");

			foreach (var predicate in condition.Predicates)
			{
				source = source.Where(predicate);
			}

			if (condition.OrderByClause != null)
			{
				source = condition.OrderByClause.Sort(source);
			}

			return source;
		}

		#region OrderBy

		#region Generic

		public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, string expression, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{expression} is required", nameof(expression));

			var evaluationResult = OrderByParser.Parse(expression, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});

			if (!evaluationResult.Succeeded) throw new InvalidOperationException("The property name or direction is not valid");

			return evaluationResult.Result.Sort(source);
		}

		public static IOrderedQueryable<T> OrderBy<T>(this IQueryable<T> source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));

			var evaluationResult = OrderByParser.Parse(orderByInfos.ToArray(), new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});

			if (!evaluationResult.Succeeded) throw new InvalidOperationException("The property name or direction is not valid");

			return evaluationResult.Result.Sort(source);
		}

		public static EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(this IQueryable<T> source, string expression, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{expression} is required", nameof(expression));

			return TryOrderBy(source, expression, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});
		}

		public static EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(this IQueryable<T> source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));

			return TryOrderBy(source, orderByInfos, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});
		}

		#region Internal

		internal static EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(this IQueryable<T> source, string expression, BuildArgument arg)
		{
			var evaluationResult = OrderByParser.Parse(expression, arg);

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedQueryable<T>>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedQueryable<T>>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}

		internal static EvaluationResult<IOrderedQueryable<T>> TryOrderBy<T>(this IQueryable<T> source, IEnumerable<OrderByInfo> orderByInfos, BuildArgument arg)
		{
			var evaluationResult = OrderByParser.Parse(orderByInfos.ToArray(), arg);

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedQueryable<T>>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedQueryable<T>>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}

		#endregion Internal

		#endregion Generic

		#region Common

		public static IOrderedQueryable OrderBy(this IQueryable source, string expression, IEnumerable<string> validProperties = null) 
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{expression} is required", nameof(expression));

			var evaluationResult = OrderByParser.Parse(expression, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = source.ElementType
			});

			if (!evaluationResult.Succeeded) throw new InvalidOperationException("The property name or direction is not valid");

			return evaluationResult.Result.Sort(source);
		}

		public static IOrderedQueryable OrderBy(this IQueryable source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));

			var evaluationResult = OrderByParser.Parse(orderByInfos.ToArray(), new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = source.ElementType
			});

			if (!evaluationResult.Succeeded) throw new InvalidOperationException("The property name or direction is not valid");

			return evaluationResult.Result.Sort(source);
		}

		public static EvaluationResult<IOrderedQueryable> TryOrderBy(this IQueryable source, string expression, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{expression} is required", nameof(expression));

			return TryOrderBy(source, expression, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = source.ElementType
			});
		}

		public static EvaluationResult<IOrderedQueryable> TryOrderBy(this IQueryable source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));

			return TryOrderBy(source, orderByInfos, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = source.ElementType
			});
		}

		#region Internal

		internal static EvaluationResult<IOrderedQueryable> TryOrderBy(this IQueryable source, string expression, BuildArgument arg)
		{
			var evaluationResult = OrderByParser.Parse(expression, arg);

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedQueryable>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedQueryable>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}

		internal static EvaluationResult<IOrderedQueryable> TryOrderBy(this IQueryable source, IEnumerable<OrderByInfo> orderByInfos, BuildArgument arg)
		{
			var evaluationResult = OrderByParser.Parse(orderByInfos.ToArray(), arg);

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedQueryable>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedQueryable>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}

		#endregion Internal

		#endregion Common

		#endregion OrderBy
	}
}
