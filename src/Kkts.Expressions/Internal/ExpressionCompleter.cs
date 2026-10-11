using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal static class ExpressionCompleter
    {
        internal static ExpressionCompletionResult Complete(
            string source, int position, ExpressionSchema schema, ExpressionVariableSchema variables,
            ExpressionValueSuggestionSchema suggestions, ExpressionCompletionOptions options,
            ExpressionQueryContext queryContext = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            ExpressionCompletionCursor.Locate(source, position, Array.Empty<ExpressionToken>());
            suggestions?.ValidateSchema(schema);
            var execution = new QueryPolicyExecution(queryContext?.Policy ?? new QueryPolicy());
            if (!QueryPolicySourceScanner.TryScanCompletion(source, execution))
                return Limited(execution.Diagnostics.ToReadOnlyList());
            var tokens = new List<ExpressionToken>();
            var diagnostics = new List<ExpressionDiagnostic>();
            var lexer = new ExpressionLexer(source, tokens,
                (code, message, start, length) =>
                {
                    if (code == "completion-work-limit-exceeded")
                        diagnostics.Add(new ExpressionDiagnostic(ExpressionDiagnosticKind.Syntax, code, message, start, length));
                }, ExpressionCompletionBudget.MaximumTokens);
            lexer.ReadAll();
            if (lexer.IsTruncated) return Limited(diagnostics);
            var syntax = new ExpressionAnalysisResult(tokens, Array.Empty<ExpressionSyntaxDiagnostic>(), false);
            var bareConditions = new List<ExpressionToken>();
            if (execution.Policy.MaxAtomicConditions.HasValue &&
                !CountBareConditions(source, tokens, schema, variables, syntax, queryContext, execution, bareConditions))
                return Limited(execution.Diagnostics.ToReadOnlyList());
            var context = ExpressionCompletionContext.Recognize(source, position, schema, variables, syntax, queryContext);
            var accounting = new ExpressionCompletionPolicy(source, tokens, context, execution, bareConditions);
            var candidates = new ExpressionCompletionCandidates(source, schema, variables, syntax, context, queryContext, accounting);
            candidates.AddFields();
            candidates.AddOperators();
            candidates.AddValues(suggestions);
            candidates.AddVariables();
            candidates.AddConstructs();
            return candidates.Shape(options);
        }

        private static ExpressionCompletionResult Limited(IEnumerable<ExpressionDiagnostic> diagnostics) =>
            new ExpressionCompletionResult(Array.Empty<ExpressionCompletionItem>(), diagnostics,
                ExpressionCompletionStatus.LimitExceeded, true);

        private static bool CountBareConditions(string source, IReadOnlyList<ExpressionToken> tokens,
            ExpressionSchema schema, ExpressionVariableSchema variables, ExpressionAnalysisResult syntax,
            ExpressionQueryContext context, QueryPolicyExecution execution, List<ExpressionToken> bareConditions)
        {
            var binder = new ExpressionSemanticAnalyzer(source, schema, variables, syntax, context, completionMode: true);
            var inList = false;
            for (var index = 0; index < tokens.Count; ++index)
            {
                var token = tokens[index];
                var text = source.Substring(token.Start, token.Length);
                if (token.Kind == ExpressionTokenKind.Punctuation && ExpressionGrammar.IsListStart(text[0]) &&
                    index > 0 && Interpreter.IsMembership(
                        ExpressionGrammar.NormalizeOperator(Text(source, tokens[index - 1]))))
                {
                    inList = true;
                    continue;
                }
                if (inList)
                {
                    if (token.Kind == ExpressionTokenKind.Punctuation && (text == ")" || text == "]" || text == "}"))
                        inList = false;
                    continue;
                }
                if (token.Kind == ExpressionTokenKind.Punctuation || token.Kind == ExpressionTokenKind.Operator) continue;
                var previous = index - 1;
                while (previous >= 0 && (Text(source, tokens[previous]) == "(" || Text(source, tokens[previous]) == "!" ||
                    string.Equals(Text(source, tokens[previous]), "not", StringComparison.OrdinalIgnoreCase))) --previous;
                var next = index + 1;
                while (next < tokens.Count && Text(source, tokens[next]) == ")") ++next;
                if (previous >= 0 && !ExpressionGrammar.IsLogical(Text(source, tokens[previous])) ||
                    next < tokens.Count && !ExpressionGrammar.IsLogical(Text(source, tokens[next]))) continue;
                if (binder.TryDescribeToken(token, out var node) && node.IsBareBooleanPredicate)
                {
                    bareConditions.Add(token);
                    if (!execution.TryCountCondition(token.Start, token.Length)) return false;
                }
            }
            return true;
        }

        private static string Text(string source, ExpressionToken token) => source.Substring(token.Start, token.Length);
    }
}
