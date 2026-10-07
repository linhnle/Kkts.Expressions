using System.Collections.Generic;
using System.Globalization;

namespace Kkts.Expressions.Internal
{
	internal class NumberParser : Parser
	{
		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (Done) return false;
			if (noOfWhiteSpaceIgnored > 0 && Length > 0)
			{
				Done = Length > 0;
				if (Done) EndIndex = index - noOfWhiteSpaceIgnored;
				return false;
			}

			if (char.IsDigit(@char) || @char == '.' || (@char == '-' && Length == 0))
			{
				if (PreviousChar == char.MinValue) StartIndex = index;
				Append(@char);
				return true;
			}


			Done = Length > 0;
			if (Done) EndIndex = index - 1;

			return false;
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			if (@char == '+' || @char == '-') return GetAdditiveParsers();
			if (EndFunction) return new List<Parser>(0);
			if (LeftHand)
			{
				return new List<Parser>
				{
					new ComparisonOparatorParser  { Previous = this }
				};
			}

			return new List<Parser>
			{
				new LogicalOperatorParser  { Previous = this }
			};
		}

		public override bool Validate()
		{
			return double.TryParse(Result, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
		}
	}
}
