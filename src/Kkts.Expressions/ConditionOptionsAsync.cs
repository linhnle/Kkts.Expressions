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

			var predicates = new List<LambdaExpression>();
			var exceptions = new List<Exception>();

			if (Filters != null && Filters.Any())
			{
				AddPredicate(await Filters.TryBuildPredicateAsync(type, arg, cancellationToken), predicates, exceptions);
			}

			if (FilterGroups != null && FilterGroups.Any())
			{
				AddPredicate(await FilterGroups.TryBuildPredicateAsync(type, arg, cancellationToken), predicates, exceptions);
			}

			if (!string.IsNullOrEmpty(Where))
			{
				AddPredicate(await ExpressionParser.ParseAsync(Where, type, arg), predicates, exceptions);
			}

			var orderByClause = BuildOrderByClause(arg, exceptions);
			return CreateCondition(arg, predicates, exceptions, orderByClause);
		}
	}
}
