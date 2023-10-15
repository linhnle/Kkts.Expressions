using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions
{
	public static partial class FilterGroupExtensions
	{
		#region FilterGroup
		public static async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(this FilterGroup filterGroup, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
			return (Expression<Func<T, bool>>)(await FilterExtensions.BuildPredicateAsync(filterGroup.Filters, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken));
		}

		public static Task<LambdaExpression> BuildPredicateAsync(this FilterGroup filterGroup, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};
			if (!IsValid(filterGroup, arg)) throw new InvalidOperationException("The operator or property name is not valid");

			return FilterExtensions.BuildPredicateInternalAsync(filterGroup.Filters, type, arg, cancellationToken);
		}

		public static async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(this IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			return (Expression<Func<T, bool>>)(await BuildPredicateAsync(filterGroups, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken));
		}

		public static async Task<LambdaExpression> BuildPredicateAsync(this IEnumerable<FilterGroup> filterGroups, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};
			if (!IsValid(filterGroups, arg)) throw new InvalidOperationException("The operator or property name is not valid");

			var allFilterGroups = filterGroups as FilterGroup[] ?? filterGroups.ToArray();
			allFilterGroups = allFilterGroups.Where(p => p?.Filters?.Any() == true).ToArray();
			if (!allFilterGroups.Any()) return FilterExtensions.AlwaysTruePredicate(type);

			var param = type.CreateParameterExpression();
			var body = await FilterExtensions.BuildBodyAsync(allFilterGroups[0].Filters.ToArray(), param, arg, cancellationToken);
			for (var i = 1; i < allFilterGroups.Length; i++)
            {
				body = Expression.OrElse(body, await FilterExtensions.BuildBodyAsync(allFilterGroups[i].Filters.ToArray(), param, arg, cancellationToken));

            }

			return Expression.Lambda(body, param);
		}

		public static Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(this FilterGroup filterGroup, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup)); ;
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T),
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return TryBuildPredicateAsync<T>(filterGroup, arg, cancellationToken);
		}

		public static async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(this IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
			var result = await TryBuildPredicateAsync(filterGroups, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken);

			return new EvaluationResult<T, bool>
			{
				Exception = result.Exception,
				InvalidOperators = result.InvalidOperators,
				InvalidProperties = result.InvalidProperties,
				InvalidVariables = result.InvalidVariables,
				Result = (Expression<Func<T, bool>>)result.Result,
				Succeeded = result.Succeeded
			};
		}

		public static Task<EvaluationResult> TryBuildPredicateAsync(this IEnumerable<FilterGroup> filterGroups, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return TryBuildPredicateAsync(filterGroups, type, arg, cancellationToken);
		}

		#region internal
		internal static async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(this FilterGroup filterGroup, BuildArgument arg, CancellationToken cancellationToken = default)
		{
			filterGroup.Filters.ForEach(f => { arg.IsValidProperty(f.Property); arg.IsValidOperator(f.Operator); });
			if (arg.InvalidProperties.Any() || arg.InvalidOperators.Any())
			{
				return new EvaluationResult<T, bool>
				{
					InvalidProperties = arg.InvalidProperties,
					InvalidOperators = arg.InvalidOperators
				};
			}

			try
			{
				var expression = (Expression<Func<T, bool>>)(await FilterExtensions.BuildPredicateInternalAsync(filterGroup.Filters, typeof(T), arg, cancellationToken));

				return new EvaluationResult<T, bool>
				{
					Result = expression,
					Succeeded = true
				};
			}
			catch (Exception ex)
			{
				return new EvaluationResult<T, bool>
				{
					Exception = ex
				};
			}
		}

		internal static async Task<EvaluationResult> TryBuildPredicateAsync(this IEnumerable<FilterGroup> filterGroups, Type type, BuildArgument arg, CancellationToken cancellationToken = default)
		{
			if (!filterGroups.Any()) return new EvaluationResult
			{
				Succeeded = true,
				Result = FilterExtensions.AlwaysTruePredicate(type)
			};

			var allFilterGroups = filterGroups as FilterGroup[] ?? filterGroups.ToArray();

			foreach (var filterGroup in allFilterGroups)
			{
				if (filterGroup is null) throw new InvalidOperationException("FilterGroup can not be null");
				if (filterGroup.Filters is null) throw new InvalidOperationException("Filters of FilterGroup can not be null");

				foreach (var filter in filterGroup.Filters)
				{
					arg.IsValidProperty(filter.Property);
					arg.IsValidOperator(filter.Operator);
				}
			}

			if (arg.InvalidOperators.Any() || arg.InvalidProperties.Any())
			{
				return new EvaluationResult
				{
					InvalidProperties = arg.InvalidProperties,
					InvalidOperators = arg.InvalidOperators
				};
			}

			try
			{
				var param = type.CreateParameterExpression();
                var body = await FilterExtensions.BuildBodyAsync(allFilterGroups[0].Filters.ToArray(), param, arg, cancellationToken);
                for (var i = 1; i < allFilterGroups.Length; i++)
                {
                    body = Expression.OrElse(body, await FilterExtensions.BuildBodyAsync(allFilterGroups[i].Filters.ToArray(), param, arg, cancellationToken));

                }

				return new EvaluationResult
				{
					Succeeded = true,
					Result = Expression.Lambda(body, param)
				};
			}
			catch (Exception ex)
			{
				return new EvaluationResult
				{
					Exception = ex
				};
			}
		}
		#endregion internal

		#endregion FilterGroup
	}
}
