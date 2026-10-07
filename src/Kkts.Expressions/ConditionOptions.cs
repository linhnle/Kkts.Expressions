using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	public partial class ConditionOptions
	{
		public virtual string OrderBy { get; set; }

		public virtual List<OrderByInfo> OrderBys { get; set; }

		public virtual IEnumerable<FilterGroup> FilterGroups { get; set; }

		public virtual IEnumerable<Filter> Filters { get; set; }

		public virtual string Where { get; set; }

		public virtual Condition<T> BuildCondition<T>(VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			return (Condition<T>)BuildCondition(typeof(T), variableResolver, validProperties, propertyMapping);
		}

		public virtual Condition BuildCondition(Type type, VariableResolver variableResolver = null, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));
			var arg = new BuildArgument
			{
				ValidProperties = validProperties,
				VariableResolver = variableResolver,
				EvaluationType = type,
				PropertyMapping = propertyMapping
			};

			var predicates = new List<LambdaExpression>();
			var exceptions = new List<Exception>();

			if (Filters != null && Filters.Any())
			{
				AddPredicate(Filters.TryBuildPredicate(type, arg), predicates, exceptions);
			}

			if (FilterGroups != null && FilterGroups.Any())
			{
				AddPredicate(FilterGroups.TryBuildPredicate(type, arg), predicates, exceptions);
			}

			if (!string.IsNullOrEmpty(Where))
			{
				AddPredicate(ExpressionParser.Parse(Where, type, arg), predicates, exceptions);
			}

			var orderByClause = BuildOrderByClause(arg, exceptions);
			return CreateCondition(arg, predicates, exceptions, orderByClause);
		}

		private static void AddPredicate(EvaluationResult evaluation, ICollection<LambdaExpression> predicates, ICollection<Exception> exceptions)
		{
			if (evaluation.Succeeded) predicates.Add(evaluation.Result);
			else if (evaluation.Exception != null) exceptions.Add(evaluation.Exception);
		}

		private OrderByClause BuildOrderByClause(BuildArgument arg, ICollection<Exception> exceptions)
		{
			EvaluationResult<OrderByClause> evaluation;
			if (!string.IsNullOrWhiteSpace(OrderBy))
			{
				evaluation = OrderByParser.Parse(OrderBy, arg);
			}
			else if (OrderBys != null && OrderBys.Any())
			{
				evaluation = OrderByParser.Parse(OrderBys.ToArray(), arg);
			}
			else return null;

			if (evaluation.Succeeded) return evaluation.Result;
			if (evaluation.Exception != null) exceptions.Add(evaluation.Exception);
			return null;
		}

		private static Condition CreateCondition(BuildArgument arg, List<LambdaExpression> predicates, List<Exception> exceptions, OrderByClause orderByClause)
		{
			var isInvalid = arg.InvalidProperties.Any() || arg.InvalidOperators.Any() || arg.InvalidVariables.Any() || exceptions.Any();
			var result = new Condition { IsValid = !isInvalid };
			if (isInvalid)
			{
				result.Error = new ConditionBase.ErrorInfo
				{
					EvaluationResult = new EvaluationResultBase
					{
						InvalidProperties = arg.InvalidProperties,
						InvalidValues = arg.InvalidValues,
						InvalidOperators = arg.InvalidOperators,
						InvalidVariables = arg.InvalidVariables,
						InvalidOrderByDirections = arg.InvalidOrderByDirections
					},
					Exceptions = exceptions
				};
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
