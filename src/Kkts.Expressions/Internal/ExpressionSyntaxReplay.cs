using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal sealed class ExpressionSyntaxReplay
    {
        private ExpressionSyntaxReplay(ExpressionSyntaxParser parser, bool recovering, int recoveryTokenEnd)
        {
            Parser = parser;
            IsRecovering = recovering;
            RecoveryTokenEnd = recoveryTokenEnd;
        }

        internal ExpressionSyntaxParser Parser { get; }
        internal bool IsRecovering { get; }
        internal int RecoveryTokenEnd { get; }

        internal static ExpressionSyntaxReplay Read(
            string source, IReadOnlyList<ExpressionToken> tokens, int sourceEnd,
            Action<int, ExpressionToken> unexpectedToken = null,
            Action<int, ExpressionToken> recoveredLogical = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (sourceEnd < 0 || sourceEnd > source.Length) throw new ArgumentOutOfRangeException(nameof(sourceEnd));

            var state = new ExpressionSyntaxParser(syntaxOnly: true);
            var cursor = 0;
            var whitespace = 0;
            var recovering = false;
            var recoveryTokenEnd = 0;
            var lastContent = sourceEnd - 1;
            while (lastContent >= 0 && char.IsWhiteSpace(source[lastContent])) --lastContent;
            for (var tokenIndex = 0; tokenIndex < tokens.Count; ++tokenIndex)
            {
                var token = tokens[tokenIndex];
                if (token.Start >= sourceEnd) break;
                var end = Math.Min(sourceEnd, token.Start + token.Length);
                var text = source.Substring(token.Start, end - token.Start);
                if (recovering)
                {
                    if (end == token.Start + token.Length &&
                        (token.Kind == ExpressionTokenKind.Operator || token.Kind == ExpressionTokenKind.Property) &&
                        ExpressionGrammar.IsLogical(text))
                    {
                        if (token.Kind != ExpressionTokenKind.Operator) recoveredLogical?.Invoke(tokenIndex, token);
                        state.Recover();
                        recovering = false;
                        recoveryTokenEnd = tokenIndex + 1;
                        cursor = end;
                        whitespace = 0;
                        continue;
                    }
                    if (token.Kind != ExpressionTokenKind.Punctuation || text != ")" || !state.HasOpenGroups)
                        continue;
                    state.Recover();
                    recovering = false;
                    recoveryTokenEnd = tokenIndex;
                    cursor = token.Start;
                    whitespace = 0;
                }

                while (cursor < end)
                {
                    var value = source[cursor];
                    if (!state.KeepsWhitespace && char.IsWhiteSpace(value))
                    {
                        ++whitespace;
                        ++cursor;
                        continue;
                    }
                    if (!state.TryRead(value, whitespace, cursor, cursor < lastContent))
                    {
                        unexpectedToken?.Invoke(tokenIndex, token);
                        recovering = true;
                        cursor = end;
                        break;
                    }
                    whitespace = 0;
                    ++cursor;
                }
            }
            return new ExpressionSyntaxReplay(state, recovering, recoveryTokenEnd);
        }
    }
}
