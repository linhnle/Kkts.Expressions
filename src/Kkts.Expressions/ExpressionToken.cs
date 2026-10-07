namespace Kkts.Expressions
{
	/// <summary>An immutable classification of a half-open range in the original input.</summary>
	public sealed class ExpressionToken
	{
		internal ExpressionToken(ExpressionTokenKind kind, int start, int length)
		{
			Kind = kind;
			Start = start;
			Length = length;
		}

		/// <summary>The source classification used to select an editor style.</summary>
		public ExpressionTokenKind Kind { get; }

		/// <summary>The zero-based UTF-16 offset into the unmodified input.</summary>
		public int Start { get; }

		/// <summary>The positive UTF-16 length, including literal delimiters and variable prefixes.</summary>
		public int Length { get; }
	}
}
