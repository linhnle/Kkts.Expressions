using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal enum ExpressionCompletionSlot
    {
        Unavailable,
        Condition,
        Operand,
        Operator,
        FunctionOperator,
        Continuation,
        MembershipOperand,
        MembershipItem,
        MembershipSeparator
    }

    internal sealed class ExpressionCompletionContext
    {
        private ExpressionCompletionContext(
            ExpressionCompletionCursor edit, ExpressionParserExpectation expectation,
            ExpressionCompletionSlot slot, ExpressionSemanticAnalyzer.SemanticNode receiver,
            string op, ExpressionSemanticAnalyzer.SemanticNode suffixOperand, string suffixOperator,
            string candidatePrefix = null, string insertionPrefix = null, bool allowsPathFields = false)
        {
            Edit = edit;
            Expectation = expectation;
            Slot = slot;
            Receiver = receiver;
            Operator = op;
            SuffixOperand = suffixOperand;
            SuffixOperator = suffixOperator;
            CandidatePrefix = candidatePrefix ?? edit.Prefix;
            InsertionPrefix = insertionPrefix ?? string.Empty;
            AllowsPathFields = allowsPathFields;
        }

        internal ExpressionCompletionCursor Edit { get; }
        internal ExpressionParserExpectation Expectation { get; }
        internal ExpressionCompletionSlot Slot { get; }
        internal ExpressionSemanticAnalyzer.SemanticNode Receiver { get; }
        internal string Operator { get; }
        internal ExpressionSemanticAnalyzer.SemanticNode SuffixOperand { get; }
        internal string SuffixOperator { get; }
        internal string CandidatePrefix { get; }
        internal string InsertionPrefix { get; }
        internal bool AllowsPathFields { get; }
        internal bool IsAvailable => Slot != ExpressionCompletionSlot.Unavailable;

        internal static ExpressionCompletionContext Recognize(
            string source, int position, ExpressionSchema schema, ExpressionVariableSchema variables,
            ExpressionAnalysisResult syntax, ExpressionQueryContext queryContext = null)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (syntax == null) throw new ArgumentNullException(nameof(syntax));
            var tokens = syntax.Tokens;
            var edit = ExpressionCompletionCursor.Locate(source, position, tokens);
            var binder = new ExpressionSemanticAnalyzer(source, schema, variables, syntax,
                queryContext, completionMode: true);

            if (edit.IsAtTokenEnd && edit.HasReliablePrefix &&
                (!edit.IsQuoted || edit.IsClosedQuote))
            {
                var complete = edit.Kind == ExpressionTokenKind.Operator
                    ? !(edit.TokenStart > 0 && Text(source, tokens[edit.TokenStart - 1]) == ".") &&
                        IsComparison(Text(source, tokens[edit.TokenStart])) ||
                        ExpressionGrammar.IsLogical(Text(source, tokens[edit.TokenStart]))
                    : binder.TryDescribeOperand(edit.TokenStart, edit.TokenEnd, out _);
                if (complete) edit = edit.AsInsertion();
            }

            var replay = ExpressionSyntaxReplay.Read(source, tokens, edit.Start);
            var expectation = replay.Parser.Observe();
            if (replay.IsRecovering || !edit.HasReliablePrefix)
                return Unavailable(edit, expectation);

            if (expectation.Current.HasFlag(ExpressionParserSlot.MembershipContent))
            {
                var membership = FindMembershipOperator(source, tokens, edit.TokenStart);
                if (membership < 0 || !TryLeftOperand(binder, source, tokens, membership, out var receiver))
                    return Unavailable(edit, expectation);
                var itemComplete = !edit.HasToken && edit.TokenStart > membership + 2 &&
                    tokens[edit.TokenStart - 1].Kind != ExpressionTokenKind.Punctuation;
                return new ExpressionCompletionContext(edit, expectation,
                    itemComplete ? ExpressionCompletionSlot.MembershipSeparator : ExpressionCompletionSlot.MembershipItem,
                    receiver, Text(source, tokens[membership]), null, null);
            }

            if ((edit.Kind == ExpressionTokenKind.Property || edit.Kind == ExpressionTokenKind.Variable) &&
                edit.PathSuffix.Length == 0 && TryFunctionPath(source, edit, binder,
                    out var functionReceiver, out var functionPrefix, out var insertionPrefix))
            {
                if (!ReadSuffixOperand(source, tokens, edit.TokenEnd, binder, out var functionOperand))
                    return Unavailable(edit, expectation);
                if (edit.TokenStart > 0 && tokens[edit.TokenStart - 1].Kind == ExpressionTokenKind.Operator)
                {
                    var outerOperator = Text(source, tokens[edit.TokenStart - 1]);
                    if (!ExpressionGrammar.IsLogical(outerOperator) &&
                        (!TryOperand(binder, source, tokens, edit.TokenStart - 1, out var outerReceiver) ||
                            !binder.TryDescribeBinary(outerOperator, outerReceiver,
                                ExpressionSemanticAnalyzer.SemanticNode.Value(typeof(bool),
                                    ExpressionNullability.NonNullable, edit.Start, edit.Start + edit.Length), out _)))
                        return Unavailable(edit, expectation);
                }
                return new ExpressionCompletionContext(edit, expectation, ExpressionCompletionSlot.FunctionOperator,
                    functionReceiver, null, functionOperand, null, functionPrefix, insertionPrefix,
                    allowsPathFields: !schema.IsPublicSchema && functionPrefix.Length == 0);
            }

            var prefixEnd = edit.TokenStart;
            if (prefixEnd > 1 && Text(source, tokens[prefixEnd - 1]) == "(")
            {
                var openIndex = prefixEnd - 1;
                while (openIndex > 0 && Text(source, tokens[openIndex - 1]) == "(") --openIndex;
                var opIndex = openIndex - 1;
                if (opIndex < 0) return OperandStart(edit, expectation, source, tokens, binder);
                var op = Text(source, tokens[opIndex]);
                if (tokens[opIndex].Kind == ExpressionTokenKind.Operator &&
                    (IsComparison(op) || ExpressionGrammar.IsFunction(op) ||
                        op.Length == 1 && ExpressionGrammar.IsAdditive(op[0])))
                {
                    var receiverEnd = ExpressionGrammar.IsFunction(op) &&
                        opIndex > 0 && Text(source, tokens[opIndex - 1]) == "." ? opIndex - 1 : opIndex;
                    if (!TryOperand(binder, source, tokens, receiverEnd, out var receiver))
                        return Unavailable(edit, expectation);
                    if (!ReadSuffix(source, tokens, edit.TokenEnd, binder, out var suffixOp, out var suffixOperand))
                        return Unavailable(edit, expectation);
                    return new ExpressionCompletionContext(edit, expectation, ExpressionCompletionSlot.Operand,
                        receiver, op, suffixOperand, suffixOp);
                }
            }
            if (prefixEnd > 0 && Text(source, tokens[prefixEnd - 1]) == ".")
            {
                if (!TryOperand(binder, source, tokens, prefixEnd - 1, out var receiver))
                    return Unavailable(edit, expectation);
                if (!ReadSuffixOperand(source, tokens, edit.TokenEnd, binder, out var suffixOperand))
                    return Unavailable(edit, expectation);
                return new ExpressionCompletionContext(edit, expectation,
                    ExpressionCompletionSlot.FunctionOperator, receiver, null, suffixOperand, null);
            }

            if (prefixEnd > 0 && tokens[prefixEnd - 1].Kind == ExpressionTokenKind.Operator)
            {
                var operatorIndex = prefixEnd - 1;
                var op = Text(source, tokens[operatorIndex]);
                if (IsComparison(op) || op.Length == 1 && ExpressionGrammar.IsAdditive(op[0]))
                {
                    if (!TryLeftOperand(binder, source, tokens, operatorIndex, out var receiver))
                        return Unavailable(edit, expectation);
                    if (!ReadSuffix(source, tokens, edit.TokenEnd, binder, out var suffixOp, out var suffixOperand))
                        return Unavailable(edit, expectation);
                    return new ExpressionCompletionContext(edit, expectation,
                        Interpreter.IsMembership(ExpressionGrammar.NormalizeOperator(op))
                            ? ExpressionCompletionSlot.MembershipOperand : ExpressionCompletionSlot.Operand,
                        receiver, op, suffixOperand, suffixOp);
                }
                if (ExpressionGrammar.IsLogical(op))
                    return OperandStart(edit, expectation, source, tokens, binder);
            }

            if (edit.HasToken)
            {
                if (edit.Kind == ExpressionTokenKind.Operator ||
                    edit.Kind == ExpressionTokenKind.Property && expectation.HasCompleteToken &&
                    expectation.Continuation.HasFlag(ExpressionParserSlot.Comparison) &&
                    IsComparisonPrefix(edit.Prefix))
                {
                    if (!TryOperand(binder, source, tokens, prefixEnd, out var receiver))
                        return Unavailable(edit, expectation);
                    var membership = Interpreter.IsMembership(ExpressionGrammar.NormalizeOperator(
                        source.Substring(edit.Start, edit.Length)));
                    if (!ReadSuffixOperand(source, tokens, edit.TokenEnd, binder, out var suffixOperand,
                        membershipOperand: membership))
                        return Unavailable(edit, expectation);
                    return new ExpressionCompletionContext(edit, expectation, ExpressionCompletionSlot.Operator,
                        receiver, null, suffixOperand, null);
                }
                if (edit.IsQuoted) return Unavailable(edit, expectation);
                return OperandStart(edit, expectation, source, tokens, binder);
            }

            if (expectation.Current.HasFlag(ExpressionParserSlot.UnaryNot) ||
                !expectation.HasCompleteToken)
                return OperandStart(edit, expectation, source, tokens, binder);

            if (!TryOperand(binder, source, tokens, prefixEnd, out var completeOperand))
                return Unavailable(edit, expectation);
            if (!ReadSuffixOperand(source, tokens, edit.TokenEnd, binder, out var followingOperand))
                return Unavailable(edit, expectation);
            return new ExpressionCompletionContext(edit, expectation,
                completeOperand.Type == typeof(bool) && !completeOperand.IsBareBooleanPredicate
                    ? ExpressionCompletionSlot.Continuation : ExpressionCompletionSlot.Operator,
                completeOperand, null, followingOperand, null);
        }

        private static ExpressionCompletionContext OperandStart(
            ExpressionCompletionCursor edit, ExpressionParserExpectation expectation,
            string source, IReadOnlyList<ExpressionToken> tokens, ExpressionSemanticAnalyzer binder)
        {
            if (expectation.HasCompleteToken &&
                !expectation.Continuation.HasFlag(ExpressionParserSlot.Operand) &&
                !expectation.Current.HasFlag(ExpressionParserSlot.UnaryNot))
                return Unavailable(edit, expectation);
            if (!ReadSuffix(source, tokens, edit.TokenEnd, binder, out var suffixOp, out var suffixOperand))
                return Unavailable(edit, expectation);
            return new ExpressionCompletionContext(edit, expectation, ExpressionCompletionSlot.Condition,
                null, null, suffixOperand, suffixOp);
        }

        private static bool ReadSuffix(
            string source, IReadOnlyList<ExpressionToken> tokens, int start, ExpressionSemanticAnalyzer binder,
            out string op, out ExpressionSemanticAnalyzer.SemanticNode operand)
        {
            op = null;
            operand = null;
            if (start >= tokens.Count) return true;
            var token = tokens[start];
            var text = Text(source, token);
            if (token.Kind == ExpressionTokenKind.Operator &&
                !ExpressionGrammar.IsLogical(text) && ExpressionGrammar.GetBinaryPrecedence(text) >= 0)
            {
                op = text;
                return ReadSuffixOperand(source, tokens, start + 1, binder, out operand);
            }
            return IsScopeBoundary(token, text);
        }

        private static bool ReadSuffixOperand(
            string source, IReadOnlyList<ExpressionToken> tokens, int start, ExpressionSemanticAnalyzer binder,
            out ExpressionSemanticAnalyzer.SemanticNode operand, bool membershipOperand = false)
        {
            operand = null;
            if (start >= tokens.Count) return true;
            if (IsScopeBoundary(tokens[start], Text(source, tokens[start]))) return true;
            var depth = 0;
            var end = start;
            while (end < tokens.Count)
            {
                var token = tokens[end];
                var text = Text(source, token);
                if (depth == 0 && IsScopeBoundary(token, text)) break;
                if (token.Kind == ExpressionTokenKind.Punctuation)
                {
                    if (text == "(" || text == "[" || text == "{") ++depth;
                    else if (text == ")" || text == "]" || text == "}") --depth;
                }
                ++end;
            }
            if (depth != 0) return false;
            var list = tokens[start].Kind == ExpressionTokenKind.Punctuation &&
                (Text(source, tokens[start]) == "[" || Text(source, tokens[start]) == "{" ||
                    membershipOperand && Text(source, tokens[start]) == "(");
            if (binder.TryDescribeOperand(start, end, out operand, membershipOperand: list)) return true;
            return !list && Text(source, tokens[start]) == "(" &&
                binder.TryDescribeOperand(start, end, out operand, membershipOperand: true);
        }

        private static bool TryLeftOperand(
            ExpressionSemanticAnalyzer binder, string source, IReadOnlyList<ExpressionToken> tokens, int end,
            out ExpressionSemanticAnalyzer.SemanticNode operand) =>
            TryOperand(binder, source, tokens, end, out operand);

        private static bool TryOperand(
            ExpressionSemanticAnalyzer binder, string source, IReadOnlyList<ExpressionToken> tokens, int end,
            out ExpressionSemanticAnalyzer.SemanticNode operand)
        {
            var start = end;
            var depth = 0;
            while (start > 0)
            {
                var token = tokens[start - 1];
                var text = Text(source, token);
                if (token.Kind == ExpressionTokenKind.Punctuation)
                {
                    if (text == ")" || text == "]" || text == "}") ++depth;
                    else if (text == "(" || text == "[" || text == "{")
                    {
                        if (depth == 0) break;
                        --depth;
                    }
                }
                if (depth == 0 && token.Kind == ExpressionTokenKind.Operator && ExpressionGrammar.IsLogical(text))
                    break;
                --start;
            }
            return binder.TryDescribeOperand(start, end, out operand);
        }

        private static int FindMembershipOperator(string source, IReadOnlyList<ExpressionToken> tokens, int end)
        {
            for (var index = end - 1; index >= 0; --index)
            {
                var token = tokens[index];
                if (token.Kind == ExpressionTokenKind.Operator &&
                    Interpreter.IsMembership(ExpressionGrammar.NormalizeOperator(Text(source, token))))
                    return index;
            }
            return -1;
        }

        private static bool TryFunctionPath(
            string source, ExpressionCompletionCursor edit, ExpressionSemanticAnalyzer binder,
            out ExpressionSemanticAnalyzer.SemanticNode receiver, out string prefix, out string insertionPrefix)
        {
            receiver = null;
            prefix = null;
            insertionPrefix = null;
            if (edit.Position <= edit.Start) return false;
            var dot = source.LastIndexOf('.', edit.Position - 1, edit.Position - edit.Start);
            if (dot < 0) return false;
            var root = ExpressionSchema.NormalizePath(source.Substring(edit.Start, dot - edit.Start));
            if (root.IndexOf('.') >= 0) return false;
            prefix = ExpressionSchema.NormalizePath(source.Substring(dot + 1, edit.Position - dot - 1));
            var functionPrefix = false;
            foreach (var function in Interpreter.ComparisonFunctionOperators)
                if (function.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    functionPrefix = true;
                    break;
                }
            if (!functionPrefix ||
                !binder.TryDescribeToken(new ExpressionToken(edit.Kind.Value, edit.Start, dot - edit.Start),
                    out receiver) || receiver.Type != typeof(string))
                return false;
            insertionPrefix = root + ".";
            return true;
        }

        private static bool IsScopeBoundary(ExpressionToken token, string text) =>
            token.Kind == ExpressionTokenKind.Operator && ExpressionGrammar.IsLogical(text) ||
            token.Kind == ExpressionTokenKind.Punctuation && (text == ")" || text == "]" || text == "}" || text == ",");

        private static bool IsComparisonPrefix(string prefix)
        {
            foreach (var op in Interpreter.ComparisonOperators)
                if (op.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsComparison(string op) =>
            ExpressionGrammar.IsComparison(ExpressionGrammar.NormalizeOperator(op));

        private static string Text(string source, ExpressionToken token) =>
            source.Substring(token.Start, token.Length);

        private static ExpressionCompletionContext Unavailable(
            ExpressionCompletionCursor edit, ExpressionParserExpectation expectation) =>
            new ExpressionCompletionContext(edit, expectation, ExpressionCompletionSlot.Unavailable,
                null, null, null, null);
    }
}
