using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class GroupParser : Parser
	{
		private const char Beginning = '(';
		private const char Ending = ')';

		private bool _isStarted = false;
		private bool _isEnd = false;
		private IList<Parser> _parsers;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (!_isStarted)
			{
				if (@char == Beginning)
				{
					_isStarted = true;
					StartIndex = index;
					Append(@char);
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
					return true;
				}

				return false;
			}

			if (keepTrack) return false;

			if (_isEnd) return false;

			if (@char == Ending)
			{
				Append(@char);
				EndIndex = index;
				Done = true;
				_isEnd = true;
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
