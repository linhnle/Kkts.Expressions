using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionContextTest
    {
        [Theory]
        [InlineData("", ExpressionParserSlot.Operand | ExpressionParserSlot.UnaryNot | ExpressionParserSlot.Group)]
        [InlineData("   ", ExpressionParserSlot.Operand | ExpressionParserSlot.UnaryNot | ExpressionParserSlot.Group)]
        [InlineData("Price >", ExpressionParserSlot.Comparison)]
        [InlineData("Price not i", ExpressionParserSlot.Comparison)]
        [InlineData("Price > 1 and", ExpressionParserSlot.Logical)]
        [InlineData("!!!", ExpressionParserSlot.UnaryNot)]
        public void ObservationsExposeAcceptedParserSlots(string text, object expectedSlots)
        {
            var expected = (ExpressionParserSlot)expectedSlots;
            var observation = Replay(text).Observe();
            Assert.True((observation.Current & expected) == expected);
        }

        [Theory]
        [InlineData("Price", ExpressionParserSlot.Comparison | ExpressionParserSlot.Additive, true)]
        [InlineData("Price >", ExpressionParserSlot.Operand | ExpressionParserSlot.Group, true)]
        [InlineData("Price not i", ExpressionParserSlot.None, false)]
        [InlineData("Price in", ExpressionParserSlot.MembershipList | ExpressionParserSlot.Variable, true)]
        [InlineData("Name = 'Active'", ExpressionParserSlot.Logical, true)]
        [InlineData("Price > 1 and", ExpressionParserSlot.Operand | ExpressionParserSlot.UnaryNot, true)]
        [InlineData("Customer.", ExpressionParserSlot.None, false)]
        public void ObservationsDistinguishCompleteTokenContinuationsFromPartialTokens(
            string text, object expectedSlots, bool complete)
        {
            var expected = (ExpressionParserSlot)expectedSlots;
            var observation = Replay(text).Observe();
            Assert.Equal(complete, observation.HasCompleteToken);
            Assert.True((observation.Continuation & expected) == expected);
            if (!complete) Assert.Equal(ExpressionParserSlot.None, observation.Continuation);
        }

        [Theory]
        [InlineData("(", 1)]
        [InlineData("not (", 1)]
        [InlineData("not ((Price > 1", 2)]
        [InlineData("Name.contains(", 1)]
        [InlineData("(Name.contains('a'", 2)]
        public void ObservationsRetainFunctionAndGroupScope(string text, int scopes)
        {
            var observation = Replay(text).Observe();
            Assert.Equal(scopes, observation.OpenScopes);
            Assert.Equal(')', observation.ClosingDelimiter);
        }

        [Theory]
        [InlineData("Price in [", ']')]
        [InlineData("Price in [1,", ']')]
        [InlineData("Price in {", '}')]
        [InlineData("Price in {1,", '}')]
        [InlineData("Price in (", ')')]
        [InlineData("Price in (1,", ')')]
        public void ObservationsKeepMembershipContentSeparateFromScalarGrammar(string text, char closing)
        {
            var observation = Replay(text).Observe();
            Assert.True(observation.Current.HasFlag(ExpressionParserSlot.MembershipContent));
            Assert.False(observation.HasCompleteToken);
            Assert.Equal(closing, observation.ClosingDelimiter);
            Assert.Equal(1, observation.OpenScopes);
        }

        [Fact]
        public void QuotedContentIsNotReinterpretedAsExpressionGrammar()
        {
            var observation = Replay("Name = 'Price > ").Observe();
            Assert.True(observation.Current.HasFlag(ExpressionParserSlot.QuotedContent));
            Assert.False(observation.HasCompleteToken);
            Assert.Equal(ExpressionParserSlot.None, observation.Continuation);
        }

        [Theory]
        [InlineData("Customer.Name = 'a'")]
        [InlineData("Name.contains('a') and Price > 1")]
        [InlineData("Name in ['a', 'b']")]
        [InlineData("not (Price > 1 and Price < 5)")]
        [InlineData("!!!Enabled")]
        [InlineData("(Price + 1) >= 2")]
        public void RepeatedObservationsDoNotChangeCompletionOrParserChains(string text)
        {
            var baseline = Replay(text);
            var observed = Replay(text, observeEachCharacter: true);
            Assert.Equal(baseline.TryComplete(out var original), observed.TryComplete(out var inspected));
            Assert.Equal(Shape(original), Shape(inspected));
        }

        [Fact]
        public void RecoveryObservationDoesNotConsumeTheNextClause()
        {
            var parser = Replay("Price >");
            parser.Recover();
            var snapshot = parser.Observe();
            Assert.True(snapshot.Current.HasFlag(ExpressionParserSlot.Operand));
            Feed(parser, "Name = 'a'");
            Assert.True(parser.TryComplete(out _));
        }

        [Fact]
        public void CursorReplayIgnoresAnIndependentInvalidSuffix()
        {
            const string text = "Price > 1 and Unknown = )";
            var tokens = Interpreter.AnalyzeExpression(text).Tokens;
            var replay = ExpressionSyntaxReplay.Read(text, tokens, 9);
            Assert.False(replay.IsRecovering);
            Assert.True(replay.Parser.Observe().Continuation.HasFlag(ExpressionParserSlot.Logical));
        }

        [Fact]
        public void CursorReplayResumesAtTheSameRecoveryBoundaryAsAnalysis()
        {
            const string text = "Price = ) and Name";
            var tokens = Interpreter.AnalyzeExpression(text).Tokens;
            var replay = ExpressionSyntaxReplay.Read(text, tokens, text.Length);
            Assert.False(replay.IsRecovering);
            Assert.Equal(4, replay.RecoveryTokenEnd);
            Assert.True(replay.Parser.Observe().HasCompleteToken);
            Assert.True(replay.Parser.Observe().Continuation.HasFlag(ExpressionParserSlot.Comparison));
        }

        [Fact]
        public void PartialRecoveryOperatorDoesNotPretendAClauseWasRecovered()
        {
            const string text = "Price = ) and Name";
            var tokens = Interpreter.AnalyzeExpression(text).Tokens;
            var replay = ExpressionSyntaxReplay.Read(text, tokens, text.IndexOf("and", StringComparison.Ordinal) + 2);
            Assert.True(replay.IsRecovering);
        }

        [Fact]
        public void QuotedLogicalTextIsNotARecoveryBoundary()
        {
            const string text = "Name = 'bad and Price > 1";
            var tokens = Interpreter.AnalyzeExpression(text).Tokens;
            var replay = ExpressionSyntaxReplay.Read(text, tokens, text.Length);
            Assert.Equal(0, replay.RecoveryTokenEnd);
            Assert.True(replay.Parser.Observe().Current.HasFlag(ExpressionParserSlot.QuotedContent));
        }

        private static string[] Shape(IReadOnlyList<Parser> chain) => chain == null ? Array.Empty<string>() :
            chain.Select(parser => parser.GetType().Name + ":" + parser.Result + ":" +
                string.Join("/", Shape(parser.Body))).ToArray();

        private static ExpressionSyntaxParser Replay(string text, bool observeEachCharacter = false)
        {
            var parser = new ExpressionSyntaxParser(syntaxOnly: true);
            Feed(parser, text, observeEachCharacter);
            return parser;
        }

        private static void Feed(ExpressionSyntaxParser parser, string text, bool observeEachCharacter = false)
        {
            var whitespace = 0;
            for (var index = 0; index < text.Length; ++index)
            {
                if (!parser.KeepsWhitespace && char.IsWhiteSpace(text[index]))
                {
                    ++whitespace;
                    continue;
                }
                Assert.True(parser.TryRead(text[index], whitespace, index, hasNext: true),
                    "Cannot replay character at " + index + " in " + text);
                whitespace = 0;
                if (observeEachCharacter)
                {
                    var first = parser.Observe();
                    var second = parser.Observe();
                    Assert.Equal(first.Current, second.Current);
                    Assert.Equal(first.Continuation, second.Continuation);
                    Assert.Equal(first.HasCompleteToken, second.HasCompleteToken);
                    Assert.Equal(first.OpenScopes, second.OpenScopes);
                    Assert.Equal(first.ClosingDelimiter, second.ClosingDelimiter);
                }
            }
        }
    }
}
