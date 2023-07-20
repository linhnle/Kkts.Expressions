using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal abstract class Node
	{
		public int StartIndex { get; set; }
		public char StartChar { get; set; }
		public abstract Expression Build(BuildArgument arg);

		public virtual Task<Expression> BuildAsync(BuildArgument arg) => Task.FromResult(Build(arg));

		public string GetErrorMessage()
		{
			return StartChar == '\0' ? "Incorrect syntax" : $"Incorrect syntax near '{StartChar}', index {StartIndex}";
		}
	}
}
