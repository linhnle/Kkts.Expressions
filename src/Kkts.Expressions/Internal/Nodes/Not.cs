using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Not : Node
	{
		public Node Node { get; set; }
		public override bool ContainsArithmetic => Node?.ContainsArithmetic == true;

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

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			arg.CancellationToken.ThrowIfCancellationRequested();
			if (Node == null) throw new FormatException(GetErrorMessage());
			try
			{
				return Expression.Not(await Node.BuildAsync(arg));
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}
