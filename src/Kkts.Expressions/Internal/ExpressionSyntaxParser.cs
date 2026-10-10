using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal sealed class ExpressionSyntaxParser
	{
		private List<Parser> _accepted = GetBeginningParsers(null);
		private List<Parser> _next = new List<Parser>(6);
		private readonly Stack<Parser> _groups = new Stack<Parser>();
		private readonly bool _syntaxOnly;
		private HashSet<Parser> _recoveredScopes;
		private bool _keepTrack;
		private bool _isStartGroup;
		private Parser _lastAccepted;

		internal ExpressionSyntaxParser(bool syntaxOnly = false)
		{
			_syntaxOnly = syntaxOnly;
		}

		internal bool KeepsWhitespace => _keepTrack;
		internal bool HasOpenGroups => _groups.Count > 0;
		internal bool IsRecoveredScope(Parser parser) => _recoveredScopes?.Contains(parser) == true;

		internal ExpressionParserExpectation Observe()
		{
			var current = ExpressionParserSlot.None;
			var continuation = ExpressionParserSlot.None;
			var complete = false;
			var tokenStart = -1;
			var tokenLength = 0;
			var list = false;
			var closing = _groups.Count > 0 ? ')' : '\0';
			foreach (var parser in _accepted)
			{
				current |= Slot(parser);
				if (parser.StartIndex >= tokenStart && parser.StartIndex >= 0)
				{
					tokenStart = parser.StartIndex;
					tokenLength = parser.Length;
				}
				if (parser is ArrayParser array && array.HasOpenList)
				{
					list = true;
					closing = array.ClosingDelimiter;
					current |= ExpressionParserSlot.MembershipContent;
				}
				if (parser is StringParser && _keepTrack)
					current |= ExpressionParserSlot.QuotedContent;
				if (parser.StartIndex < 0 ||
					!(parser is PropertyParser property ? property.HasCompleteIdentifier : Validate(parser)))
					continue;
				complete = true;
				AddContinuation(parser, '\0', ref continuation);
				AddContinuation(parser, '+', ref continuation);
				AddContinuation(parser, '.', ref continuation);
			}
			return new ExpressionParserExpectation(
				current, continuation, complete, tokenStart, tokenLength,
				_groups.Count + (list ? 1 : 0), closing);
		}

		private static void AddContinuation(Parser parser, char value, ref ExpressionParserSlot slots)
		{
			var next = parser.GetNextParsersForObservation(value);
			if (next == null) return;
			foreach (var candidate in next) slots |= Slot(candidate);
		}

		private static ExpressionParserSlot Slot(Parser parser)
		{
			if (parser is PropertyParser property)
				return property.ForInOperator ? ExpressionParserSlot.Variable : ExpressionParserSlot.Operand;
			if (parser is NumberParser || parser is StringParser) return ExpressionParserSlot.Operand;
			if (parser is NotOperatorParser || parser is NotFunctionParser) return ExpressionParserSlot.UnaryNot;
			if (parser is GroupParser) return ExpressionParserSlot.Group;
			if (parser is ComparisonOparatorParser) return ExpressionParserSlot.Comparison;
			if (parser is LogicalOperatorParser) return ExpressionParserSlot.Logical;
			if (parser is AdditiveOperatorParser) return ExpressionParserSlot.Additive;
			if (parser is ArrayParser) return ExpressionParserSlot.MembershipList;
			if (parser is ComparisonFunctionOperatorParser) return ExpressionParserSlot.ComparisonFunction;
			return ExpressionParserSlot.None;
		}

		internal void Read(ExpressionReader reader)
		{
			var whitespace = _keepTrack ? 0 : reader.IgnoreWhiteSpace();
			var value = reader.Read();
			if (!TryRead(value, whitespace, reader.CurrentIndex, reader.HasNext))
				throw SyntaxError(_lastAccepted?.Result ?? reader.Current.ToString(), _lastAccepted?.StartIndex ?? reader.CurrentIndex);
		}

		internal bool TryRead(char value, int whitespace, int index, bool hasNext)
		{
			if (_syntaxOnly && value == ')' && !_keepTrack && _groups.Count == 0) return false;
			var parsers = _accepted;
			_accepted = _next;
			_accepted.Clear();
			_next = parsers;
			_isStartGroup = false;
			Parser group = null;

			foreach (var parser in parsers)
			{
				AcceptParser(parser, value, whitespace, index, ref group);
				if (group == null) continue;
				_groups.Push(group);
				_accepted.Clear();
				_accepted.AddRange(group.GetNextParsers(value));
			}

			if (_accepted.Count > 0) return true;
			group = _groups.Count > 0 ? _groups.Peek() : null;
			if (group == null || !group.Accept(value, whitespace, index, ref _keepTrack, ref _isStartGroup))
				return false;

			_groups.Pop();
			if (hasNext) _accepted.AddRange(group.GetNextParsers(value));
			else _accepted.Add(group);
			group.Body = BuildChain(group, group.LastSuccess);
			if (_groups.Count > 0) _groups.Peek().LastSuccess = group;
			return true;
		}

		private bool Validate(Parser parser) =>
			_syntaxOnly && parser is NumberParser ? ExpressionGrammar.IsNumber(parser.Result) : parser.Validate();

		private bool Accept(Parser parser, char value, int whitespace, int index)
		{
			if (_syntaxOnly && value == ')' && parser.Body == null &&
				(parser is NotFunctionParser || parser is ComparisonFunctionOperatorParser)) return false;
			return parser.Accept(value, whitespace, index, ref _keepTrack, ref _isStartGroup);
		}

		private void AcceptParser(Parser parser, char value, int whitespace, int index, ref Parser group)
		{
			if (Accept(parser, value, whitespace, index))
				RecordAccepted(parser, parser, ref group);
			else if (parser.Done && Validate(parser))
			{
				if (_groups.Count > 0) _groups.Peek().LastSuccess = parser;
				foreach (var next in parser.GetNextParsers(value))
				{
					if (Accept(next, value, whitespace, index))
						RecordAccepted(next, parser, ref group);
				}
			}
		}

		private void RecordAccepted(Parser accepted, Parser last, ref Parser group)
		{
			if (_isStartGroup) group = accepted;
			else
			{
				_accepted.Add(accepted);
				_lastAccepted = last;
			}
		}

		internal List<Parser> Complete(ExpressionReader reader)
		{
			if (!TryComplete(out var chain))
				throw SyntaxError(reader.LastChar.ToString(), reader.Length - 1);
			return chain;
		}

		internal bool TryComplete(out List<Parser> chain)
		{
			Parser last = null;
			var count = 0;
			foreach (var parser in _accepted)
			{
				parser.EndExpression();
				if (!Validate(parser)) continue;
				++count;
				last = parser;
			}

			chain = null;
			if (count != 1 || _groups.Count > 0) return false;
			chain = BuildChain(null, last);
			return true;
		}

		internal void Recover()
		{
			var group = _groups.Count > 0 ? _groups.Peek() : null;
			if (group != null)
			{
				if (_recoveredScopes == null) _recoveredScopes = new HashSet<Parser>();
				_recoveredScopes.Add(group);
				group.LastSuccess = null;
			}
			_accepted = GetBeginningParsers(group);
			_next.Clear();
			_keepTrack = false;
			_lastAccepted = null;
		}

		private static FormatException SyntaxError(string value, int index) =>
			new FormatException($"Incorrect syntax near '{value}', index {index}");

		private List<Parser> BuildChain(Parser root, Parser last)
		{
			var chain = new List<Parser>();
			do
			{
				if (_syntaxOnly && last == null) break;
				chain.Add(last);
				var tmp = last;
				last = last.Previous;
				tmp.Previous = null;
			} while (last != root);
			chain.Reverse();
			return chain;
		}

		private static List<Parser> GetBeginningParsers(Parser previous) => new List<Parser>
		{
			new PropertyParser { Previous = previous },
			new NumberParser { Previous = previous },
			new StringParser { Previous = previous },
			new NotOperatorParser { Previous = previous },
			new NotFunctionParser { Previous = previous },
			new GroupParser { Previous = previous }
		};
	}
}
