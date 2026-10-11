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
			arg.ValidateQueryProperty(Name, StartIndex, Name?.Length ?? 0);
			try
			{
				return arg.BuildPropertyExpression(Param, Name);
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
