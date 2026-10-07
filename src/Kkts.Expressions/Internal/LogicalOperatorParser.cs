using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal class LogicalOperatorParser : Parser
	{
		private static readonly char[] SpecialChars = { '|', '&' };
		private static readonly string[] Oparators = new[] { 
			Interpreter.LogicalAnd,
			Interpreter.LogicalAnd2,
			Interpreter.LogicalOr,
			Interpreter.LogicalOr2, 
			"&", 
			"|" 
		};

		private bool _isSpecialChar = true;
		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			return AcceptOperator(@char, noOfWhiteSpaceIgnored, index, SpecialChars, ref _isSpecialChar);
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

			var valid = Oparators.Contains(result);

			return valid;
		}
	}
}
