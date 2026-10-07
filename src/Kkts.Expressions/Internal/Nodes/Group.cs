using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Group : Node
	{
		public Node Node { get; set; }
		public override bool ContainsArithmetic => Node.ContainsArithmetic;
		public override bool IsConstantValue => Node.IsConstantValue;

		public override Expression Build(BuildArgument arg)
		{
			return Node.Build(arg);
		}

		public override Task<Expression> BuildAsync(BuildArgument arg) =>
			Node.BuildAsync(arg);
	}
}
