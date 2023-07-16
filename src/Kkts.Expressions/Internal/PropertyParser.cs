using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class PropertyParser : Parser
	{
		private const string KeyWordTrue = Interpreter.True;
		private const string KeyWordFalse = Interpreter.False;
		private const string KeyWordNull = Interpreter.Null;
		private PropertyParser _nestedParser;

		public bool IsNull { get; private set; }

		public bool IsBoolean { get; private set; }

		public bool IsVariable { get; set; }

		public bool ForInOperator { get; set; }

		public bool IsNestedProperty { get; set; }

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (Done) return false;
			var prevChar = PreviousChar;
			if (IsNestedProperty)
			{
				if (@char == '.')
				{
					_nestedParser = new PropertyParser();
					return true;
				}
				else
				{
					var accepted = _nestedParser.Accept(@char, noOfWhiteSpaceIgnored, index, ref keepTrack, ref isStartGroup);
					if (!accepted)
					{
						if (@char != '(' && _nestedParser.Done)
						{
							Append('.');
							Append(_nestedParser);
						}

						Done = _nestedParser.Done;
						if (Done) EndIndex = index - 1;
					}

					return accepted;
				}
			}

			if (noOfWhiteSpaceIgnored > 0 && Length > 0)
			{
				Done = Length > 0;
				if (Done) EndIndex = index - noOfWhiteSpaceIgnored;
				return false;
			}

			if (prevChar == char.MinValue)
			{
                if (@char == '$')
                {
                    IsVariable = true;
					StartIndex = index;
                    return true;
                }

                if (char.IsDigit(@char))
				{
					return false;
				}
			}

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
				IsNestedProperty = true;
				Done = false;
				return new List<Parser> { this, new ComparisonFunctionOperatorParser { Previous = this } };
			}

			if (EndFunction)
			{
				return new List<Parser>(0);
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

		public override void EndExpression()
		{
			if (IsNestedProperty && !Done && _nestedParser.Validate())
			{
				Append('.');
				Append(_nestedParser);
			}
		}
	}
}
