using System;
using System.Collections;
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
				if (IsVariable)
				{
					if (arg.VariableResolver.TryResolve(Value, out var value))
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
						throw new InvalidCastException($"Invalid variable, name {Value}");
					}
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
