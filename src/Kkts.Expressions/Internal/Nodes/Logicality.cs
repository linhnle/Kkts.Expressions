using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Logicality : Node
	{
		public string Operator { get; set; }

		public Node Left { get; set; }

		public Node Right { get; set; }

		public override Expression Build(BuildArgument options)
		{
			Operator = Operator.ToLower();
			switch (Operator) 
			{
				case Interpreter.LogicalAnd:
				case Interpreter.LogicalAnd2:
					return Expression.AndAlso(Left.Build(options), Right.Build(options));
				case Interpreter.LogicalOr:
				case Interpreter.LogicalOr2:
					return Expression.OrElse(Left.Build(options), Right.Build(options));
				default:
					return null;
			}
		}
	}
}
