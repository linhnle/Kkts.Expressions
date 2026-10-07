namespace Kkts.Expressions.Internal.Nodes
{
	internal abstract class Arithmetic : Node
	{
		public Node Left { get; set; }
		public Node Right { get; set; }
		public override bool ContainsArithmetic => true;
		public override bool IsConstantValue => Left.IsConstantValue && Right.IsConstantValue;

		protected static void PrepareOperand(Node node)
		{
			if (node is Constant constant) constant.UseNaturalType = true;
			else if (node is Group group) PrepareOperand(group.Node);
		}
	}
}
