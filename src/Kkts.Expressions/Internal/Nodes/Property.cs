using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Property : Node
	{
		public string Name { get; set; }

		public ParameterExpression Param { get; set; }

		public override Expression Build(BuildArgument arg)
		{
			try
			{
				return Param.CreatePropertyExpression(arg.MapProperty(Name));
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
