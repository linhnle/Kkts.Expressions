using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
	public static class IEnumerableExtensions
	{
		
		public static IEnumerable<T> Take<T>(this IEnumerable<T> source, Condition<T> condition, Pagination pagination)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));

			source = Where(source, condition);

			return source.Skip(pagination.Offset).Take(pagination.Limit);
		}


		public static PagedResult<TResult> TakePage<T, TResult>(this IEnumerable<T> source, Condition<T> condition, Pagination pagination, Func<T, TResult> select)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));
			if (select == null) throw new ArgumentNullException(nameof(select));

			source = Where(source, condition);
			var enumerable = source.ToArray();
			var result = new PagedResult<TResult>(pagination)
			{
				TotalRecords = enumerable.Length,
				Records = enumerable.Skip(pagination.Offset).Take(pagination.Limit).Select(select).ToArray()
			};

			return result;
		}

		public static PagedResult<T> TakePage<T>(this IEnumerable<T> source, Condition<T> condition, Pagination pagination)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));
			if (pagination == null) throw new ArgumentNullException(nameof(pagination));

			source = Where(source, condition);
			var enumerable = source.ToArray();
			var result = new PagedResult<T>(pagination)
			{
				TotalRecords = enumerable.Length,
				Records = enumerable.Skip(pagination.Offset).Take(pagination.Limit).ToArray()
			};

			return result;
		}

		public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Condition<T> condition)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (condition == null) throw new ArgumentNullException(nameof(condition));

			foreach (var predicate in condition.Predicates)
			{
				source = source.Where(predicate.Compile());
			}

			if (condition.OrderByClause != null)
			{
				source = condition.OrderByClause.Sort(source);
			}

			return source;
		}

		#region OrderBy
		#region Generic
		public static IOrderedEnumerable<T> OrderBy<T>(this IEnumerable<T> source, string expression, IEnumerable<string> validProperties = null) where T : class
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

		public static IOrderedEnumerable<T> OrderBy<T>(this IEnumerable<T> source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
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

		public static EvaluationResult<IOrderedEnumerable<T>> TryOrderBy<T>(this IEnumerable<T> source, string expression, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{expression} is required", nameof(expression));

			var evaluationResult = OrderByParser.Parse(expression, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedEnumerable<T>>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedEnumerable<T>>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}

		public static EvaluationResult<IOrderedEnumerable<T>> TryOrderBy<T>(this IEnumerable<T> source, IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (orderByInfos == null) throw new ArgumentNullException(nameof(orderByInfos));

			var evaluationResult = OrderByParser.Parse(orderByInfos.ToArray(), new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T)
			});

			if (!evaluationResult.Succeeded)
			{
				return new EvaluationResult<IOrderedEnumerable<T>>
				{
					InvalidProperties = evaluationResult.InvalidProperties,
					InvalidOrderByDirections = evaluationResult.InvalidOrderByDirections,
					Exception = evaluationResult.Exception,
				};
			}

			return new EvaluationResult<IOrderedEnumerable<T>>
			{
				Result = evaluationResult.Result.Sort(source),
				Succeeded = true
			};
		}
		#endregion Generic
		#endregion OrderBy
	}
}
