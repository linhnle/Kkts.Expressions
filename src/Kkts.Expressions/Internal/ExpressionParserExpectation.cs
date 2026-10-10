using System;

namespace Kkts.Expressions.Internal
{
    [Flags]
    internal enum ExpressionParserSlot
    {
        None = 0,
        Operand = 1,
        UnaryNot = 2,
        Group = 4,
        Comparison = 8,
        Logical = 16,
        Additive = 32,
        MembershipList = 64,
        Variable = 128,
        ComparisonFunction = 256,
        QuotedContent = 512,
        MembershipContent = 1024
    }

    internal sealed class ExpressionParserExpectation
    {
        internal ExpressionParserExpectation(
            ExpressionParserSlot current, ExpressionParserSlot continuation,
            bool hasCompleteToken, int tokenStart, int tokenLength, int openScopes, char closingDelimiter)
        {
            Current = current;
            Continuation = continuation;
            HasCompleteToken = hasCompleteToken;
            TokenStart = tokenStart;
            TokenLength = tokenLength;
            OpenScopes = openScopes;
            ClosingDelimiter = closingDelimiter;
        }

        internal ExpressionParserSlot Current { get; }
        internal ExpressionParserSlot Continuation { get; }
        internal bool HasCompleteToken { get; }
        internal int TokenStart { get; }
        internal int TokenLength { get; }
        internal int OpenScopes { get; }
        internal char ClosingDelimiter { get; }
    }
}
