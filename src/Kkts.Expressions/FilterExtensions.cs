using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public static partial class FilterExtensions
	{
		#region Filter

		public static Expression<Func<T, bool>> BuildPredicate<T>(this Filter filter, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			return (Expression<Func<T, bool>>)BuildPredicate(filter, typeof(T), variableResolver, validProperties, propertyMapping);
		}

		public static LambdaExpression BuildPredicate(this Filter filter, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};
			if (!IsValid(filter, arg)) throw new InvalidOperationException("The operator or property name is not valid");

			return Interpreter.BuildPredicate(filter.Operator.GetComparisonOperator(), arg.MapProperty(filter.Property), filter.Value, type, arg.VariableResolver);
		}

		public static Expression<Func<T, bool>> BuildPredicate<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			return (Expression<Func<T, bool>>)BuildPredicate(filters, typeof(T), variableResolver, validProperties, propertyMapping);
		}

		public static LambdaExpression BuildPredicate(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return BuildPredicateInternal(filters, type, arg);
		}

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this Filter filter, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			var result = TryBuildPredicate(filter, typeof(T), variableResolver, validProperties, propertyMapping);

			return new EvaluationResult<T, bool>
			{
				Exception = result.Exception,
				InvalidOperators = result.InvalidOperators,
				InvalidProperties = result.InvalidProperties,
				InvalidValues = result.InvalidValues,
				InvalidVariables = result.InvalidVariables,
				Result = (Expression<Func<T, bool>>)result.Result,
				Succeeded = result.Succeeded
			};
		}

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			var result = TryBuildPredicate(filters, typeof(T), variableResolver, validProperties, propertyMapping);

			return new EvaluationResult<T, bool>
			{
				Exception = result.Exception,
				InvalidOperators = result.InvalidOperators,
				InvalidProperties = result.InvalidProperties,
				InvalidValues = result.InvalidValues,
				InvalidVariables = result.InvalidVariables,
				Result = (Expression<Func<T, bool>>)result.Result,
				Succeeded = result.Succeeded
			};
		}

		public static EvaluationResult TryBuildPredicate(this Filter filter, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return TryBuildPredicate(filter, type, arg);
		}

		public static EvaluationResult TryBuildPredicate(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return TryBuildPredicate(filters, type, arg);
		}

		#region internal

		internal static LambdaExpression BuildPredicateInternal(this IEnumerable<Filter> filters, Type type, BuildArgument arg)
		{
			if (!IsValid(filters, arg)) throw new InvalidOperationException("The operator or property name is not valid");

			var allFilters = filters as Filter[] ?? filters.ToArray();
			if (allFilters.Length == 0) return AlwaysTruePredicate(type);

			var param = type.CreateParameterExpression();
			var body = BuildBody(allFilters, param, arg);

			return Expression.Lambda(body, param);
		}

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
				var expression = Interpreter.BuildPredicate(filter.Operator.GetComparisonOperator(), arg.MapProperty(filter.Property), filter.Value, type, arg.VariableResolver);

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
				var body = BuildBody(allFilters, param, arg);
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

		internal static bool IsValid(this Filter filter, BuildArgument arg)
		{
			return arg.IsValidProperty(filter.Property)
				&& !string.IsNullOrWhiteSpace(filter?.Operator)
				&& Interpreter.ComparisonOperators.Contains(filter.Operator.ToLower());
		}

		internal static bool IsValid(this IEnumerable<Filter> filters, BuildArgument arg)
		{
			return filters != null && filters.All(p => IsValid(p, arg));
		}

		#endregion Validation

		#region shared

		internal static LambdaExpression AlwaysTruePredicate(this Type type)
		{
			var param = type.CreateParameterExpression();
			var @constant = Expression.Constant(true);
			return Expression.Lambda(@constant, param);
		}

		internal static Expression BuildBody(Filter[] filters, ParameterExpression param, BuildArgument arg)
		{
			if (!filters.Any()) return AlwaysTruePredicate(param.Type);
			var condition = filters.Aggregate((Expression)null, (current, next) => current == null ? BuildCondition(param, next, arg) : Expression.AndAlso(current, BuildCondition(param, next, arg)));
			return condition;
		}

		internal static Expression BuildCondition(ParameterExpression param, Filter filter, BuildArgument arg)
		{
			var prop = param.CreatePropertyExpression(arg.MapProperty(filter.Property));
			return Interpreter.BuildBody(filter.Operator.GetComparisonOperator(), prop, filter.Value, arg.VariableResolver);
		}

		#endregion private
	}
}
