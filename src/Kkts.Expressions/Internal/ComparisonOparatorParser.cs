using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class ComparisonOparatorParser : Parser
	{
		private bool _isSpecialChar = true;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (!Done && noOfWhiteSpaceIgnored > 0 && NormalizedResult == "not")
			{
				Append(' ');
				Append(@char);
				return true;
			}
			return AcceptOperator(@char, noOfWhiteSpaceIgnored, index, ExpressionGrammar.ComparisonCharacters, ref _isSpecialChar);
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			var opa = NormalizedResult;

            if (!ExpressionGrammar.IsComparison(opa))
			{
				return new List<Parser>(0);
			}

            if (Interpreter.IsMembership(opa))
			{
				return new List<Parser>
				{
					new ArrayParser { LeftHand = false, Previous = this },
					new PropertyParser { LeftHand = true, Previous = this, IsVariable = true, ForInOperator = true }
				};
			}

			return new List<Parser>
			{
				new NumberParser { LeftHand = false, Previous = this },
				new StringParser { LeftHand = false, Previous = this },
				new PropertyParser { LeftHand = false, Previous = this },
				new GroupParser { LeftHand = false, Previous = this },
				new NotOperatorParser { LeftHand = false, Previous = this },
				new NotFunctionParser { LeftHand = false, Previous = this }
			};
		}

		public override bool Validate()
		{
			var result = NormalizedResult;

			var valid = ExpressionGrammar.IsComparison(result);

			return valid;
		}
	}
}
