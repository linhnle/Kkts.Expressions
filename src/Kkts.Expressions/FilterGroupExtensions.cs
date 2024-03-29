﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public static partial class FilterGroupExtensions
	{
		#region FilterGroup
		public static Expression<Func<T, bool>> BuildPredicate<T>(this FilterGroup filterGroup, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup));
			return (Expression<Func<T, bool>>)FilterExtensions.BuildPredicate(filterGroup.Filters, typeof(T), variableResolver, validProperties, propertyMapping);
		}

		public static LambdaExpression BuildPredicate(this FilterGroup filterGroup, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
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

			return FilterExtensions.BuildPredicateInternal(filterGroup.Filters, type, arg);
		}

		public static Expression<Func<T, bool>> BuildPredicate<T>(this IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			return (Expression<Func<T, bool>>)BuildPredicate(filterGroups, typeof(T), variableResolver, validProperties, propertyMapping);
		}

		public static LambdaExpression BuildPredicate(this IEnumerable<FilterGroup> filterGroups, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
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
			var body = allFilterGroups.Aggregate((Expression)null, (current, next) => current == null
				? FilterExtensions.BuildBody(next.Filters.ToArray(), param, arg)
				: Expression.OrElse(current, FilterExtensions.BuildBody(next.Filters.ToArray(), param, arg)));

			return Expression.Lambda(body, param);
		}

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this FilterGroup filterGroup, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filterGroup == null) throw new ArgumentNullException(nameof(filterGroup)); ;
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = typeof(T),
				VariableResolver = variableResolver,
				PropertyMapping = propertyMapping
			};

			return TryBuildPredicate<T>(filterGroup, arg);
		}

		public static EvaluationResult<T, bool> TryBuildPredicate<T>(this IEnumerable<FilterGroup> filterGroups, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (filterGroups == null) throw new ArgumentNullException(nameof(filterGroups));
			var result = TryBuildPredicate(filterGroups, typeof(T), variableResolver, validProperties, propertyMapping);

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

		public static EvaluationResult TryBuildPredicate(this IEnumerable<FilterGroup> filterGroups, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
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

			return TryBuildPredicate(filterGroups, type, arg);
		}

		#region internal
		internal static EvaluationResult<T, bool> TryBuildPredicate<T>(this FilterGroup filterGroup, BuildArgument arg)
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
				var expression = (Expression<Func<T, bool>>)FilterExtensions.BuildPredicateInternal(filterGroup.Filters, typeof(T), arg);

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

		internal static EvaluationResult TryBuildPredicate(this IEnumerable<FilterGroup> filterGroups, Type type, BuildArgument arg)
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
				var body = allFilterGroups.Aggregate((Expression)null, (current, next) => current == null
					? FilterExtensions.BuildBody(next.Filters.ToArray(), param, arg)
					: Expression.OrElse(current, FilterExtensions.BuildBody(next.Filters.ToArray(), param, arg)));
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

		#region Validation

		internal static bool IsValid(FilterGroup filterGroup, BuildArgument arg)
		{
			return filterGroup != null
				&& filterGroup.Filters != null
				&& filterGroup.Filters.All(p => p.IsValid(arg));
		}

		internal static bool IsValid(this IEnumerable<FilterGroup> filterGroups, BuildArgument arg)
		{
			return filterGroups != null && filterGroups.All(p => IsValid(p, arg));
		}

		#endregion Validation
	}
}
