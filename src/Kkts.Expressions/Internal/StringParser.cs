using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal class StringParser : Parser
	{
		private readonly char[] Quotes = { '\'', '"' };
		private bool _endString = false;
		private bool _startString = false;
		private bool _isInEntity = false;
		private char _quote = '"';

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (_endString) return false;
			if(!_startString)
			{
				if (Quotes.Contains(@char))
				{
					StartIndex = index;
					keepTrack = true;
					_startString = true;
					_quote = @char;
					return true;
				}

				return false;
			}

			if (_isInEntity)
			{
				_isInEntity = false;
				if (@char == _quote)
				{
					Append(@char);

					return true;
				}
				else
				{
					Append('\\');
				}
			}

			if (@char == '\\')
			{
				_isInEntity = true;
				return true;
			}

			if (@char == _quote)
			{
				Done = true;
				EndIndex = index;
				keepTrack = false;
				_endString = true;
				return true;
			}

			Append(@char);

			return true;
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			if (LeftHand)
			{
				return new List<Parser>
				{
					new ComparisonOparatorParser  { Previous = this }
				};
			}

			return new List<Parser>
			{
				new LogicalOperatorParser { Previous = this }
			};
		}

		public override bool Validate()
		{
			return _endString;
		}
	}
}
