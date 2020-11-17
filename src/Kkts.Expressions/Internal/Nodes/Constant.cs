using System;
using System.Linq.Expressions;

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
				if (IsVariable && arg.VariableResolver.TryResolve(Value, out var value))
				{
					return Expression.Constant(Convert.ChangeType(value, Type));
				}

				return Expression.Constant(Value.Cast(Type), Type);
			}
			catch (Exception ex)
			{
				arg.InvalidValues.Add(Value);
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
