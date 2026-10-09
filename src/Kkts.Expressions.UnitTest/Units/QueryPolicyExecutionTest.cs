using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyExecutionTest
    {
        [Fact]
        public void DiagnosticCollector_AllowsThirtyTwoDiagnosticsAndMarksFurtherReports()
        {
            var collector = new QueryPolicyDiagnosticCollector();
            for (var index = 0; index < 32; index++)
                collector.Add("policy-test-" + index, "Policy test.", index, 1);

            Assert.Equal(32, collector.ToReadOnlyList().Count);
            Assert.False(collector.IsTruncated);

            collector.Add("policy-test-32", "Policy test.", 32, 1);

            var diagnostics = collector.ToReadOnlyList();
            Assert.Equal(32, diagnostics.Count);
            Assert.Equal("query-policy-diagnostics-truncated", diagnostics[31].Code);
            Assert.True(collector.IsTruncated);
            collector.Add("policy-test-33", "Policy test.", 33, 1);
            Assert.Equal(32, collector.ToReadOnlyList().Count);
        }

        [Fact]
        public void DiagnosticCollector_DeduplicatesByCodeAndLocation()
        {
            var collector = new QueryPolicyDiagnosticCollector();
            collector.Add("policy-test", "First.", 4, 2, inputPath: "Where");
            collector.Add("policy-test", "Duplicate.", 4, 2, inputPath: "Where");
            collector.Add("policy-test", "Different path.", 4, 2, inputPath: "Filters[0].Value");
            collector.Add("policy-test", "Different span.", 4, 1, inputPath: "Where");

            Assert.Equal(3, collector.ToReadOnlyList().Count);
        }

        [Fact]
        public void DiagnosticCollector_CanOrderTextDiagnosticsAndPreserveStructuredSubmissionOrder()
        {
            var collector = new QueryPolicyDiagnosticCollector();
            collector.Add("later", "Later.", 9, 1);
            collector.Add("earlier", "Earlier.", 1, 1);

            Assert.Equal(new[] { "later", "earlier" },
                collector.ToReadOnlyList().Select(diagnostic => diagnostic.Code));
            Assert.Equal(new[] { "earlier", "later" },
                collector.ToReadOnlyList(sourceOrder: true).Select(diagnostic => diagnostic.Code));
        }

        [Fact]
        public void QueryPolicyCounter_UsesLongCountsAndSaturatesWithoutOverflow()
        {
            var counter = new QueryPolicyCounter((long)int.MaxValue);

            Assert.Equal((long)int.MaxValue + 1, counter.Increment());

            var saturated = new QueryPolicyCounter(long.MaxValue);
            Assert.Equal(long.MaxValue, saturated.Increment());
            Assert.Equal(long.MaxValue, saturated.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => new QueryPolicyCounter(-1));
        }

        [Fact]
        public void QueryPolicyExecution_UsesOperationLocalConditionAndMembershipCounters()
        {
            var policy = new QueryPolicy(maxAtomicConditions: 1, maxInItems: 1);
            var first = new QueryPolicyExecution(policy);
            var second = new QueryPolicyExecution(policy);

            Assert.True(first.TryCountCondition(0, 1));
            Assert.False(first.TryCountCondition(3, 1));
            Assert.Single(first.Diagnostics.ToReadOnlyList());
            Assert.True(second.TryCountCondition(3, 1));

            var membership = first.CreateMembershipCounter();
            Assert.True(first.TryCountMembershipItem(membership, 5, 1));
            Assert.False(first.TryCountMembershipItem(membership, 8, 1, "Filters[0].Value"));
            var membershipDiagnostic = first.Diagnostics.ToReadOnlyList()[1];
            Assert.Equal("query-policy-in-items-exceeded", membershipDiagnostic.Code);
            Assert.Equal(1, membershipDiagnostic.ConfiguredLimit);
            Assert.Equal(2, membershipDiagnostic.ObservedValue);
            Assert.True(membershipDiagnostic.ObservedValueIsLowerBound);
            Assert.Equal("Filters[0].Value", membershipDiagnostic.InputPath);
        }

        [Fact]
        public void QueryPolicyExecution_AdmitsExactUntrimmedUtf16LengthAndRejectsNextUnit()
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 4));

            Assert.True(execution.TryAdmitExpression(" x  "));
            Assert.False(execution.TryAdmitExpression(" x   "));
            Assert.Single(execution.Diagnostics.ToReadOnlyList());

            var diagnostic = execution.Diagnostics.ToReadOnlyList()[0];
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal(4, diagnostic.Start);
            Assert.Equal(1, diagnostic.Length);
            Assert.Equal(4, diagnostic.ConfiguredLimit);
            Assert.Equal(5, diagnostic.ObservedValue);
        }

        [Fact]
        public void QueryPolicyExecution_CountsSupplementaryCharactersAsTwoUtf16Units()
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 4));

            Assert.True(execution.TryAdmitExpression("'😀'"));
            Assert.False(execution.TryAdmitExpression("'😀' "));
            Assert.Equal(5, execution.Diagnostics.ToReadOnlyList()[0].ObservedValue);
        }

        [Theory]
        [InlineData("((Price = 1))", 2, true)]
        [InlineData("(((Price = 1)))", 2, false)]
        [InlineData("'((not in))' = Name", 0, true)]
        public void SourceScanner_CountsParenthesesOutsideQuotedText(
            string expression,
            int maximumDepth,
            bool expected)
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxParenthesisDepth: maximumDepth));

            Assert.Equal(expected, QueryPolicySourceScanner.TryScan(expression, execution));
            if (!expected)
            {
                var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
                Assert.Equal("query-policy-parenthesis-depth-exceeded", diagnostic.Code);
                Assert.Equal(maximumDepth, diagnostic.ConfiguredLimit);
                Assert.Equal(maximumDepth + 1, diagnostic.ObservedValue);
            }
        }

        [Theory]
        [InlineData("NOT (Price = 1)", 1, true)]
        [InlineData("Price = 1 AND Name contains 'a = b'", 2, true)]
        [InlineData("Price = 1 OR Name contains 'a = b' AND Discount >= 2", 2, false)]
        [InlineData("Price =", 0, false)]
        public void SourceScanner_CountsAtomicComparisonsNotBooleanOrNotNodes(
            string expression,
            int maximumConditions,
            bool expected)
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxAtomicConditions: maximumConditions));

            Assert.Equal(expected, QueryPolicySourceScanner.TryScan(expression, execution));
            if (!expected)
            {
                var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
                Assert.Equal("query-policy-condition-count-exceeded", diagnostic.Code);
                Assert.Equal(maximumConditions + 1, diagnostic.ObservedValue);
            }
        }

        [Theory]
        [InlineData("Price in [1, 1]", 2, true)]
        [InlineData("Price in (1, 1)", 2, true)]
        [InlineData("Price in {1, 1}", 2, true)]
        [InlineData("Price in [null, null]", 2, true)]
        [InlineData("Name in ['a,b', 'c']", 2, true)]
        [InlineData("Name in ['a\\'b', 'c']", 2, true)]
        [InlineData("Name in ['(a)', 'b']", 2, true)]
        [InlineData("Price NOT IN (1, 1, 2)", 2, false)]
        [InlineData("Name contains 'a,b'", 1, true)]
        [InlineData("Price in [1,]", 1, true)]
        [InlineData("Price in [1, 2,", 2, true)]
        public void SourceScanner_CountsMembershipSlotsBeforeDeduplication(
            string expression,
            int maximumItems,
            bool expected)
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxInItems: maximumItems));

            Assert.Equal(expected, QueryPolicySourceScanner.TryScan(expression, execution));
            if (!expected)
            {
                var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
                Assert.Equal("query-policy-in-items-exceeded", diagnostic.Code);
                Assert.Equal(expression.LastIndexOf('2'), diagnostic.Start);
                Assert.Equal(maximumItems, diagnostic.ConfiguredLimit);
                Assert.Equal(maximumItems + 1, diagnostic.ObservedValue);
            }
        }

        [Fact]
        public void SourceScanner_StopsOnFirstViolationBeforeAnalysis()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<ScannerEntity>(),
                new QueryPolicy(maxAtomicConditions: 1));

            var result = context.AnalyzeExpression("Value = 1 AND Value = 2");

            Assert.False(result.IsSemanticallyValid);
            Assert.Empty(result.Tokens);
            Assert.Equal("query-policy-condition-count-exceeded", Assert.Single(result.Diagnostics).Code);
        }

        private sealed class ScannerEntity
        {
            public int Value { get; set; }
        }
    }
}
