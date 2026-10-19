using System;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
	/// <summary>One ordering key of an <see cref="OrderByClause"/>.</summary>
	public sealed class OrderByKey
	{
		internal OrderByKey(LambdaExpression keySelector, bool descending)
		{
			KeySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
			Descending = descending;
		}

		/// <summary>The key selector applied by the clause's <c>Sort</c> methods.</summary>
		public LambdaExpression KeySelector { get; }

		/// <summary>Whether the key sorts in descending order.</summary>
		public bool Descending { get; }
	}
}
