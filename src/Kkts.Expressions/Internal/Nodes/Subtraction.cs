using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Subtraction : Arithmetic
	{
		public override Expression Build(BuildArgument arg)
		{
			PrepareOperand(Left);
			PrepareOperand(Right);
			return BuildExpression(Left.Build(arg), Right.Build(arg), arg);
		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			PrepareOperand(Left);
			PrepareOperand(Right);
			var left = await Left.BuildAsync(arg);
			var right = await Right.BuildAsync(arg);
			return BuildExpression(left, right, arg);
		}

		private Expression BuildExpression(Expression left, Expression right, BuildArgument arg)
		{
			try
			{
				NumericOperands.Normalize(ref left, ref right, Left.IsConstantValue, Right.IsConstantValue);
				return Expression.Subtract(left, right);
			}
			catch (InvalidOperationException ex)
			{
				arg.InvalidOperators.Add("-");
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
