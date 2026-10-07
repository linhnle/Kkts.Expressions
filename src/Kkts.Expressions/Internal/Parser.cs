using Kkts.Expressions.Internal.Nodes;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal abstract class Parser
	{
		private readonly List<char> _chars = new List<char>();
		private string _result;
		private string _normalizedResult;

		public Node BuiltNode { get; set; }

		public Parser Previous { get; set; }

		public Parser LastSuccess { get; set; }

		public List<Parser> Body { get; set; }

		public int StartIndex { get; protected set; } = -1;

		public char StartChar => _chars.Count == 0 ? char.MinValue : _chars[0];

		public int EndIndex { get; protected set; } = -1;

		public bool Done { get; protected set; }

		public bool LeftHand { get; set; } = true;

		public bool EndFunction { get; set; } = false;

		public int Length => _chars.Count;

		public string Result => _result ?? (_result = new string(_chars.ToArray()));

		public string NormalizedResult => _normalizedResult ?? (_normalizedResult = Result.ToLowerInvariant());

		public char PreviousChar => _chars.Count == 0 ? char.MinValue : _chars[_chars.Count - 1];
		
		public virtual IList<Parser> GetNextParsers(char @char)
		{
			return new List<Parser>(0);
		}
		
		public abstract bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup);

		public abstract bool Validate();

		public virtual void EndExpression() { }

		protected IList<Parser> GetAdditiveParsers()
		{
			return new List<Parser>
			{
				new AdditiveOperatorParser { Previous = this, LeftHand = LeftHand, EndFunction = EndFunction }
			};
		}

		protected bool AcceptOperator(char value, int whitespace, int index, char[] specialChars, ref bool isSpecialChar)
		{
			if (Done || whitespace > 0 && Length > 0)
			{
				Done = Length > 0;
				if (Done) EndIndex = index - whitespace;
				return false;
			}

			if (PreviousChar == char.MinValue)
			{
				StartIndex = index;
				isSpecialChar = specialChars.Contains(value);
				if (!isSpecialChar && !char.IsLetter(value)) return false;
			}
			else if (!(isSpecialChar ? specialChars.Contains(value) : char.IsLetter(value)))
			{
				Done = true;
				EndIndex = index - 1;
				return false;
			}

			Append(value);
			return true;
		}

		protected void Append(char @char)
		{
			_chars.Add(@char);
			_result = _normalizedResult = null;
		}

		protected void Append(Parser parser)
		{
			_chars.AddRange(parser._chars);
			_result = _normalizedResult = null;
		}
	}
}
