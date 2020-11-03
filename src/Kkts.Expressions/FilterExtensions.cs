using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public static class FilterExtensions
	{
		#region Filter
		public static Expression<Func<T, bool>> BuildPredicate<T>(this Filter filter, VariableResolver variableResolver = null)
		{
			return (Expression<Func<T, bool>>)BuildPredicate(filter, typeof(T), variableResolver);
		}

		public static LambdaExpression BuildPredicate(this Filter filter, Type type, VariableResolver variableResolver = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			if (type == null) throw new ArgumentNullException(nameof(type));
			if (!IsValid(filter)) throw new InvalidOperationException("The operator or property name is not valid");

			return Interpreter.BuildPredicate(filter.Operator.GetComparisonOperator(), filter.Property, filter.Value, type, variableResolver ?? new VariableResolver());
		}

		public static Expression<Func<T, bool>> BuildPredicate<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null)
		{
			return (Expression<Func<T, bool>>)BuildPredicate(filters, typeof(T), variableResolver ?? new VariableResolver());
		}

		public static LambdaExpression BuildPredicate(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			if (type == null) throw new ArgumentNullException(nameof(type));
			if (!IsValid(filters)) throw new InvalidOperationException("The operator or property name is not valid");

			var allFilters = filters as Filter[] ?? filters.ToArray();
			if (allFilters.Length == 0) return AlwaysTruePredicate(type);

			var param = type.CreateParameterExpression();
			var body = BuildBody(allFilters, param, variableResolver ?? new VariableResolver());

			return Expression.Lambda(body, param);
		}

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this Filter filter, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			var result = TryBuildPredicate(filter, typeof(T), variableResolver, validProperties);

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

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			var result = TryBuildPredicate(filters, typeof(T), variableResolver, validProperties);

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

		public static EvaluationResult TryBuildPredicate(this Filter filter, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver ?? new VariableResolver()
			};

			return TryBuildPredicate(filter, type, arg);
		}

		public static EvaluationResult TryBuildPredicate(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver ?? new VariableResolver()
			};

			return TryBuildPredicate(filters, type, arg);
		}

		#region internal
		internal static EvaluationResult TryBuildPredicate(this Filter filter, Type type, BuildArgument arg)
		{
			var isInvalid = !arg.IsValidProperty(filter.Property);
			isInvalid |= !arg.IsValidOperator(filter.Operator);
			if (isInvalid)
			{
				return new EvaluationResult
				{
					InvalidProperties = arg.InvalidProperties,
					InvalidOperators = arg.InvalidOperators
				};
			}

			try
			{
				var expression = Interpreter.BuildPredicate(filter.Operator.GetComparisonOperator(), filter.Property, filter.Value, type, arg.VariableResolver);

				return new EvaluationResult
				{
					Result = expression,
					Succeeded = true
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

		internal static EvaluationResult TryBuildPredicate(this IEnumerable<Filter> filters, Type type, BuildArgument arg)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			if (type == null) throw new ArgumentNullException(nameof(type));
			if (!filters.Any()) return new EvaluationResult
			{
				Succeeded = true,
				Result = AlwaysTruePredicate(type)
			};

			var allFilters = filters as Filter[] ?? filters.ToArray();
			
			foreach(var filter in allFilters)
			{
				arg.IsValidProperty(filter?.Property);
				arg.IsValidOperator(filter?.Operator);
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
				var body = BuildBody(allFilters, param, arg.VariableResolver);
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

		#endregion Filter

		#region Validation
		public static bool IsValid(this Filter filter)
		{
			return !string.IsNullOrWhiteSpace(filter?.Property)
				&& !string.IsNullOrWhiteSpace(filter?.Operator)
				&& Interpreter.ComparisonOperators.Contains(filter.Operator.ToLower());
		}

		public static bool IsValid(this IEnumerable<Filter> filters)
		{
			return filters != null && filters.All(p => IsValid(p));
		}

		#endregion Validation

		#region shared
		internal static LambdaExpression AlwaysTruePredicate(this Type type)
		{
			var param = type.CreateParameterExpression();
			var @constant = Expression.Constant(true);
			return Expression.Lambda(@constant, param);
		}

		internal static Expression BuildBody(Filter[] filters, ParameterExpression param, VariableResolver variableResolver)
		{
			if (!filters.Any()) return AlwaysTruePredicate(param.Type);
			var condition = filters.Aggregate((Expression)null, (current, next) => current == null ? BuildCondition(param, next, variableResolver) : Expression.AndAlso(current, BuildCondition(param, next, variableResolver)));
			return condition;
		}

		internal static Expression BuildCondition(ParameterExpression param, Filter filter, VariableResolver variableResolver)
		{
			var prop = param.CreatePropertyExpression(filter.Property);
			return Interpreter.BuildBody(filter.Operator.GetComparisonOperator(), prop, filter.Value, variableResolver);
		}
		#endregion private
	}
}
