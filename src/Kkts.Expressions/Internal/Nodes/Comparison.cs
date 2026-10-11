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
		public override bool ContainsArithmetic => Left.ContainsArithmetic || Right?.ContainsArithmetic == true;

		public override Expression Build(BuildArgument arg)
		{
			return BuildCore(node => Task.FromResult(node.Build(arg)), arg, asyncBuild: false)
				.GetAwaiter().GetResult();
		}

		public override Task<Expression> BuildAsync(BuildArgument arg)
		{
			arg.CancellationToken.ThrowIfCancellationRequested();
			return BuildCore(node => node.BuildAsync(arg), arg, asyncBuild: true);
		}

		private async Task<Expression> BuildCore(
			Func<Node, Task<Expression>> build,
			BuildArgument arg,
			bool asyncBuild)
		{
			try
			{
				arg.ValidateComparison(Operator, Left, Right, StartIndex);
				if (Interpreter.IsMembership(Operator) && Unwrap(Right) is Constant constant)
				{
					if (asyncBuild)
						await arg.SnapshotMembershipVariableAsync(constant).ConfigureAwait(false);
					else
						arg.SnapshotMembershipVariable(constant);
				}
				var operands = await BuildOperands(build);
				return BuildOperator(operands.Left, operands.Right, operands.InType);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (QueryPolicyException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}

		private async Task<(Expression Left, Expression Right, Type InType)> BuildOperands(Func<Node, Task<Expression>> build)
		{
			if (ContainsArithmetic) return await BuildArithmeticOperands(build);
			if (Left is Constant constant && Right is Property property)
				return await BuildConstantProperty(constant, property, build);
			if (Left is Property leftProperty && Right is Constant rightConstant)
			{
				var left = await build(leftProperty);
				rightConstant.Type = left.Type;
				return (left, await build(rightConstant), rightConstant.Type ?? left.Type);
			}
			if (Left is Property arrayProperty && Right is ArrayList array)
			{
				var left = await build(arrayProperty);
				array.Type = left.Type;
				return (left, await build(array), array.Type);
			}
			if (Left is Constant arrayConstant && Right is ArrayList constantArray)
			{
				arrayConstant.Type = arrayConstant.Type ?? typeof(int);
				constantArray.Type = arrayConstant.Type;
				return (await build(arrayConstant), await build(constantArray), arrayConstant.Type);
			}

			return (await build(Left), Right == null ? null : await build(Right), typeof(string));
		}

		private static async Task<(Expression Left, Expression Right, Type InType)> BuildConstantProperty(Constant constant, Property property, Func<Node, Task<Expression>> build)
		{
			if (constant.Type != null) return (await build(constant), await build(property), typeof(string));
			var right = await build(property);
			constant.Type = right.Type;
			return (await build(constant), right, typeof(string));
		}

		private async Task<(Expression Left, Expression Right, Type InType)> BuildArithmeticOperands(Func<Node, Task<Expression>> build)
		{
			Expression left;
			Expression right;
			var inType = typeof(string);
			var constant = Unwrap(Left) as Constant;
			if (!Left.ContainsArithmetic && constant != null && !constant.IsVariable)
			{
				right = await build(Right);
				PrepareCounterpart(Left, right.Type);
				left = await build(Left);
			}
			else
			{
				if (constant?.IsVariable == true) constant.UseNaturalType = true;
				left = await build(Left);
				if (Interpreter.IsMembership(Operator)) inType = left.Type;
				if (Right is ArrayList array)
				{
					array.Type = left.Type;
					inType = left.Type;
				}
				else PrepareCounterpart(Right, left.Type);
				right = await build(Right);
			}

			if (!Interpreter.IsMembership(Operator)) NormalizeComparison(ref left, ref right);
			return (left, right, inType);
		}

		private void NormalizeComparison(ref Expression left, ref Expression right)
		{
			if (NumericOperands.IsNumeric(left.Type) || NumericOperands.IsNumeric(right.Type))
				NumericOperands.Normalize(ref left, ref right, Left.IsConstantValue, Right.IsConstantValue);
			else if (left.Type == typeof(string) && NumericOperands.IsNull(right))
				right = Expression.Constant(null, typeof(string));
			else if (right.Type == typeof(string) && NumericOperands.IsNull(left))
				left = Expression.Constant(null, typeof(string));
		}

		private Expression BuildOperator(Expression left, Expression right, Type inType)
		{
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
				case Interpreter.ComparisonGreaterThan:
				case Interpreter.ComparisonGreaterThanOrEqual:
				case Interpreter.ComparisonLessThan:
				case Interpreter.ComparisonLessThanOrEqual:
				case Interpreter.ComparisonNotEqual:
					return ExpressionOperatorRules.ApplyComparison(
						Operator,
						left,
						right ?? throw new FormatException(GetErrorMessage()));
				case Interpreter.LogicalNot:
					return left;
				case Interpreter.ComparisonIn:
				case Interpreter.ComparisonNotIn:
					return Interpreter.BuildMembership(left, right, inType, Operator == Interpreter.ComparisonNotIn);
				default:
					throw new FormatException(GetErrorMessage());
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
