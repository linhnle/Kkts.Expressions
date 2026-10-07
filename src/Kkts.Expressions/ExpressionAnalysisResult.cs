using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
	/// <summary>An immutable analysis snapshot for highlighting and syntax diagnostics.</summary>
	public sealed class ExpressionAnalysisResult
	{
		internal ExpressionAnalysisResult(IEnumerable<ExpressionToken> tokens,
			IEnumerable<ExpressionSyntaxDiagnostic> diagnostics, bool isComplete)
		{
			Tokens = System.Array.AsReadOnly(tokens.ToArray());
			Diagnostics = System.Array.AsReadOnly(diagnostics.ToArray());
			IsComplete = isComplete;
		}

		/// <summary>Source-ordered, nonoverlapping tokens; whitespace gaps are not tokens.</summary>
		public IReadOnlyList<ExpressionToken> Tokens { get; }

		/// <summary>Source-ordered syntax errors, including errors after recovery.</summary>
		public IReadOnlyList<ExpressionSyntaxDiagnostic> Diagnostics { get; }

		/// <summary>
		/// True for nonblank input with valid syntax. This does not imply a Boolean
		/// result, valid entity properties, resolved variables, or executable values.
		/// </summary>
		public bool IsComplete { get; }
	}
}
