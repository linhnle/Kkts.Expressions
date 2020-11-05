using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal class NotOperatorParser : Parser
	{
		private const char Operator = '!';

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (@char == Operator)
			{
				Append(@char);
				StartIndex = index;
				EndIndex = index;
				Done = true;
				return true;
			}

			return false;
		}

		public override bool Validate()
		{
			return true;
		}

        public override IList<Parser> GetNextParsers(char @char)
		{
			return new List<Parser>
				{
					new PropertyParser { LeftHand = LeftHand, Previous = this },
					new NotOperatorParser { LeftHand = LeftHand, Previous = this },
					new NotFunctionParser { LeftHand = LeftHand, Previous = this },
					new GroupParser { LeftHand = LeftHand, Previous = this }
				};
		}
	}
}
