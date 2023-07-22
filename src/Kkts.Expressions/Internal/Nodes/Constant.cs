using System;
using System.Collections;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Constant : Node
	{
		public string Value { get; set; }

		public Type Type { get; set; }

		public bool IsVariable { get; set; }

		public override Expression Build(BuildArgument arg)
		{
			try
			{
				if (IsVariable)
				{
					var resolved = arg.VariableResolver.TryResolve(Value, out var value);

					return GetVariableExpression(value, resolved, arg);
				}

				return Expression.Constant(Value.Cast(Type), Type);
			}
			catch (Exception ex)
			{
				arg.InvalidValues.Add(Value);
				throw new FormatException(GetErrorMessage(), ex);
			}
		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
        {
			try
			{
				if (IsVariable)
				{
					var variableInfo = await arg.VariableResolver.ResolveAsync(Value, arg.CancellationToken);

					return GetVariableExpression(variableInfo.Value, variableInfo.Resolved, arg);
				}

				return Expression.Constant(Value.Cast(Type), Type);
			}
			catch (Exception ex)
			{
				arg.InvalidValues.Add(Value);
				throw new FormatException(GetErrorMessage(), ex);
			}
		}

		private Expression GetVariableExpression(object value, bool resolved, BuildArgument arg)
        {
			if (resolved)
            {
				if (value is IEnumerable)
				{
					var valueType = value.GetType();
					if (valueType.IsGenericType)
					{
						Type = valueType.GetGenericArguments()[0];
					}
					else if (valueType.IsArray)
					{
						Type = valueType.GetElementType();
					}

					return Expression.Constant(value);
				}

				return Expression.Constant(Convert.ChangeType(value, Type));
			}
            else
            {
				arg.InvalidVariables.Add(Value);
				arg.InvalidProperties.Add(Value);
				throw new InvalidCastException($"Invalid variable or property, name {Value}");
			}
		}
	}
}
