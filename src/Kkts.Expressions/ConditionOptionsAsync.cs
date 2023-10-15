using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions
{
	public partial class ConditionOptions
	{
		public virtual async Task<Condition<T>> BuildConditionAsync<T>(VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			return (Condition<T>)(await BuildConditionAsync(typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken));
		}

		public virtual async Task<Condition> BuildConditionAsync(Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				VariableResolver = variableResolver,
				EvaluationType = type,
				PropertyMapping = propertyMapping,
				CancellationToken = cancellationToken
			};

			var error = new ConditionBase.ErrorInfo();
			var result = new Condition();
			var predicates = new List<LambdaExpression>();
			var exceptions = new List<Exception>();
			OrderByClause orderByClause = null;

			if (Filters != null && Filters.Any())
			{
				var filtersResult = await Filters.TryBuildPredicateAsync(type, arg, cancellationToken);
				if (filtersResult.Succeeded)
				{
					predicates.Add(filtersResult.Result);
				}
				else
				{
					if (filtersResult.Exception != null)
					{
						exceptions.Add(filtersResult.Exception);
					}
				}
			}

			if (FilterGroups != null && FilterGroups.Any())
			{
				var filterGroupsResult = await FilterGroups.TryBuildPredicateAsync(type, arg, cancellationToken);
				if (filterGroupsResult.Succeeded)
				{
					predicates.Add(filterGroupsResult.Result);
				}
				else
				{
					if (filterGroupsResult.Exception != null)
					{
						exceptions.Add(filterGroupsResult.Exception);
					}
				}
			}

			if (!string.IsNullOrEmpty(Where))
			{
				var whereResult = await ExpressionParser.ParseAsync(Where, type, arg);
				if (whereResult.Succeeded)
				{
					predicates.Add(whereResult.Result);
				}
				else
				{
					if (whereResult.Exception != null)
					{
						exceptions.Add(whereResult.Exception);
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(OrderBy))
			{
				var orderByResult = OrderByParser.Parse(OrderBy, arg);
				if (orderByResult.Succeeded)
				{
					orderByClause = orderByResult.Result;
				}
				else
				{
					if (orderByResult.Exception != null)
					{
						exceptions.Add(orderByResult.Exception);
					}
				}
			}
			else if (OrderBys != null && OrderBys.Any())
			{
				var orderBysResult = OrderByParser.Parse(OrderBys.ToArray(), arg);
				if (orderBysResult.Succeeded)
				{
					orderByClause = orderBysResult.Result;
				}
				else
				{
					if (orderBysResult.Exception != null)
					{
						exceptions.Add(orderBysResult.Exception);
					}
				}
			}

			var isInvalid = arg.InvalidProperties.Any() || arg.InvalidOperators.Any() || arg.InvalidVariables.Any() || exceptions.Any();
			result.IsValid = !isInvalid;
			if (isInvalid)
			{
				error.EvaluationResult = new EvaluationResultBase
				{
					InvalidProperties = arg.InvalidProperties,
					InvalidValues = arg.InvalidValues,
					InvalidOperators = arg.InvalidOperators,
					InvalidVariables = arg.InvalidVariables,
					InvalidOrderByDirections = arg.InvalidOrderByDirections
				};
				error.Exceptions = exceptions;
				result.Error = error;
			}
			else
			{
				result.Predicates = predicates;
				result.OrderByClause = orderByClause;
			}

			return result;
		}
	}
}
