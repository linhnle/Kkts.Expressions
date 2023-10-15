using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal class ComparisonOparatorParser : Parser
	{
		private static readonly char[] SpecialChars = { '=', '!', '<', '>', '@', '*' };
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
			var opa = Result.ToLower();

            if (!Interpreter.ComparisonOperators.Contains(opa))
			{
				return new List<Parser>(0);
			}

            if (opa == Interpreter.ComparisonIn)
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
				new NotOperatorParser { LeftHand = false, Previous = this },
				new NotFunctionParser { LeftHand = false, Previous = this }
			};
		}

		public override bool Validate()
		{
			var result = Result.ToLower();

			var valid = Interpreter.ComparisonOperators.Contains(result);

			return valid;
		}
	}
}
