using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal sealed class QueryPolicySourceScanner
    {
        private readonly string _source;
        private readonly QueryPolicyExecution _execution;
        private long _parenthesisDepth;
        private bool _expectsOperand = true;
        private bool _membershipPending;
        private bool _lastTokenWasDot;
        private char _listEnd;
        private QueryPolicyCounter _membershipItems;
        private readonly Stack<char> _completionScopes;

        private QueryPolicySourceScanner(string source, QueryPolicyExecution execution, bool completion = false)
        {
            _source = source;
            _execution = execution;
            if (completion) _completionScopes = new Stack<char>();
        }

        internal static bool TryScanCompletion(string source, QueryPolicyExecution execution)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            if (!execution.TryAdmitExpression(source)) return false;
            if (source.Length > ExpressionCompletionBudget.MaximumSourceLength)
            {
                execution.Diagnostics.Add(
                    "completion-work-limit-exceeded",
                    "The expression exceeds the completion UTF-16 source budget.",
                    ExpressionCompletionBudget.MaximumSourceLength,
                    source.Length - ExpressionCompletionBudget.MaximumSourceLength,
                    ExpressionCompletionBudget.MaximumSourceLength,
                    source.Length);
                return false;
            }
            return new QueryPolicySourceScanner(source, execution, completion: true).Scan();
        }

        internal static bool TryScan(string source, QueryPolicyExecution execution)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            if (!execution.TryAdmitExpression(source)) return false;
            return new QueryPolicySourceScanner(source, execution).Scan();
        }

        internal static bool TryCountMembershipFragment(
            string fragment,
            QueryPolicyExecution execution,
            string inputPath)
        {
            if (fragment == null) throw new ArgumentNullException(nameof(fragment));
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            if (!execution.Policy.MaxInItems.HasValue) return true;

            var counter = execution.CreateMembershipCounter();
            var active = false;
            var escaped = false;
            var quote = '\0';
            for (var index = 0; index < fragment.Length; ++index)
            {
                var value = fragment[index];
                if (!active)
                {
                    if (char.IsWhiteSpace(value) || value == ',') continue;
                    active = true;
                    if (!execution.TryCountMembershipItem(counter, 0, 0, inputPath)) return false;
                    if (ExpressionGrammar.IsQuote(value))
                    {
                        quote = value;
                        continue;
                    }
                }

                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (value == ExpressionGrammar.Escape)
                {
                    escaped = true;
                    continue;
                }
                if (quote != '\0')
                {
                    if (value == quote) quote = '\0';
                    continue;
                }
                if (ExpressionGrammar.IsQuote(value))
                {
                    quote = value;
                    continue;
                }
                if (value == ',') active = false;
            }
            return true;
        }

        private bool Scan()
        {
            var index = 0;
            while (index < _source.Length)
            {
                var value = _source[index];
                if (char.IsWhiteSpace(value))
                {
                    ++index;
                    continue;
                }

                if (ExpressionGrammar.IsQuote(value))
                {
                    if (_listEnd != '\0' && !_execution.TryCountMembershipItem(
                            _membershipItems,
                            index,
                            GetListItemLength(index),
                            inputPath: null))
                        return false;

                    index = ReadQuoted(index);
                    _expectsOperand = false;
                    _membershipPending = false;
                    _lastTokenWasDot = false;
                    continue;
                }

                if (_listEnd != '\0')
                {
                    if (value == _listEnd)
                    {
                        CloseCompletionScope(value);
                        if (value == ')') DecreaseParenthesisDepth();
                        _listEnd = '\0';
                        _membershipItems = null;
                        _expectsOperand = false;
                        ++index;
                        continue;
                    }
                    if (value == ',')
                    {
                        ++index;
                        continue;
                    }

                    var start = index;
                    var length = GetListItemLength(index);
                    if (!_execution.TryCountMembershipItem(_membershipItems, start, length))
                        return false;
                    index += length;
                    _expectsOperand = false;
                    continue;
                }

                if (_membershipPending && ExpressionGrammar.IsListStart(value))
                {
                    if (!OpenCompletionScope(value, index)) return false;
                    _membershipPending = false;
                    _listEnd = ExpressionGrammar.ListEnd(value);
                    _membershipItems = _execution.CreateMembershipCounter();
                    if (value == '(' && !IncreaseParenthesisDepth(index)) return false;
                    _expectsOperand = true;
                    _lastTokenWasDot = false;
                    ++index;
                    continue;
                }

                _membershipPending = false;

                if (char.IsDigit(value) ||
                    value == '.' && index + 1 < _source.Length && char.IsDigit(_source[index + 1]) ||
                    value == '-' && _expectsOperand && index + 1 < _source.Length &&
                    (char.IsDigit(_source[index + 1]) ||
                     _source[index + 1] == '.' && index + 2 < _source.Length && char.IsDigit(_source[index + 2])))
                {
                    if (value == '-') ++index;
                    while (index < _source.Length &&
                           (char.IsDigit(_source[index]) || _source[index] == '.'))
                        ++index;
                    _expectsOperand = false;
                    _lastTokenWasDot = false;
                    continue;
                }

                if (ExpressionGrammar.IsIdentifierStart(value) || value == VariableResolver.VariablePrefix)
                {
                    var start = index;
                    if (value == VariableResolver.VariablePrefix) ++index;
                    var wordStart = index;
                    while (index < _source.Length && ExpressionGrammar.IsIdentifierPart(_source[index]))
                        ++index;
                    if (wordStart == index)
                    {
                        _expectsOperand = false;
                        _lastTokenWasDot = false;
                        continue;
                    }

                    var wordLength = index - wordStart;
                    if (MatchesWord(wordStart, "not", out _) && !_expectsOperand)
                    {
                        var next = SkipWhitespace(index);
                        if (next > index && MatchesWord(next, "in", out var afterIn))
                        {
                            if (!_execution.TryCountCondition(start, afterIn - start)) return false;
                            _membershipPending = true;
                            _expectsOperand = true;
                            _lastTokenWasDot = false;
                            index = afterIn;
                            continue;
                        }

                        _expectsOperand = true;
                        _lastTokenWasDot = false;
                        continue;
                    }

                    var isComparison = MatchesAny(wordStart, wordLength, Interpreter.ComparisonOperators);
                    var isFunction = _lastTokenWasDot &&
                        MatchesAny(wordStart, wordLength, Interpreter.ComparisonFunctionOperators);
                    if (!_expectsOperand && (isComparison || isFunction))
                    {
                        if (!_execution.TryCountCondition(start, index - start)) return false;
                        _membershipPending = MatchesWord(wordStart, "in", out _);
                        _expectsOperand = true;
                    }
                    else if (!_expectsOperand && MatchesAny(wordStart, wordLength, ExpressionGrammar.LogicalOperators))
                    {
                        _expectsOperand = true;
                    }
                    else if (MatchesWord(wordStart, "not", out _) &&
                             SkipWhitespace(index) < _source.Length &&
                             _source[SkipWhitespace(index)] == '(')
                    {
                        _expectsOperand = true;
                    }
                    else
                    {
                        _expectsOperand = false;
                    }

                    _lastTokenWasDot = false;
                    continue;
                }

                if (value == '(')
                {
                    if (!OpenCompletionScope(value, index)) return false;
                    if (!IncreaseParenthesisDepth(index)) return false;
                    _expectsOperand = true;
                    _lastTokenWasDot = false;
                    ++index;
                    continue;
                }

                if (value == ')')
                {
                    CloseCompletionScope(value);
                    DecreaseParenthesisDepth();
                    _expectsOperand = false;
                    _lastTokenWasDot = false;
                    ++index;
                    continue;
                }

                if (value == '.')
                {
                    _lastTokenWasDot = true;
                    ++index;
                    continue;
                }

                if (TryReadSymbolicOperator(index, out var operatorText))
                {
                    if (ExpressionGrammar.IsComparison(operatorText))
                    {
                        if (!_execution.TryCountCondition(index, operatorText.Length)) return false;
                        _membershipPending = Interpreter.IsMembership(operatorText);
                    }
                    else if (ExpressionGrammar.IsLogical(operatorText))
                    {
                        _membershipPending = false;
                    }

                    _expectsOperand = true;
                    _lastTokenWasDot = false;
                    index += operatorText.Length;
                    continue;
                }

                if (value == ',' || value == '[' || value == ']' || value == '{' || value == '}')
                {
                    if (value == '[' || value == '{')
                    {
                        if (!OpenCompletionScope(value, index)) return false;
                    }
                    else if (value == ']' || value == '}')
                    {
                        CloseCompletionScope(value);
                    }
                    _expectsOperand = value == ',' || value == '[' || value == '{';
                    _lastTokenWasDot = false;
                    ++index;
                    continue;
                }

                _lastTokenWasDot = false;
                _expectsOperand = true;
                ++index;
            }

            return true;
        }

        private bool OpenCompletionScope(char opening, int start)
        {
            if (_completionScopes == null) return true;
            if (_completionScopes.Count >= ExpressionCompletionBudget.MaximumScopes)
            {
                _execution.Diagnostics.Add(
                    "completion-work-limit-exceeded",
                    "The expression exceeds the completion open-scope budget.",
                    start, 1, ExpressionCompletionBudget.MaximumScopes,
                    ExpressionCompletionBudget.MaximumScopes + 1);
                return false;
            }
            _completionScopes.Push(ExpressionGrammar.ListEnd(opening));
            return true;
        }

        private void CloseCompletionScope(char closing)
        {
            if (_completionScopes != null && _completionScopes.Count > 0 &&
                _completionScopes.Peek() == closing)
                _completionScopes.Pop();
        }

        private bool IncreaseParenthesisDepth(int index)
        {
            ++_parenthesisDepth;
            if (!_execution.Policy.MaxParenthesisDepth.HasValue ||
                _parenthesisDepth <= _execution.Policy.MaxParenthesisDepth.Value)
                return true;

            _execution.Diagnostics.Add(
                "query-policy-parenthesis-depth-exceeded",
                "The expression exceeds the configured parenthesis depth.",
                index,
                1,
                _execution.Policy.MaxParenthesisDepth.Value,
                _parenthesisDepth);
            return false;
        }

        private void DecreaseParenthesisDepth()
        {
            if (_parenthesisDepth > 0) --_parenthesisDepth;
        }

        private int ReadQuoted(int start)
        {
            var quote = _source[start];
            var index = start + 1;
            while (index < _source.Length)
            {
                var value = _source[index++];
                if (value == ExpressionGrammar.Escape &&
                    index < _source.Length &&
                    _source[index] == quote)
                {
                    ++index;
                    continue;
                }
                if (value == quote) break;
            }
            return index;
        }

        private int GetListItemLength(int start)
        {
            if (ExpressionGrammar.IsQuote(_source[start]))
                return ReadQuoted(start) - start;

            var index = start;
            while (index < _source.Length &&
                   _source[index] != ',' &&
                   _source[index] != _listEnd &&
                   !char.IsWhiteSpace(_source[index]))
                ++index;
            return Math.Max(1, index - start);
        }

        private int SkipWhitespace(int index)
        {
            while (index < _source.Length && char.IsWhiteSpace(_source[index])) ++index;
            return index;
        }

        private bool MatchesWord(int index, string word, out int end)
        {
            end = index;
            if (index + word.Length > _source.Length ||
                string.Compare(_source, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            end = index + word.Length;
            return end == _source.Length || !ExpressionGrammar.IsIdentifierPart(_source[end]);
        }

        private bool MatchesAny(int start, int length, IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Length == length &&
                    string.Compare(_source, start, candidate, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }

        private bool TryReadSymbolicOperator(int index, out string match)
        {
            match = null;
            foreach (var candidate in Interpreter.ComparisonOperators)
            {
                if (ExpressionGrammar.IsIdentifierStart(candidate[0]) ||
                    index + candidate.Length > _source.Length ||
                    string.CompareOrdinal(_source, index, candidate, 0, candidate.Length) != 0)
                    continue;
                if (match == null || candidate.Length > match.Length) match = candidate;
            }

            foreach (var candidate in ExpressionGrammar.LogicalOperators)
            {
                if (ExpressionGrammar.IsIdentifierStart(candidate[0]) ||
                    index + candidate.Length > _source.Length ||
                    string.CompareOrdinal(_source, index, candidate, 0, candidate.Length) != 0)
                    continue;
                if (match == null || candidate.Length > match.Length) match = candidate;
            }
            return match != null;
        }
    }
}
