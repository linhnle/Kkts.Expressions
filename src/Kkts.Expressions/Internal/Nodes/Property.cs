using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Property : Node
	{
		public string Name { get; set; }

		public ParameterExpression Param { get; set; }

		public override Expression Build(BuildArgument options)
		{
			try
			{
				return Param.CreatePropertyExpression(Name);
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
