using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionApiTest
    {
        private static readonly ExpressionSchema Schema = new QuerySchema<Product>()
            .Field("Price", product => product.Price, allowedOperators: new[]
            {
                ComparisonOperator.Equal, ComparisonOperator.NotEqual, ComparisonOperator.GreaterThan,
                ComparisonOperator.GreaterThanOrEqual, ComparisonOperator.LessThan,
                ComparisonOperator.LessThanOrEqual, ComparisonOperator.In
            })
            .Field("Status", product => product.Status)
            .Field("CreatedAt", product => product.CreatedAt)
            .Field("Enabled", product => product.Enabled)
            .Build();
        private static readonly ExpressionVariableSchema Variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
            new ExpressionVariableDefinition("startOfMonth", typeof(DateTime))
        });
        private static readonly ExpressionValueSuggestionSchema Hints = new ExpressionValueSuggestionSchema(Schema,
            new[]
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Status", new[]
                {
                    new ExpressionValueSuggestion(FilterValue.String("Active")),
                    new ExpressionValueSuggestion(FilterValue.String("Pending")),
                    new ExpressionValueSuggestion(FilterValue.String("Closed"))
                })
            });

        [Fact]
        public void RepresentativeResultsUseExplicitMetadataAndExactAcceptedText()
        {
            Assert.Equal(new[] { "=", "!=", ">", ">=", "<", "<=", "in" }, Complete("Price |").Items.Select(item => item.InsertionText));
            var values = Complete("Status = |").Items.Where(item => item.Kind == ExpressionCompletionKind.Value).ToArray();
            Assert.Equal(new[] { "'Active'", "'Closed'", "'Pending'", "null" }, values.Select(item => item.InsertionText));
            Assert.True(Interpreter.AnalyzeExpression(Apply("Status = ", values[0]), typeof(Product), Schema).IsSemanticallyValid);
            var variables = Complete("CreatedAt > $|").Items;
            Assert.Equal(new[] { "$startOfMonth", "$utcnow" }, variables.Select(item => item.InsertionText));
            Assert.True(Interpreter.AnalyzeExpression(Apply("CreatedAt > $", variables[0]), typeof(Product), Schema, Variables).IsSemanticallyValid);
            var nested = ExpressionSchema.FromType<Product>();
            var path = Assert.Single(Interpreter.CompleteExpression("Customer.", 9, nested).Items);
            Assert.Equal("Customer.Name", path.InsertionText);
            Assert.Equal(0, path.Start);
            Assert.Equal(9, path.Length);
        }

        [Fact]
        public void RequiredArgumentsAndUtf16CursorPositionsFailExplicitly()
        {
            Assert.Throws<ArgumentNullException>(() => Interpreter.CompleteExpression(null, 0, Schema));
            Assert.Throws<ArgumentNullException>(() => Interpreter.CompleteExpression("", 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Interpreter.CompleteExpression("", -1, Schema));
            Assert.Throws<ArgumentOutOfRangeException>(() => Interpreter.CompleteExpression("", 1, Schema));
            Assert.Throws<ArgumentOutOfRangeException>(() => Interpreter.CompleteExpression("\U0001f600", 1, Schema));
            Assert.Equal(ExpressionCompletionStatus.Available, Interpreter.CompleteExpression("  \r\n", 4, Schema).Status);
        }

        [Fact]
        public void SourcePolicyAndFixedWorkLimitsHaveExplicitPositionedResults()
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxExpressionLength: 5));
            var limited = context.CompleteExpression("Price ", 6);
            Assert.Equal(ExpressionCompletionStatus.LimitExceeded, limited.Status);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(limited.Diagnostics).Code);
            Assert.Equal(5, limited.Diagnostics[0].Start);
            Assert.True(limited.IsIncomplete);
            Assert.Empty(limited.Items);
            Assert.NotEqual(ExpressionCompletionStatus.LimitExceeded,
                Interpreter.CompleteExpression(new string(' ', 16384), 16384, Schema).Status);
            foreach (var text in new[] { new string(' ', 16385), new string('!', 8193), new string('(', 65) })
            {
                limited = Interpreter.CompleteExpression(text, text.Length, Schema);
                Assert.Equal(ExpressionCompletionStatus.LimitExceeded, limited.Status);
                Assert.Equal("completion-work-limit-exceeded", Assert.Single(limited.Diagnostics).Code);
                Assert.Empty(limited.Items);
                Assert.True(limited.IsIncomplete);
            }
        }

        [Fact]
        public void ContextAllowlistsPermissionsAndSnapshotBindingArePreserved()
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(), new[] { "Price" });
            Assert.Equal(new[] { "Price" }, context.CompleteExpression("", 0).Items
                .Where(item => item.Kind == ExpressionCompletionKind.Field).Select(item => item.InsertionText));
            Assert.Throws<ArgumentException>(() => Interpreter.CompleteExpression("", 0,
                ExpressionSchema.FromType<Product>(), valueSuggestions: Hints));
        }

        [Theory]
        [InlineData("Unknown |")]
        [InlineData("Unknown = |")]
        [InlineData("'arbitrary| text'")]
        [InlineData("Price > | 1")]
        [InlineData("Price = ) an|d Status")]
        public void UnreliableLocalContextsReturnPositionedSuppression(string marked)
        {
            var result = Complete(marked);
            Assert.Equal(ExpressionCompletionStatus.ContextUnavailable, result.Status);
            Assert.Empty(result.Items);
            var diagnostic = Assert.Single(result.Diagnostics.Where(
                item => item.Code == "completion-context-unavailable"));
            Assert.Equal(marked.IndexOf('|'), diagnostic.Start);
            Assert.Equal(0, diagnostic.Length);
        }

        [Theory]
        [InlineData("Zzz|")]
        [InlineData("Status = 'Zzz|'")]
        public void ReliableUnmatchedPrefixesDoNotFabricateSuppression(string marked)
        {
            var result = Complete(marked);
            Assert.Equal(ExpressionCompletionStatus.NoMatches, result.Status);
            Assert.Empty(result.Items);
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == "completion-context-unavailable");
        }

        [Theory]
        [InlineData("Price = ) and Sta|")]
        [InlineData("Price > 1 and Sta| = 'Active' and Unknown = )")]
        [InlineData("(Price = ) and Sta|")]
        public void IndependentSyntaxErrorsPreserveReliableRecoveredFieldEdits(string marked)
        {
            var result = Complete(marked);
            Assert.Equal(ExpressionCompletionStatus.Available, result.Status);
            Assert.Contains(result.Items, item => item.InsertionText == "Status");
            Assert.DoesNotContain(result.Diagnostics, item => item.Code == "completion-context-unavailable");
        }

        [Fact]
        public void DeniedReceiverDoesNotDiscloseHintsThroughSuppression()
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(), new[] { "Price" });
            var result = Complete("Status = |", context);
            Assert.Equal(ExpressionCompletionStatus.ContextUnavailable, result.Status);
            Assert.Empty(result.Items);
            Assert.DoesNotContain(result.Diagnostics, item => item.Message.Contains("Active"));
            Assert.DoesNotContain(result.Diagnostics, item => item.Message.Contains("Closed"));
        }

        [Fact]
        public void GroupNotLogicalAndListConstructsAreMinimalAndDoNotDuplicatePunctuation()
        {
            Assert.Contains(Complete("|").Items, item => item.InsertionText == "not(");
            var result = Complete("not (Price > 1|");
            Assert.Equal(new[] { " and", " or", ")" }, result.Items.Select(item => item.InsertionText));
            Assert.DoesNotContain(Complete("not (Price > 1|)").Items, item => item.Label == ")");
            Assert.DoesNotContain(Complete("Price > 1 |and Status = 'Active'").Items, item => item.Kind == ExpressionCompletionKind.LogicalOperator);
            Assert.All(Complete("Price in |").Items.Where(item => item.Kind == ExpressionCompletionKind.Delimiter),
                item => Assert.Equal(1, item.InsertionText.Length));
            Assert.DoesNotContain(Complete("Status in ['Active'|, 'Pending']").Items, item => item.Label == ",");
        }

        [Fact]
        public void ImplicitBooleanConditionsOfferOnlyPermittedLogicalContinuations()
        {
            Assert.Contains(Complete("Enabled |").Items, item => item.Label == "and");
            Assert.Contains(Complete("Enabled |").Items, item => item.Label == "or");
            Assert.Contains(Complete("not (Enabled|").Items, item => item.InsertionText == ")");
            var bounded = new ExpressionQueryContext(Schema, new QueryPolicy(maxAtomicConditions: 1));
            var items = Complete("not (Enabled|", bounded).Items;
            Assert.Contains(items, item => item.InsertionText == ")");
            Assert.DoesNotContain(items, item => item.Label == "and" || item.Label == "or");
            var restricted = new QuerySchema<Product>()
                .Field("Enabled", product => product.Enabled,
                    allowedOperators: new[] { ComparisonOperator.NotEqual }).Build();
            var denied = Interpreter.CompleteExpression("Enabled ", 8, restricted);
            Assert.DoesNotContain(denied.Items, item => item.Kind == ExpressionCompletionKind.LogicalOperator);
            Assert.Contains(denied.Items, item => item.Label == "!=");
        }

        [Fact]
        public void ActualEditsHonorLengthDepthConditionAndMembershipBudgets()
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxAtomicConditions: 1, maxParenthesisDepth: 1, maxInItems: 1));
            var result = Complete("not (Price > 1|", context);
            Assert.Equal(")", Assert.Single(result.Items).InsertionText);
            Assert.Contains(Complete("Price >|= 1", context).Items, item => item.Label == ">=");
            Assert.DoesNotContain(Complete("(Price > |", context).Items, item => item.InsertionText.Contains("("));
            Assert.DoesNotContain(Complete("Status in ['Active', |]", context).Items, item => item.Kind == ExpressionCompletionKind.Value);
            Assert.Contains(Complete("Status in ['Ac|ZZ']", context).Items, item => item.InsertionText == "'Active'");
            Assert.DoesNotContain(Complete("Status in ['Active'|", context).Items, item => item.Label == ",");
            Assert.Equal(ExpressionCompletionStatus.LimitExceeded, Complete("Enabled and Enabled|", context).Status);
            Assert.Contains(Complete("Ena|bled", context).Items, item => item.InsertionText == "Enabled");
            var length = new ExpressionQueryContext(Schema, new QueryPolicy(maxExpressionLength: 10));
            Assert.DoesNotContain(Complete("Status = |", length).Items, item => item.Kind == ExpressionCompletionKind.Value);
        }

        [Theory]
        [InlineData("Status in ['Active'|")]
        [InlineData("Status IN ['Active'|")]
        [InlineData("Status NOT   IN ['Active'|")]
        public void MembershipBudgetUsesNormalizedOperatorIdentity(string marked)
        {
            var context = new ExpressionQueryContext(Schema, new QueryPolicy(maxInItems: 1));
            var result = Complete(marked, context);
            Assert.DoesNotContain(result.Items, item => item.Label == ",");
            Assert.Contains(result.Items, item => item.Label == "]");
        }

        private static ExpressionCompletionResult Complete(string marked, ExpressionQueryContext context = null)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            return context == null ? Interpreter.CompleteExpression(text, position, Schema, Variables, Hints) :
                context.CompleteExpression(text, position, Variables, Hints);
        }

        private static string Apply(string text, ExpressionCompletionItem item) =>
            text.Substring(0, item.Start) + item.InsertionText + text.Substring(item.Start + item.Length);

        public class Product
        {
            public decimal Price { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool Enabled { get; set; }
            public Customer Customer { get; set; }
        }
        public class Customer { public string Name { get; set; } }
    }
}
