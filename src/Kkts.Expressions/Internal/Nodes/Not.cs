using System;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Not : Node
	{
		public Node Node { get; set; }

		public override Expression Build(BuildArgument arg)
		{
			if(Node == null) throw new FormatException(GetErrorMessage());
			try
			{
				return Expression.Not(Node.Build(arg));
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
