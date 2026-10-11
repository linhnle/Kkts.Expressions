using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal sealed class ExpressionCompletionCursor
    {
        private ExpressionCompletionCursor(
            int position, int start, int length, int tokenStart, int tokenEnd,
            ExpressionTokenKind? kind, string prefix, string pathSuffix,
            bool atTokenEnd, char quote, bool closedQuote, bool reliablePrefix)
        {
            Position = position;
            Start = start;
            Length = length;
            TokenStart = tokenStart;
            TokenEnd = tokenEnd;
            Kind = kind;
            Prefix = prefix;
            PathSuffix = pathSuffix;
            IsAtTokenEnd = atTokenEnd;
            Quote = quote;
            IsClosedQuote = closedQuote;
            HasReliablePrefix = reliablePrefix;
        }

        internal int Position { get; }
        internal int Start { get; }
        internal int Length { get; }
        internal int TokenStart { get; }
        internal int TokenEnd { get; }
        internal ExpressionTokenKind? Kind { get; }
        internal string Prefix { get; }
        internal string PathSuffix { get; }
        internal bool IsAtTokenEnd { get; }
        internal char Quote { get; }
        internal bool IsClosedQuote { get; }
        internal bool HasReliablePrefix { get; }
        internal bool HasToken => Kind.HasValue;
        internal bool IsQuoted => Quote != '\0';

        internal ExpressionCompletionCursor AsInsertion() =>
            new ExpressionCompletionCursor(Position, Position, 0, TokenEnd, TokenEnd, null,
                string.Empty, string.Empty, false, '\0', false, true);

        internal static ExpressionCompletionCursor Locate(
            string source, int position, IReadOnlyList<ExpressionToken> tokens)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            if (position < 0 || position > source.Length ||
                position > 0 && position < source.Length &&
                char.IsHighSurrogate(source[position - 1]) && char.IsLowSurrogate(source[position]))
                throw new ArgumentOutOfRangeException(nameof(position));

            var selected = -1;
            var insertionIndex = tokens.Count;
            for (var index = 0; index < tokens.Count; ++index)
            {
                var token = tokens[index];
                if (token.Start > position)
                {
                    insertionIndex = index;
                    break;
                }
                if (token.Kind == ExpressionTokenKind.Punctuation &&
                    position >= token.Start && position < token.Start + token.Length)
                {
                    insertionIndex = index;
                    break;
                }
                if (token.Kind != ExpressionTokenKind.Punctuation &&
                    position >= token.Start && position <= token.Start + token.Length)
                {
                    selected = index;
                    if (position < token.Start + token.Length) break;
                }
                if (token.Start + token.Length <= position) insertionIndex = index + 1;
            }
            if (selected < 0)
                return new ExpressionCompletionCursor(position, position, 0, insertionIndex, insertionIndex,
                    null, string.Empty, string.Empty, false, '\0', false, true);

            var active = tokens[selected];
            var start = active.Start;
            var end = active.Start + active.Length;
            var tokenStart = selected;
            var kind = active.Kind;
            if (selected > 0 && (kind == ExpressionTokenKind.Property || kind == ExpressionTokenKind.Operator) &&
                tokens[selected - 1].Kind == ExpressionTokenKind.Operator)
            {
                var previous = tokens[selected - 1];
                if (string.Equals(source.Substring(previous.Start, previous.Length), "not", StringComparison.OrdinalIgnoreCase) &&
                    IsWhitespaceGap(source, previous.Start + previous.Length, start))
                {
                    start = previous.Start;
                    tokenStart = selected - 1;
                    kind = ExpressionTokenKind.Operator;
                }
            }
            var quote = kind == ExpressionTokenKind.Constant && ExpressionGrammar.IsQuote(source[start])
                ? source[start] : '\0';
            var prefix = source.Substring(start, position - start);
            var suffix = string.Empty;
            var closed = false;
            var reliable = true;
            if (quote != '\0')
            {
                ExpressionLiteralCodec.ScanQuotedToken(source, start, out closed);
                var contentEnd = closed && position == end ? position - 1 : position;
                var contentStart = Math.Min(start + 1, contentEnd);
                reliable = !(position > start + 1 && position < end &&
                    source[position - 1] == ExpressionGrammar.Escape && source[position] == quote);
                prefix = ExpressionLiteralCodec.DecodeString(
                    source.Substring(contentStart, contentEnd - contentStart), quote);
            }
            else if (kind == ExpressionTokenKind.Property || kind == ExpressionTokenKind.Variable)
            {
                var nextDot = source.IndexOf('.', position, end - position);
                if (nextDot >= 0) suffix = ExpressionSchema.NormalizePath(source.Substring(nextDot, end - nextDot));
                prefix = ExpressionSchema.NormalizePath(prefix);
            }
            else if (kind == ExpressionTokenKind.Operator)
                prefix = string.Join(" ", prefix.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

            return new ExpressionCompletionCursor(position, start, end - start, tokenStart, selected + 1,
                kind, prefix, suffix, position == end, quote, closed, reliable);
        }

        private static bool IsWhitespaceGap(string source, int start, int end)
        {
            if (start >= end) return false;
            for (var index = start; index < end; ++index)
                if (!char.IsWhiteSpace(source[index])) return false;
            return true;
        }
    }
}
