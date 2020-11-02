using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal class NotOperatorParser : Parser
	{
		private const char Operator = '!';

		private bool _started = false;
		private IList<Parser> _parsers;

		public override bool Accept(char @char, int noOfWhiteSpaceIgnored, int index, ref bool keepTrack, ref bool isStartGroup)
		{
			if (@char == Operator)
			{
				Append(@char);
				_started = true;
				StartIndex = index;
				EndIndex = index;
				Done = true;
				return true;
			}

			if (!_started) return false;

			if (_parsers == null)
			{
				_parsers = new List<Parser>
				{
					new PropertyParser { LeftHand = LeftHand, Previous = this },
					new NotOperatorParser { LeftHand = LeftHand, Previous = this },
					new NotFunctionParser { LeftHand = LeftHand, Previous = this },
					new GroupParser { LeftHand = LeftHand, Previous = this }
				};
			}

			var parsers = new List<Parser>(_parsers.Count);

			foreach(var subParser in _parsers)
			{
				if (subParser.Accept(@char, noOfWhiteSpaceIgnored, index, ref keepTrack, ref isStartGroup))
				{
					parsers.Add(subParser);

					_parsers = parsers;
					return true;
				}
			}

			return false;
		}

		public override bool Validate()
		{
			return _started;
		}

		public override IList<Parser> GetNextParsers(char @char)
		{
			return _parsers.Count == 0 ? base.GetNextParsers(@char) : _parsers[0].GetNextParsers(@char);
		}
	}
}
