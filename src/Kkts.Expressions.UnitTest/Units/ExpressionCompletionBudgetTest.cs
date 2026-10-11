using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionBudgetTest
    {
        [Fact]
        public void SourceLengthHasExactUtf16BoundaryAndPolicyPrecedence()
        {
            var policy = new QueryPolicy();
            var atLimit = new QueryPolicyExecution(policy);
            Assert.True(QueryPolicySourceScanner.TryScanCompletion(new string(' ', 16384), atLimit));
            var beyond = new QueryPolicyExecution(policy);
            Assert.False(QueryPolicySourceScanner.TryScanCompletion(new string(' ', 16385), beyond));
            var diagnostic = Assert.Single(beyond.Diagnostics.ToReadOnlyList());
            Assert.Equal("completion-work-limit-exceeded", diagnostic.Code);
            Assert.Equal((16384, 1), (diagnostic.Start, diagnostic.Length));
            var tighter = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 4));
            Assert.False(QueryPolicySourceScanner.TryScanCompletion(new string(' ', 16385), tighter));
            Assert.Equal("query-policy-expression-length-exceeded",
                Assert.Single(tighter.Diagnostics.ToReadOnlyList()).Code);
            Assert.True(QueryPolicySourceScanner.TryScanCompletion("'\ud83d\ude00'",
                new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 4))));
        }

        [Fact]
        public void LexerStopsAtFirstExcessTokenWithoutChangingUnboundedLexing()
        {
            var source = string.Concat(Enumerable.Repeat("() ", 4096));
            var tokens = new List<ExpressionToken>();
            var diagnostics = new List<ExpressionSyntaxDiagnostic>();
            var lexer = new ExpressionLexer(source, tokens,
                (code, message, start, length) => diagnostics.Add(new ExpressionSyntaxDiagnostic(code, message, start, length)),
                ExpressionCompletionBudget.MaximumTokens);
            lexer.ReadAll();
            Assert.Equal(8192, tokens.Count);
            Assert.False(lexer.IsTruncated);
            Assert.Empty(diagnostics);

            var excessiveTokens = new List<ExpressionToken>();
            lexer = new ExpressionLexer(source + "Id", excessiveTokens,
                (code, message, start, length) => diagnostics.Add(new ExpressionSyntaxDiagnostic(code, message, start, length)),
                ExpressionCompletionBudget.MaximumTokens);
            lexer.ReadAll();
            Assert.True(lexer.IsTruncated);
            Assert.Equal(8192, excessiveTokens.Count);
            Assert.Equal("completion-work-limit-exceeded", Assert.Single(diagnostics).Code);
            Assert.Equal(source.Length, diagnostics[0].Start);
            var unlimitedTokens = new List<ExpressionToken>();
            new ExpressionLexer(source + "Id", unlimitedTokens, (code, message, start, length) => { }).ReadAll();
            Assert.Equal(8193, unlimitedTokens.Count);
        }

        [Fact]
        public void ScopeBudgetIgnoresQuotesAndAllowsExactBoundary()
        {
            Assert.True(QueryPolicySourceScanner.TryScanCompletion(new string('(', 64),
                new QueryPolicyExecution(new QueryPolicy())));
            var exceeded = new QueryPolicyExecution(new QueryPolicy());
            Assert.False(QueryPolicySourceScanner.TryScanCompletion(new string('(', 65), exceeded));
            Assert.Equal(64, Assert.Single(exceeded.Diagnostics.ToReadOnlyList()).Start);
            var quote = "Name = '" + new string('(', 100) + "'";
            Assert.True(QueryPolicySourceScanner.TryScanCompletion(quote, new QueryPolicyExecution(new QueryPolicy())));
            Assert.True(QueryPolicySourceScanner.TryScanCompletion(string.Concat(Enumerable.Repeat("not ", 1000)) + "Flag",
                new QueryPolicyExecution(new QueryPolicy())));
            Assert.True(QueryPolicySourceScanner.TryScanCompletion(new string('[', 64),
                new QueryPolicyExecution(new QueryPolicy())));
            Assert.False(QueryPolicySourceScanner.TryScanCompletion(new string('[', 65),
                new QueryPolicyExecution(new QueryPolicy())));
        }

        [Fact]
        public void QueryDepthWinsBeforeCompletionDepthWhenTighter()
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxParenthesisDepth: 2));
            Assert.False(QueryPolicySourceScanner.TryScanCompletion("(((Id = 1", execution));
            Assert.Equal("query-policy-parenthesis-depth-exceeded", Assert.Single(execution.Diagnostics.ToReadOnlyList()).Code);
            Assert.True(QueryPolicySourceScanner.TryScan(new string('(', 65), new QueryPolicyExecution(new QueryPolicy())));
        }
    }
}
