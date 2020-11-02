using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class PropertyParser : Parser
	{
		private const string KeyWordTrue = Interpreter.True;
		private const string KeyWordFalse = Interpreter.False;
		private const string KeyWordNull = Interpreter.Null;

		public bool IsNull { get; private set; }

		public bool IsBoolean { get; private set; }

		public bool IsVariable { get; set; }

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (Done) return false;
			if(noOfWhiteSpaceIgnored > 0 && Length > 0)
			{
				Done = Length > 0;
				if (Done) EndIndex = index - noOfWhiteSpaceIgnored;
				return false;
			}

			var prevChar = PreviousChar;
			if (prevChar == char.MinValue && char.IsDigit(@char)) return false;
			if (char.IsLetter(@char) || @char == '_' || char.IsDigit(@char))
			{
				if (prevChar == char.MinValue) StartIndex = index;
				Append(@char);
				return true;
			}

			
			Done = Length > 0;
			if (Done) EndIndex = index - 1;

			return false;
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			if (@char == '.')
			{
				Append(@char);
				return new List<Parser> { this };
			}

			if (LeftHand)
			{
				return new List<Parser>
				{
					new ComparisonOparatorParser { Previous = this },
					new LogicalOperatorParser { Previous = this }
				};
			}

			return new List<Parser>
			{
				new LogicalOperatorParser { Previous = this }
			};
		}

		public override bool Validate()
		{
			var result = Result;

			IsNull = result == KeyWordNull;
			IsBoolean = result == KeyWordFalse || result == KeyWordTrue;

			return true;
		}
	}
}
