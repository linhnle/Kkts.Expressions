using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Comparison : Node
	{
		public string Operator { get; set; }

		public Node Left { get; set; }

		public Node Right { get; set; }
		public override bool ContainsAddition => Left.ContainsAddition || Right?.ContainsAddition == true;

		public override Expression Build(BuildArgument arg)
		{
			return BuildCore(arg, node => Task.FromResult(node.Build(arg))).GetAwaiter().GetResult();
		}

		public override Task<Expression> BuildAsync(BuildArgument arg)
		{
			return arg.BuildAdditionAsync ? BuildCore(arg, node => node.BuildAsync(arg)) : base.BuildAsync(arg);
		}

		private async Task<Expression> BuildCore(BuildArgument arg, Func<Node, Task<Expression>> build)
		{
			try
			{
				Expression left = null;
				Expression right = null;
				Type inOperatorDataType = typeof(string);
				if (ContainsAddition)
				{
					var leftConstant = Unwrap(Left) as Constant;
					if (!Left.ContainsAddition && leftConstant != null && !leftConstant.IsVariable)
					{
						right = await build(Right);
						PrepareCounterpart(Left, right.Type);
						left = await build(Left);
					}
					else
					{
						if (leftConstant?.IsVariable == true) leftConstant.UseNaturalType = true;
						left = await build(Left);
						if (Operator == Interpreter.ComparisonIn) inOperatorDataType = left.Type;
						if (Right is ArrayList array)
						{
							array.Type = left.Type;
							inOperatorDataType = left.Type;
						}
						else
						{
							PrepareCounterpart(Right, left.Type);
						}
						right = await build(Right);
					}
					if (Operator != Interpreter.ComparisonIn)
					{
						if (NumericOperands.IsNumeric(left.Type) || NumericOperands.IsNumeric(right.Type))
							NumericOperands.Normalize(ref left, ref right, Left.IsConstantValue, Right.IsConstantValue);
						else if (left.Type == typeof(string) && NumericOperands.IsNull(right))
							right = Expression.Constant(null, typeof(string));
						else if (right.Type == typeof(string) && NumericOperands.IsNull(left))
							left = Expression.Constant(null, typeof(string));
					}
				}
				else if (Left is Constant c && Right is Property p)
				{
					if (c.Type == null)
					{
						var propEx = (MemberExpression)await build(p);
						c.Type = propEx.Type;
						left = await build(c);
						right = propEx;
					}
					else
					{
						left = await build(c);
						right = await build(p);
					}
				}
				else if (Left is Property p2 && Right is Constant c2)
				{
					var propEx = (MemberExpression)await build(p2);
                    c2.Type = propEx.Type;
                    left = propEx;
					right = await build(c2);
					inOperatorDataType = c2.Type is null ? left.Type : c2.Type;
                }
				else if (Left is Property p3 && Right is ArrayList al)
				{
					var propEx = (MemberExpression)await build(p3);
					al.Type = propEx.Type;
					left = propEx;
					right = await build(al);
					inOperatorDataType = al.Type;
				}
				else if (Left is Constant c3 && Right is ArrayList al2)
				{
					if (c3.Type != null) al2.Type = c3.Type;
					else if (c3.Type == null)
					{
						c3.Type = typeof(int);
						al2.Type = c3.Type;
					}

					inOperatorDataType = c3.Type;
					left = await build(c3);
					right = await build(al2);
				}
				else
				{
					left = await build(Left);
					right = Right == null ? null : await build(Right);
				}

				if (left == null) throw new FormatException(GetErrorMessage());
				if (string.IsNullOrEmpty(Operator)) return left;

				switch (Operator)
				{
					case Interpreter.ComparisonContains:
						return Expression.Call(left, Interpreter.StringContainsMethod, right);
					case Interpreter.ComparisonStartsWith:
						return Expression.Call(left, Interpreter.StringStartsWithMethod, right);
					case Interpreter.ComparisonEndsWith:
						return Expression.Call(left, Interpreter.StringEndsWithMethod, right);
					case Interpreter.ComparisonEqual:
					case Interpreter.ComparisonEqual2:
						return Expression.Equal(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonGreaterThan:
						return Expression.GreaterThan(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonGreaterThanOrEqual:
						return Expression.GreaterThanOrEqual(left,
							right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonLessThan:
						return Expression.LessThan(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonLessThanOrEqual:
						return Expression.LessThanOrEqual(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonNotEqual:
						return Expression.NotEqual(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.LogicalNot:
						return left;
					case Interpreter.ComparisonIn:
						return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new Type[] { inOperatorDataType }, right, left);
					default:
						throw new FormatException(GetErrorMessage());
				}
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}

		private static Node Unwrap(Node node) => node is Group group ? Unwrap(group.Node) : node;

		private static void PrepareCounterpart(Node node, Type type)
		{
			if (!(Unwrap(node) is Constant constant)) return;
			if (constant.IsVariable)
			{
				constant.UseNaturalType = true;
				return;
			}
			if (constant.Value == null && type.IsValueType && Nullable.GetUnderlyingType(type) == null)
				type = typeof(Nullable<>).MakeGenericType(type);
			constant.Type = type;
		}
	}
}
