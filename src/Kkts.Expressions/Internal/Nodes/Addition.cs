using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Addition : Node
	{
		private static readonly MethodInfo StringConcat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) });
		private static readonly MethodInfo ObjectConcat = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(object), typeof(object) });

		public Node Left { get; set; }
		public Node Right { get; set; }
		public override bool ContainsAddition => true;
		public override bool IsConstantValue => Left.IsConstantValue && Right.IsConstantValue;

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

		internal static void PrepareOperand(Node node)
		{
			if (node is Constant constant) constant.UseNaturalType = true;
			else if (node is Group group) PrepareOperand(group.Node);
		}

		private Expression BuildExpression(Expression left, Expression right, BuildArgument arg)
		{
			try
			{
				if (left.Type == typeof(string) || right.Type == typeof(string))
				{
					if (left.Type == typeof(string) && right.Type == typeof(string))
						return Expression.Call(StringConcat, left, right);
					return Expression.Call(ObjectConcat, Expression.Convert(left, typeof(object)), Expression.Convert(right, typeof(object)));
				}
				NumericOperands.Normalize(ref left, ref right, Left.IsConstantValue, Right.IsConstantValue);
				return Expression.Add(left, right);
			}
			catch (InvalidOperationException ex)
			{
				arg.InvalidOperators.Add("+");
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
