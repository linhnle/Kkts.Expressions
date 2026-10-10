using System;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionPolicyTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();

        [Theory]
        [InlineData("Status in ['Active'|", "]")]
        [InlineData("Status IN {'Active'|", "}")]
        [InlineData("Status NOT   IN ('Active'|", ")")]
        public void FullMembershipBudgetRetainsOnlyMatchingClosers(string marked, string closer)
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxInItems: 1));
            var result = Complete(marked, context);
            Assert.Contains(result.Items, item => item.Label == closer);
            Assert.DoesNotContain(result.Items, item => item.Label == ",");
        }

        [Theory]
        [InlineData("Price >|= 1", ">=")]
        [InlineData("Price !|= 1", "!=")]
        public void ReplacingAnExistingComparisonDoesNotChargeAnotherCondition(string marked, string op)
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxAtomicConditions: 1));
            var item = Complete(marked, context).Items.Single(candidate => candidate.Label == op);
            var text = marked.Replace("|", "");
            var edited = text.Substring(0, item.Start) + item.InsertionText + text.Substring(item.Start + item.Length);
            Assert.True(context.AnalyzeExpression(edited).IsSemanticallyValid);
        }

        [Fact]
        public void CollectionVariableCardinalityIsDeferredRatherThanGuessed()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("prices", typeof(decimal[])),
                new ExpressionVariableDefinition("names", typeof(string[]))
            });
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxInItems: 1));
            var result = context.CompleteExpression("Price in $", 10, variables);
            Assert.Equal(ExpressionCompletionStatus.Available, result.Status);
            Assert.Contains(result.Items, item => item.InsertionText == "$prices");
            Assert.DoesNotContain(result.Items, item => item.InsertionText == "$names");
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == "query-policy-in-items-exceeded");
        }

        [Fact]
        public void ResourceRefusalUsesTighterPolicyBeforeCompletionCeilings()
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxExpressionLength: 5));
            var text = new string(' ', 16385);
            var result = context.CompleteExpression(text, text.Length);
            Assert.Equal(ExpressionCompletionStatus.LimitExceeded, result.Status);
            Assert.True(result.IsIncomplete);
            Assert.Empty(result.Items);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal(5, diagnostic.Start);
        }

        private static ExpressionCompletionResult Complete(string marked, ExpressionQueryContext context) =>
            context.CompleteExpression(marked.Replace("|", ""), marked.IndexOf('|'));

        public class Product
        {
            public decimal Price { get; set; }
            public string Status { get; set; }
        }
    }
}
