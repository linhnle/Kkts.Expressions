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
			if (Done || noOfWhiteSpaceIgnored > 0 && Length > 0)
			{
				Done = Length > 0;
				if (Done) EndIndex = index - noOfWhiteSpaceIgnored;
				return false;
			}

			if (PreviousChar == char.MinValue)
			{
				StartIndex = index;
				if (SpecialChars.Contains(@char))
				{
					_isSpecialChar = true;
					Append(@char);

					return true;
				}
				else
				{
					if (char.IsLetter(@char))
					{
						_isSpecialChar = false;
						Append(@char);

						return true;
					}

					return false;
				}
			}

			if (_isSpecialChar)
			{
				if (SpecialChars.Contains(@char))
				{
					Append(@char);

					return true;
				}
				else
				{
					Done = true;
				}
			}
			else
			{
				if (char.IsLetter(@char))
				{
					Append(@char);

					return true;
				}
				else
				{
					Done = true;
				}
			}

			if (Done) EndIndex = index - 1;

			return false;
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
			var result = Result.ToLower();

			var valid = Oparators.Contains(result);

			return valid;
		}
	}
}
