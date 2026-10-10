using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class ArrayParser : Parser
	{
		private bool _endArray = false;
		private bool _startArray = false;
		private bool _isInEntity = false;
		private char _endScope = ']';

		internal bool HasOpenList => _startArray && !_endArray;
		internal char ClosingDelimiter => _endScope;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (_endArray) return false;
			if (!_startArray)
			{
				if (ExpressionGrammar.IsListStart(@char))
				{
					StartIndex = index;
					keepTrack = true;
					_startArray = true;
					_endScope = ExpressionGrammar.ListEnd(@char);
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
					Append(ExpressionGrammar.Escape);
				}
			}

			if (@char == ExpressionGrammar.Escape)
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

	}
}
