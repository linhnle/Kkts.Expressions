using System.Linq.Expressions;
using System;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Logicality : Node
	{
		public string Operator { get; set; }

		public Node Left { get; set; }

		public Node Right { get; set; }
		public override bool ContainsAddition => Left.ContainsAddition || Right.ContainsAddition;

		public override Expression Build(BuildArgument arg)
		{
			Operator = Operator.ToLower();
			switch (Operator) 
			{
				case Interpreter.LogicalAnd:
				case Interpreter.LogicalAnd2:
					return Expression.AndAlso(Left.Build(arg), Right.Build(arg));
				case Interpreter.LogicalOr:
				case Interpreter.LogicalOr2:
					return Expression.OrElse(Left.Build(arg), Right.Build(arg));
				default:
					return null;
			}

		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			if (!arg.BuildAdditionAsync) return await base.BuildAsync(arg);
			var left = await Left.BuildAsync(arg);
			var right = await Right.BuildAsync(arg);
			switch (Operator)
			{
				case Interpreter.LogicalAnd:
					return Expression.AndAlso(left, right);
				case Interpreter.LogicalOr:
					return Expression.OrElse(left, right);
				default:
					throw new FormatException(GetErrorMessage());
			}
		}
	}
}
