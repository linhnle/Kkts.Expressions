using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal abstract class Node
	{
		public int StartIndex { get; set; }
		public char StartChar { get; set; }
		public abstract Expression Build(BuildArgument options);

		public string GetErrorMessage()
		{
			return StartChar == '\0' ? "Incorrect syntax" : $"Incorrect syntax near '{StartChar}', index {StartIndex}";
		}
	}
}
