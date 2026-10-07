using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class AdditiveOperatorParser : Parser
	{
		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (Done || (@char != '+' && @char != '-')) return false;
			StartIndex = EndIndex = index;
			Append(@char);
			Done = true;
			return true;
		}

		public override bool Validate() => Done;

		public override void EndExpression() => Done = false;

		public override IList<Parser> GetNextParsers(char @char)
		{
			return new List<Parser>
			{
				new NumberParser { Previous = this, LeftHand = LeftHand, EndFunction = EndFunction },
				new StringParser { Previous = this, LeftHand = LeftHand, EndFunction = EndFunction },
				new PropertyParser { Previous = this, LeftHand = LeftHand, EndFunction = EndFunction },
				new GroupParser { Previous = this, LeftHand = LeftHand, EndFunction = EndFunction }
			};
		}
	}
}
