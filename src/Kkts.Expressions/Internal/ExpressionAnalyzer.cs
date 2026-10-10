using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal sealed class ExpressionAnalyzer
	{
		private const int MaximumPolicyDiagnostics = 32;
		private readonly string _source;
		private readonly bool _policyAware;
		private readonly List<ExpressionToken> _tokens = new List<ExpressionToken>();
		private readonly List<ExpressionSyntaxDiagnostic> _diagnostics = new List<ExpressionSyntaxDiagnostic>();
		private readonly HashSet<Tuple<string, int, int>> _reported = new HashSet<Tuple<string, int, int>>();
		private readonly HashSet<Tuple<int, int>> _errorSpans = new HashSet<Tuple<int, int>>();
		private bool _isTruncated;

		internal ExpressionAnalyzer(string source, bool policyAware = false)
		{
			_source = source;
			_policyAware = policyAware;
		}

		internal bool IsTruncated => _isTruncated;

		internal ExpressionAnalysisResult Analyze()
		{
			new ExpressionLexer(_source, _tokens, Report).ReadAll();
			if (_tokens.Count > 0) Validate();
			return new ExpressionAnalysisResult(_tokens,
				_diagnostics.OrderBy(d => d.Start).ThenBy(d => d.Length).ThenBy(d => d.Code, StringComparer.Ordinal),
				_tokens.Count > 0 && _diagnostics.Count == 0);
		}

		private void Report(string code, string message, int start, int length)
		{
			if (_isTruncated) return;
			if (_reported.Add(Tuple.Create(code, start, length)))
			{
				if (_policyAware && _diagnostics.Count >= MaximumPolicyDiagnostics)
				{
					_diagnostics.RemoveAt(MaximumPolicyDiagnostics - 1);
					_diagnostics.Add(new ExpressionSyntaxDiagnostic(
						"query-policy-diagnostics-truncated",
						"Additional diagnostics were omitted; fix the reported issues before retrying.",
						start,
						0));
					_isTruncated = true;
					return;
				}

				_diagnostics.Add(new ExpressionSyntaxDiagnostic(code, message, start, length));
				_errorSpans.Add(Tuple.Create(start, length));
			}
		}

		private void Validate()
		{
			var replay = ExpressionSyntaxReplay.Read(_source, _tokens, _source.Length, (tokenIndex, token) =>
			{
				if (token.Kind != ExpressionTokenKind.Unknown &&
					!_errorSpans.Contains(Tuple.Create(token.Start, token.Length)))
					Report("unexpected-token", "Unexpected expression token.", token.Start, token.Length);
			}, (tokenIndex, token) =>
				_tokens[tokenIndex] = new ExpressionToken(ExpressionTokenKind.Operator, token.Start, token.Length));

			if (replay.IsRecovering) return;
			var state = replay.Parser;
			if (!state.TryComplete(out var chain))
			{
				if (!_diagnostics.Any(d => d.Start == _source.Length))
					Report(state.HasOpenGroups || state.KeepsWhitespace ? "unmatched-delimiter" : "missing-operand",
						state.HasOpenGroups || state.KeepsWhitespace ? "Expected a closing delimiter." : "Expected an operand or completed expression.",
						_source.Length, 0);
				return;
			}
			ValidateChains(chain, state);
		}

		private void ValidateChains(List<Parser> chain, ExpressionSyntaxParser state)
		{
			var pending = new Stack<Tuple<List<Parser>, int>>();
			pending.Push(Tuple.Create(chain, _source.Length));
			while (pending.Count > 0)
			{
				var item = pending.Pop();
				var expectsOperand = true;
				var operandCount = 0;
				foreach (var parser in item.Item1)
				{
					if (parser.Body != null && (parser.Body.Count > 0 || !state.IsRecoveredScope(parser)))
						pending.Push(Tuple.Create(parser.Body, parser.EndIndex));
					if (parser is NotOperatorParser)
					{
						if (!expectsOperand) ReportAt(parser.StartIndex);
					}
					else if (parser is ComparisonFunctionOperatorParser)
					{
						if (expectsOperand) ReportAt(parser.StartIndex);
					}
					else if (parser is AdditiveOperatorParser || parser is ComparisonOparatorParser || parser is LogicalOperatorParser)
					{
						if (!ExpressionGrammar.HasBinaryOperands(operandCount, !expectsOperand)) ReportAt(parser.StartIndex);
						expectsOperand = true;
					}
					else
					{
						if (!expectsOperand) ReportAt(parser.StartIndex);
						expectsOperand = false;
						++operandCount;
					}
				}
				if (expectsOperand)
				{
					var position = item.Item2 < 0 ? _source.Length : item.Item2;
					Report("missing-operand", "Expected an operand.", position, position == _source.Length ? 0 : 1);
				}
			}
		}

		private void ReportAt(int position)
		{
			Report("unexpected-token", "Unexpected expression token.", position, 1);
		}
	}
}
