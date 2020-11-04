﻿using Kkts.Expressions.Internal;
using Kkts.Expressions.Internal.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Expressions
{
	public static class Interpreter
	{
		internal const string ComparisonEqual = "=";
		internal const string ComparisonEqual2 = "==";
		internal const string ComparisonNotEqual = "!=";
		internal const string ComparisonNotEqual2 = "<>";
		internal const string ComparisonLessThan = "<";
		internal const string ComparisonLessThanOrEqual = "<=";
		internal const string ComparisonGreaterThan = ">";
		internal const string ComparisonGreaterThanOrEqual = ">=";
		internal const string ComparisonContains = "contains";
		internal const string ComparisonContains2 = "contain";
		internal const string ComparisonContains3 = "@";
		internal const string ComparisonStartsWith = "startswith";
		internal const string ComparisonStartsWith2 = "startwith";
		internal const string ComparisonStartsWith3 = "@*";
		internal const string ComparisonEndsWith = "endswith";
		internal const string ComparisonEndsWith2 = "endwith";
		internal const string ComparisonEndsWith3 = "*@";
		internal const string ComparisonIn = "in";
		internal const string Null = "null";
		internal const string True = "true";
		internal const string False = "false";
		internal const string LogicalNot = "!";
		internal const string LogicalAnd = "&&";
		internal const string LogicalAnd2 = "and";
		internal const string LogicalAnd3 = "&";
		internal const string LogicalOr = "||";
		internal const string LogicalOr2 = "or";
		internal const string LogicalOr3 = "|";
		internal static string[] ComparisonOperators =
			{
				ComparisonEqual,
				ComparisonEqual2,
				ComparisonNotEqual,
				ComparisonNotEqual2,
				ComparisonLessThan,
				ComparisonLessThanOrEqual,
				ComparisonGreaterThan,
				ComparisonGreaterThanOrEqual,
				ComparisonContains,
				ComparisonContains2,
				ComparisonContains3,
				ComparisonStartsWith,
				ComparisonStartsWith2,
				ComparisonStartsWith3,
				ComparisonEndsWith,
				ComparisonEndsWith2,
				ComparisonEndsWith3,
				ComparisonIn
			};
		internal static string[] ComparisonFunctionOperators = 
			{
				ComparisonContains,
				ComparisonContains2,
				ComparisonStartsWith,
				ComparisonStartsWith2,
				ComparisonEndsWith,
				ComparisonEndsWith2
			};
		internal static readonly MethodInfo StringContainsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) });
		internal static readonly MethodInfo StringStartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) });
		internal static readonly MethodInfo StringEndsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) });

		public static EvaluationResult<T, bool> ParsePredicate<T>(this string expression, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("Invalid expression", nameof(expression));

			var type = typeof(T);
			var result = ExpressionParser.Parse(expression, type, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver ?? new VariableResolver()
			});

			return new EvaluationResult<T, bool>
			{
				Result = (Expression<Func<T, bool>>)result.Result,
				Exception = result.Exception,
				InvalidProperties = result.InvalidProperties,
				InvalidVariables = result.InvalidVariables,
				InvalidOperators = result.InvalidOperators,
				Succeeded = result.Succeeded
			};
		}

		public static EvaluationResult ParsePredicate(this string expression, Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null)
		{
			if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException($"{nameof(expression)} is required", nameof(expression));
			if (type == null) throw new ArgumentNullException(nameof(type));

			return ExpressionParser.Parse(expression, type, new BuildArgument
			{
				ValidProperties = validProperties,
				EvaluationType = type,
				VariableResolver = variableResolver ?? new VariableResolver()
			});
		}

		public static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(string propertyName, ComparisonOperator @operator, object value, VariableResolver variableResolver = null)
		{
			if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException($"{nameof(propertyName)} is required", nameof(propertyName));
			return (Expression<Func<TEntity, bool>>)BuildPredicate(@operator, propertyName, value, typeof(TEntity), variableResolver ?? new VariableResolver());
		}

		public static LambdaExpression BuildPredicate(string propertyName, ComparisonOperator @operator, object value, Type type, VariableResolver variableResolver = null)
		{
			if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException($"{nameof(propertyName)} is required", nameof(propertyName));
			if (type == null) throw new ArgumentNullException(nameof(type));

			return BuildPredicate(@operator, propertyName, value, type, variableResolver ?? new VariableResolver());
		}

		internal static Expression BuildBody(ComparisonOperator @operator, MemberExpression prop, object value, VariableResolver variableResolver)
		{
			if (value is string && prop.Type != typeof(string) && @operator != ComparisonOperator.In)
			{
				var varName = (string)value;
				if (variableResolver.IsVariable(varName) && variableResolver.TryResolve(varName, out var result))
				{
					value = Convert.ChangeType(result, prop.Type);
				}
				else
				{
					value = varName.Cast(prop.Type);
				}
			}

			@operator = CorrectOperator(prop.Type, @operator);
			switch (@operator)
			{
				case ComparisonOperator.NotEqual:
					return Expression.NotEqual(prop, Expression.Constant(value, prop.Type));
				case ComparisonOperator.LessThan:
					return Expression.LessThan(prop, Expression.Constant(value, prop.Type));
				case ComparisonOperator.LessThanOrEqual:
					return Expression.LessThanOrEqual(prop, Expression.Constant(value, prop.Type));
				case ComparisonOperator.GreaterThan:
					return Expression.GreaterThan(prop, Expression.Constant(value, prop.Type));
				case ComparisonOperator.GreaterThanOrEqual:
					return Expression.GreaterThanOrEqual(prop, Expression.Constant(value, prop.Type));
				case ComparisonOperator.Contains:
					return Expression.Call(prop, StringContainsMethod, Expression.Constant(value, prop.Type));
				case ComparisonOperator.StartsWith:
					return Expression.Call(prop, StringStartsWithMethod, Expression.Constant(value, prop.Type));
				case ComparisonOperator.EndsWith:
					return Expression.Call(prop, StringEndsWithMethod, Expression.Constant(value, prop.Type));
				case ComparisonOperator.In:
					return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new Type[] { prop.Type }, new ArrayList { Type = prop.Type, DrawValue = value.ToString() }.Build(null), prop);
				default:
					return Expression.Equal(prop, Expression.Constant(value, prop.Type));
			}
		}

		internal static ComparisonOperator GetComparisonOperator(this string operatorString)
		{
			if (string.IsNullOrEmpty(operatorString)) throw new ArgumentNullException(nameof(operatorString));

			switch (operatorString.ToLower())
			{
				case ComparisonNotEqual2:
				case ComparisonNotEqual:
					return ComparisonOperator.NotEqual;
				case ComparisonLessThan:
					return ComparisonOperator.LessThan;
				case ComparisonLessThanOrEqual:
					return ComparisonOperator.LessThanOrEqual;
				case ComparisonGreaterThan:
					return ComparisonOperator.GreaterThan;
				case ComparisonGreaterThanOrEqual:
					return ComparisonOperator.GreaterThanOrEqual;
				case ComparisonContains:
				case ComparisonContains2:
				case ComparisonContains3:
					return ComparisonOperator.Contains;
				case ComparisonStartsWith:
				case ComparisonStartsWith2:
				case ComparisonStartsWith3:
					return ComparisonOperator.StartsWith;
				case ComparisonEndsWith:
				case ComparisonEndsWith2:
				case ComparisonEndsWith3:
					return ComparisonOperator.EndsWith;
				case ComparisonEqual:
				case ComparisonEqual2:
					return ComparisonOperator.Equal;
				case ComparisonIn:
					return ComparisonOperator.In;
				default:
					throw new NotSupportedException($"Operator {operatorString} not supported");
			}
		}

		private static ComparisonOperator CorrectOperator(Type type, ComparisonOperator @operator)
		{
			if (type == typeof(string))
			{
				switch (@operator)
				{
					case ComparisonOperator.In:
					case ComparisonOperator.Equal:
					case ComparisonOperator.NotEqual:
					case ComparisonOperator.Contains:
					case ComparisonOperator.StartsWith:
					case ComparisonOperator.EndsWith:
						return @operator;
					default:
						throw new NotSupportedException($"Operator {@operator} not supported for string");
				}
			}

			var requireType = Nullable.GetUnderlyingType(type) ?? type;
			if (requireType == typeof(bool))
			{
				switch (@operator)
				{
					case ComparisonOperator.In:
					case ComparisonOperator.Equal:
					case ComparisonOperator.NotEqual:
						return @operator;
					default:
						throw new NotSupportedException($"Operator {@operator} not supported for boolean");
				}
			}

			if (requireType.IsEnum || requireType == typeof(Guid))
			{
				switch (@operator)
				{
					case ComparisonOperator.In:
					case ComparisonOperator.Equal:
					case ComparisonOperator.NotEqual:
						return @operator;
					default:
						throw new NotSupportedException($"Operator {@operator} not supported for Guid");
				}
			}

			if (!requireType.IsPrimitive && requireType != typeof(decimal) &&
				requireType != typeof(DateTime) && requireType != typeof(DateTimeOffset)) return ComparisonOperator.Equal;
			switch (@operator)
			{
				case ComparisonOperator.In:
				case ComparisonOperator.Equal:
				case ComparisonOperator.NotEqual:
				case ComparisonOperator.LessThan:
				case ComparisonOperator.LessThanOrEqual:
				case ComparisonOperator.GreaterThan:
				case ComparisonOperator.GreaterThanOrEqual:
					return @operator;
				default:
					throw new NotSupportedException($"Operator {@operator} not supported for {requireType.Name}");
			}
		}

		internal static LambdaExpression BuildPredicate(ComparisonOperator @operator, string propertyName, object value, Type type, VariableResolver variableResolver)
		{
			var param = type.CreateParameterExpression();
			var prop = param.CreatePropertyExpression(propertyName);
			var body = BuildBody(@operator, prop, value, variableResolver);

			return Expression.Lambda(body, param);
		}
	}
}
