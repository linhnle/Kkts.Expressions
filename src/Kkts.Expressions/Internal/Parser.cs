using Kkts.Expressions.Internal.Nodes;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal abstract class Parser
	{
		private readonly List<char> _chars = new List<char>();

		public Node BuiltNode { get; set; }

		public Parser Previous { get; set; }

		public Parser LastSuccess { get; set; }

		public List<Parser> Chain { get; set; }

		public int StartIndex { get; protected set; } = -1;

		public char StartChar => _chars.Count == 0 ? char.MinValue : _chars[0];

		public int EndIndex { get; protected set; } = -1;

		public bool Done { get; protected set; }

		public bool LeftHand { get; set; } = true;

		public bool EndFunction { get; set; } = false;

		public int Length => _chars.Count;

		public string Result => new string(_chars.ToArray());

		public char PreviousChar => _chars.Count == 0 ? char.MinValue : _chars[_chars.Count - 1];
		
		public virtual IList<Parser> GetNextParsers(char @char)
		{
			return new List<Parser>(0);
		}
		
		public abstract bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup);

		public abstract bool Validate();

		public virtual void EndExpression() { }

		protected void Append(char @char)
		{
			_chars.Add(@char);
		}

		protected void Append(Parser parser)
		{
			_chars.AddRange(parser._chars);
		}
	}
}
