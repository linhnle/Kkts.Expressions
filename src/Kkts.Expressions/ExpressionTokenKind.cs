namespace Kkts.Expressions
{
	/// <summary>Source classifications returned by expression analysis.</summary>
	public enum ExpressionTokenKind
	{
		/// <summary>An entity property, including its dotted path.</summary>
		Property,
		/// <summary>A variable path, including its dollar prefix.</summary>
		Variable,
		/// <summary>A comparison, logical, arithmetic, or function operator.</summary>
		Operator,
		/// <summary>A literal, including quotes or an adjacent numeric sign.</summary>
		Constant,
		/// <summary>A function dot, scope delimiter, or list separator.</summary>
		Punctuation,
		/// <summary>Source text not recognized by the expression language.</summary>
		Unknown
	}
}
