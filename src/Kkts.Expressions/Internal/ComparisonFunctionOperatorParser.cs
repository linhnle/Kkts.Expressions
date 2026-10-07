using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class ComparisonFunctionOperatorParser : Parser
	{
		private const char Beginning = '(';
		private const char Ending = ')';
		private bool _isStarted = false;
		private bool _isEnd = false;
		private bool _startBoby = false;
		private IList<Parser> _parsers;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (!_isStarted && @char == '.')
			{
				return true;
			}

			if (!_startBoby && (char.IsLetter(@char) || @char == Beginning))
			{
				if (!_isStarted)
				{
					_isStarted = true;
					StartIndex = index;
				}

				if (@char == Beginning)
				{
					if (!StartBody(ref isStartGroup)) return false;
				}
				else
				{
					Append(@char);
				}

				return true;
			}

			if (_isEnd) return false;

			if (@char == Ending)
			{
				Done = true;
				_isEnd = true;
				EndIndex = index;
				_parsers = new List<Parser>
				{
					new ComparisonOparatorParser { Previous = this },
					new LogicalOperatorParser { Previous = this }
				};
				return true;
			}

			return false;
		}

		private bool StartBody(ref bool isStartGroup)
		{
			_startBoby = ExpressionGrammar.IsFunction(NormalizedResult);
			if (!_startBoby) return false;
			isStartGroup = true;
			_parsers = new List<Parser>
			{
				new PropertyParser { Previous = this, LeftHand = false, EndFunction = true },
				new StringParser { Previous = this, LeftHand = false, EndFunction = true },
				new NumberParser { Previous = this, LeftHand = false, EndFunction = true },
				new GroupParser { Previous = this, LeftHand = false, EndFunction = true }
			};
			return true;
		}

		public override bool Validate()
		{
			return _isEnd;
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			return _parsers;
		}
	}
}
