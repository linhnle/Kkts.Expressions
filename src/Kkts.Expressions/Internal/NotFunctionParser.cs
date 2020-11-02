using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class NotFunctionParser : Parser
	{
		private const string FuncName = "not";
		private const string Beginning = FuncName + "(";
		private const char Ending = ')';
		private int _charIndex = 0;
		private bool _isStarted = false;
		private bool _isEnd = false;
		private bool _startBoby = false;
		private IList<Parser> _parsers;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			@char = char.ToLower(@char);

			if(!_startBoby && @char == Beginning[_charIndex])
			{
				if (!_isStarted)
				{
					_isStarted = true;
					StartIndex = index;
				}

				++_charIndex;
				Append(@char);
				_startBoby = _charIndex == Beginning.Length;
				if (_startBoby)
				{
					isStartGroup = true;
					_parsers = new List<Parser>
					{
						new PropertyParser { Previous = this },
						new NumberParser { Previous = this },
						new StringParser { Previous = this },
						new NotOperatorParser { Previous = this },
						new NotFunctionParser { Previous = this },
						new GroupParser { Previous = this }
					};
				}

				return true;
			}

			if (keepTrack) return false;

			if (_isEnd) return false;

			if (@char == Ending)
			{
				Append(@char);
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
