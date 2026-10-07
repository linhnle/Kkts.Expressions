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
		public override bool ContainsArithmetic => Left.ContainsArithmetic || Right.ContainsArithmetic;

		public override Expression Build(BuildArgument arg)
		{
			switch (Operator) 
			{
				case Interpreter.LogicalAnd:
				case Interpreter.LogicalAnd2:
					return Expression.AndAlso(Left.Build(arg), Right.Build(arg));
				case Interpreter.LogicalOr:
				case Interpreter.LogicalOr2:
					return Expression.OrElse(Left.Build(arg), Right.Build(arg));
				default:
					throw new FormatException(GetErrorMessage());
			}

		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			arg.CancellationToken.ThrowIfCancellationRequested();
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
