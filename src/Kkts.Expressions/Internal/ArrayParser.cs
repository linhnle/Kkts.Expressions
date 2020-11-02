using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal class ArrayParser : Parser
	{
		private readonly char[] StartScopes = { '[', '(', '{' };
		private bool _endArray = false;
		private bool _startArray = false;
		private bool _isInEntity = false;
		private char _endScope = ']';

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (_endArray) return false;
			if (!_startArray)
			{
				if (StartScopes.Contains(@char))
				{
					StartIndex = index;
					keepTrack = true;
					_startArray = true;
					_endScope = GetEndScope(@char);
					return true;
				}

				return false;
			}

			if (_isInEntity)
			{
				_isInEntity = false;
				if (@char == _endScope)
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

			if (@char == _endScope)
			{
				Done = true;
				EndIndex = index;
				keepTrack = false;
				_endArray = true;
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
					new ComparisonOparatorParser { Previous = this }
				};
			}

			return new List<Parser>
			{
				new LogicalOperatorParser { Previous = this }
			};
		}

		public override bool Validate()
		{
			return _endArray;
		}

		private char GetEndScope(char startScope)
		{
			switch (startScope)
			{
				case '[': return ']';
				case '(': return ')';
				default: return '}';
			}
		}
	}
}
