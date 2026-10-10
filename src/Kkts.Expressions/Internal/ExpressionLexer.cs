using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
	internal sealed class ExpressionLexer
	{
		private readonly string _source;
		private readonly List<ExpressionToken> _tokens;
		private readonly Action<string, string, int, int> _report;
		private readonly int? _maximumTokens;
		private int _index;
		private bool _operand = true;
		private bool _membership;
		private char _listEnd;

		internal ExpressionLexer(string source, List<ExpressionToken> tokens, Action<string, string, int, int> report,
			int? maximumTokens = null)
		{
			_source = source;
			_tokens = tokens;
			_report = report;
			_maximumTokens = maximumTokens;
		}

		internal bool IsTruncated { get; private set; }

		internal void ReadAll()
		{
			while (_index < _source.Length && !IsTruncated)
			{
				if (char.IsWhiteSpace(_source[_index])) { ++_index; continue; }
				var start = _index;
				var value = _source[_index];
				if (_listEnd != '\0')
				{
					ReadListItem();
					continue;
				}
				if (_membership && ExpressionGrammar.IsListStart(value))
				{
					_listEnd = ExpressionGrammar.ListEnd(value);
					Add(ExpressionTokenKind.Punctuation, start, ++_index);
					_membership = false;
					continue;
				}
				_membership = false;
				if (ExpressionGrammar.IsQuote(value)) ReadString();
				else if (value == VariableResolver.VariablePrefix || ExpressionGrammar.IsIdentifierStart(value)) ReadIdentifier();
				else if (char.IsDigit(value) || value == '.' && IsDigitAt(_index + 1) ||
					value == '-' && _operand && (IsDigitAt(_index + 1) || IsDecimalAt(_index + 1)))
					ReadNumber();
				else if ("()[]{},.".IndexOf(value) >= 0)
				{
					Add(ExpressionTokenKind.Punctuation, start, ++_index);
					_operand = value != ')' && value != ']' && value != '}';
				}
				else ReadOperator();
			}
		}

		private bool IsDigitAt(int index) => index < _source.Length && char.IsDigit(_source[index]);
		private bool IsDecimalAt(int index) => index < _source.Length && _source[index] == '.' && IsDigitAt(index + 1);
		private int SkipWhitespace(int index)
		{
			while (index < _source.Length && char.IsWhiteSpace(_source[index])) ++index;
			return index;
		}

		private void Add(ExpressionTokenKind kind, int start, int end)
		{
			if (_maximumTokens.HasValue && _tokens.Count >= _maximumTokens.Value)
			{
				IsTruncated = true;
				_report("completion-work-limit-exceeded", "The expression exceeds the completion token budget.", start, end - start);
				return;
			}
			_tokens.Add(new ExpressionToken(kind, start, end - start));
		}

		private void ReadString()
		{
			var start = _index;
			_index = ExpressionLiteralCodec.ScanQuotedToken(_source, start, out var closed);
			Add(ExpressionTokenKind.Constant, start, _index);
			if (!closed) _report("unterminated-string", "Expected a closing quote.", _source.Length, 0);
			_operand = false;
		}

		private void ReadNumber()
		{
			var start = _index;
			if (_source[_index] == '-') ++_index;
			while (_index < _source.Length && (char.IsDigit(_source[_index]) || _source[_index] == '.')) ++_index;
			Add(ExpressionTokenKind.Constant, start, _index);
			if (!ExpressionGrammar.IsNumber(_source.Substring(start, _index - start)))
				_report("unexpected-token", "Invalid numeric literal syntax.", start, _index - start);
			_operand = false;
		}

		private void ReadIdentifier()
		{
			var start = _index;
			var variable = _source[_index] == VariableResolver.VariablePrefix;
			if (variable) ++_index;
			var nameStart = _index;
			while (_index < _source.Length && ExpressionGrammar.IsIdentifierPart(_source[_index])) ++_index;
			var word = _source.Substring(nameStart, _index - nameStart);
			if (!variable && !_operand && string.Equals(word, "not", StringComparison.OrdinalIgnoreCase))
			{
				var next = SkipWhitespace(_index);
				if (next > _index && next + 2 <= _source.Length &&
					string.Compare(_source, next, "in", 0, 2, StringComparison.OrdinalIgnoreCase) == 0 &&
					(next + 2 == _source.Length || !ExpressionGrammar.IsIdentifierPart(_source[next + 2])))
				{
					_index = next + 2;
					Add(ExpressionTokenKind.Operator, start, _index);
					_membership = true;
					_operand = true;
					return;
				}
				Add(ExpressionTokenKind.Operator, start, _index);
				_operand = true;
				return;
			}
			var plainEnd = _index;
			var following = SkipWhitespace(_index);
			var function = _tokens.Count > 0 && _tokens[_tokens.Count - 1].Kind == ExpressionTokenKind.Punctuation &&
				_source[_tokens[_tokens.Count - 1].Start] == '.' && ExpressionGrammar.IsFunction(word);
			var negation = string.Equals(word, "not", StringComparison.OrdinalIgnoreCase) &&
				following < _source.Length && _source[following] == '(';
			if (!variable && (function || negation || !_operand && (ExpressionGrammar.IsComparison(word) || ExpressionGrammar.IsLogical(word))))
			{
				Add(ExpressionTokenKind.Operator, start, _index);
				_membership = Interpreter.IsMembership(word.ToLowerInvariant());
				_operand = true;
				return;
			}
			var incomplete = _index == nameStart;
			while (SkipWhitespace(_index) < _source.Length && _source[SkipWhitespace(_index)] == '.')
			{
				var dot = SkipWhitespace(_index);
				var segment = SkipWhitespace(dot + 1);
				var end = segment;
				while (end < _source.Length && ExpressionGrammar.IsIdentifierPart(_source[end])) ++end;
				if (end > segment && SkipWhitespace(end) < _source.Length &&
					_source[SkipWhitespace(end)] == '(' && ExpressionGrammar.IsFunction(_source.Substring(segment, end - segment)))
					break;
				_index = end == segment ? dot + 1 : end;
				if (end == segment) { incomplete = true; break; }
			}
			Add(variable ? ExpressionTokenKind.Variable : ExpressionGrammar.IsConstant(word) && _index == plainEnd
				? ExpressionTokenKind.Constant : ExpressionTokenKind.Property, start, _index);
			if (incomplete)
			{
				var position = SkipWhitespace(_index);
				_report("incomplete-identifier", "Expected an identifier.", position,
					position == _source.Length ? 0 : 1);
			}
			_operand = false;
		}

		private void ReadOperator()
		{
			var start = _index;
			string match = null;
			foreach (var op in Interpreter.ComparisonOperators)
			{
				if (ExpressionGrammar.IsIdentifierStart(op[0])) continue;
				if (Matches(op) && (match == null || op.Length > match.Length)) match = op;
			}
			foreach (var op in ExpressionGrammar.LogicalOperators)
			{
				if (!ExpressionGrammar.IsIdentifierStart(op[0]) && Matches(op) && (match == null || op.Length > match.Length)) match = op;
			}
			if (match == null && (ExpressionGrammar.IsAdditive(_source[_index]) || _source[_index] == '!'))
				match = _source[_index].ToString();
			if (match == null)
			{
				Add(ExpressionTokenKind.Unknown, start, ++_index);
				_report("unknown-text", "Unrecognized expression character.", start, 1);
			}
			else
			{
				_index += match.Length;
				Add(ExpressionTokenKind.Operator, start, _index);
				_operand = true;
			}
		}

		private bool Matches(string value) => _index + value.Length <= _source.Length &&
			string.CompareOrdinal(_source, _index, value, 0, value.Length) == 0;

		private void ReadListItem()
		{
			var start = _index;
			var value = _source[_index];
			if (value == _listEnd || value == ',')
			{
				if (value == _listEnd) { _listEnd = '\0'; _operand = false; }
				Add(ExpressionTokenKind.Punctuation, start, ++_index);
				return;
			}
			if (ExpressionGrammar.IsQuote(value)) { ReadString(); return; }
			while (_index < _source.Length)
			{
				value = _source[_index];
				if (value == ExpressionGrammar.Escape && _index + 1 < _source.Length && _source[_index + 1] == _listEnd) { _index += 2; continue; }
				if (value == ',' || value == _listEnd || char.IsWhiteSpace(value)) break;
				++_index;
			}
			Add(_source[start] == VariableResolver.VariablePrefix ? ExpressionTokenKind.Variable : ExpressionTokenKind.Constant, start, _index);
		}
	}
}
