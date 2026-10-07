namespace Kkts.Expressions
{
	/// <summary>A positioned syntax error, independent of predicate type validation.</summary>
	public sealed class ExpressionSyntaxDiagnostic
	{
		internal ExpressionSyntaxDiagnostic(string code, string message, int start, int length)
		{
			Code = code;
			Message = message;
			Start = start;
			Length = length;
		}

		/// <summary>
		/// A stable code: unknown-text, unexpected-token, missing-operand,
		/// unmatched-delimiter, unterminated-string, or incomplete-identifier.
		/// </summary>
		public string Code { get; }

		/// <summary>A human-readable explanation of the syntax error.</summary>
		public string Message { get; }

		/// <summary>The zero-based UTF-16 offset into the original input.</summary>
		public int Start { get; }

		/// <summary>The UTF-16 error length; zero denotes a caret position at end of input.</summary>
		public int Length { get; }
	}
}
