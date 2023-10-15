using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions
{
    public static partial class FilterExtensions
	{
		#region Filter

		public static async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(this Filter filter, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			return (Expression<Func<T, bool>>)(await BuildPredicateAsync(filter, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken));
		}

		public static Task<LambdaExpression> BuildPredicateAsync(this Filter filter, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
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

			return Interpreter.BuildPredicateAsync(filter.Operator.GetComparisonOperator(), arg.MapProperty(filter.Property), filter.Value, type, arg.VariableResolver, cancellationToken);
		}

		public static async Task<Expression<Func<T, bool>>> BuildPredicateAsync<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			return (Expression<Func<T, bool>>)(await BuildPredicateAsync(filters, typeof(T), variableResolver, validProperties, propertyMapping));
		}

		public static Task<LambdaExpression> BuildPredicateAsync(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
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

			return BuildPredicateInternalAsync(filters, type, arg, cancellationToken);
		}

		public static async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(this Filter filter, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filter == null) throw new ArgumentNullException(nameof(filter));
			var result = await TryBuildPredicateAsync(filter, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken);

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

		public static async Task<EvaluationResult<T, bool>> TryBuildPredicateAsync<T>(this IEnumerable<Filter> filters, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
		{
			if (filters == null) throw new ArgumentNullException(nameof(filters));
			var result = await TryBuildPredicateAsync(filters, typeof(T), variableResolver, validProperties, propertyMapping, cancellationToken);

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

		public static Task<EvaluationResult> TryBuildPredicateAsync(this Filter filter, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
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

			return TryBuildPredicateAsync(filter, type, arg, cancellationToken);
		}

		public static Task<EvaluationResult> TryBuildPredicateAsync(this IEnumerable<Filter> filters, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null, CancellationToken cancellationToken = default)
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

			return TryBuildPredicateAsync(filters, type, arg, cancellationToken);
		}

        #endregion Filter

        #region internal

        internal static async Task<LambdaExpression> BuildPredicateInternalAsync(this IEnumerable<Filter> filters, Type type, BuildArgument arg, CancellationToken cancellationToken)
        {
            if (!IsValid(filters, arg)) throw new InvalidOperationException("The operator or property name is not valid");

            var allFilters = filters as Filter[] ?? filters.ToArray();
            if (allFilters.Length == 0) return AlwaysTruePredicate(type);

            var param = type.CreateParameterExpression();
            var body = await BuildBodyAsync(allFilters, param, arg, cancellationToken);

            return Expression.Lambda(body, param);
        }

        internal static async Task<EvaluationResult> TryBuildPredicateAsync(this Filter filter, Type type, BuildArgument arg, CancellationToken cancellationToken)
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
                var expression = await Interpreter.BuildPredicateAsync(filter.Operator.GetComparisonOperator(), arg.MapProperty(filter.Property), filter.Value, type, arg.VariableResolver, cancellationToken);

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

        internal static async Task<EvaluationResult> TryBuildPredicateAsync(this IEnumerable<Filter> filters, Type type, BuildArgument arg, CancellationToken cancellationToken)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!filters.Any()) return new EvaluationResult
            {
                Succeeded = true,
                Result = AlwaysTruePredicate(type)
            };

            var allFilters = filters as Filter[] ?? filters.ToArray();

            foreach (var filter in allFilters)
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
                var body = await BuildBodyAsync(allFilters, param, arg, cancellationToken);
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

        #region shared

        internal static async Task<Expression> BuildBodyAsync(Filter[] filters, ParameterExpression param, BuildArgument arg, CancellationToken cancellationToken)
        {
            if (!filters.Any()) return AlwaysTruePredicate(param.Type);
            var result = await BuildConditionAsync(param, filters[0], arg, cancellationToken);
            for (var i = 1; i < filters.Length; ++i)
            {
                result = Expression.AndAlso(result, await BuildConditionAsync(param, filters[i], arg, cancellationToken));
            }

            return result;
        }

        internal static Task<Expression> BuildConditionAsync(ParameterExpression param, Filter filter, BuildArgument arg, CancellationToken cancellationToken)
        {
            var prop = param.CreatePropertyExpression(arg.MapProperty(filter.Property));
            return Interpreter.BuildBodyAsync(filter.Operator.GetComparisonOperator(), prop, filter.Value, arg.VariableResolver, cancellationToken);
        }

        #endregion private
    }
}
