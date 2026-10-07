using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class LogicalOperatorParser : Parser
	{
		private bool _isSpecialChar = true;
		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			return AcceptOperator(@char, noOfWhiteSpaceIgnored, index, ExpressionGrammar.LogicalCharacters, ref _isSpecialChar);
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			return new List<Parser>
			{
				new NumberParser { Previous = this },
				new StringParser { Previous = this },
				new PropertyParser { Previous = this },
				new NotOperatorParser { Previous = this },
				new NotFunctionParser { Previous = this },
				new GroupParser { Previous = this }
			};
		}
		public override bool Validate()
		{
			var result = NormalizedResult;

			var valid = ExpressionGrammar.IsLogical(result);

			return valid;
		}
	}
}
