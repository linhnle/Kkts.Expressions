using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyAnalysisTest
    {
        [Fact]
        public void AnalyzeExpression_EnforcesNavigationDepthAndCollectionTraversal()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<PolicyEntity>(),
                new QueryPolicy(maxNavigationDepth: 1, allowCollectionAccess: false));

            Assert.Empty(context.AnalyzeExpression("Customer.Name = 'A'").Diagnostics);
            Assert.Empty(context.AnalyzeExpression("CreatedAt.Year = 2024").Diagnostics);
            Assert.Empty(context.AnalyzeExpression("Name.Length = 3").Diagnostics);

            var navigation = Assert.Single(context.AnalyzeExpression("Customer.Address.City = 'A'").Diagnostics);
            Assert.Equal("query-policy-navigation-depth-exceeded", navigation.Code);
            Assert.Equal(1, navigation.ConfiguredLimit);
            Assert.Equal(2, navigation.ObservedValue);

            var collection = Assert.Single(context.AnalyzeExpression("Children.Count > 0").Diagnostics);
            Assert.Equal("query-policy-collection-access-denied", collection.Code);
            Assert.DoesNotContain("Children", collection.Message);
        }

        [Fact]
        public void AnalyzeExpression_IntersectsCanonicalFieldPermissionsAcrossAliasesAndOperands()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>(
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["cost"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
                    ["Price"] = new[] { ComparisonOperator.Equal },
                    ["Discount"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan }
                }));

            Assert.True(context.AnalyzeExpression("cost == 1").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Price = 1").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Price = Discount").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Discount > 1").IsSemanticallyValid);

            AssertOperatorDenied(context.AnalyzeExpression("cost > 1"));
            AssertOperatorDenied(context.AnalyzeExpression("Price > 1"));
            AssertOperatorDenied(context.AnalyzeExpression("Discount > Price"));
            AssertOperatorDenied(context.AnalyzeExpression("Price + Discount > 1"));
            AssertOperatorDenied(context.AnalyzeExpression("NOT (Price > 1)"));
        }

        [Fact]
        public void AnalyzeExpression_ImplicitBooleanPredicatesUseEqualPermissionAndConditionBudget()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>();
            var deniedContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Enabled"] = new[] { ComparisonOperator.NotEqual }
                }));

            AssertOperatorDenied(deniedContext.AnalyzeExpression("Enabled"));
            AssertOperatorDenied(deniedContext.AnalyzeExpression("NOT (Enabled)"));

            var limitedContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(maxAtomicConditions: 1));
            Assert.True(limitedContext.AnalyzeExpression("NOT (Enabled)").IsSemanticallyValid);
            var overLimit = limitedContext.AnalyzeExpression("Enabled AND Enabled");
            Assert.False(overLimit.IsSemanticallyValid);
            var diagnostic = Assert.Single(overLimit.Diagnostics);
            Assert.Equal("query-policy-condition-count-exceeded", diagnostic.Code);
            Assert.Equal(1, diagnostic.ConfiguredLimit);
            Assert.Equal(2, diagnostic.ObservedValue);
        }

        [Fact]
        public void AnalyzeExpression_HandlesLongUnaryChainsAndStopsAtExcessBareBooleanPredicate()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<PolicyEntity>(),
                new QueryPolicy(maxAtomicConditions: 1));
            var unaryChain = string.Concat(Enumerable.Repeat("!", 5000)) + "Enabled";
            Assert.True(context.AnalyzeExpression(unaryChain).IsSemanticallyValid);

            var booleanChain = string.Join(" AND ", Enumerable.Repeat("Enabled", 2000));
            var result = context.AnalyzeExpression(booleanChain);
            Assert.False(result.IsSemanticallyValid);
            Assert.True(result.IsTruncated);
            Assert.Equal(
                "query-policy-condition-count-exceeded",
                Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_CountsAComputedArithmeticPredicateOnce()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<PolicyEntity>(),
                new QueryPolicy(maxAtomicConditions: 1));
            var expression = string.Join(" + ", Enumerable.Repeat("1", 100)) + " + Price > 10";

            var result = context.AnalyzeExpression(expression);

            Assert.True(result.IsSemanticallyValid);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void AnalyzeExpression_CapsCombinedPolicyDiagnostics()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<PolicyEntity>(),
                new QueryPolicy(maxAtomicConditions: 64));
            var expression = string.Join(" AND ", Enumerable.Repeat("Missing = 1", 40));

            var result = context.AnalyzeExpression(expression);

            Assert.False(result.IsSemanticallyValid);
            Assert.True(result.IsTruncated);
            Assert.Equal(32, result.Diagnostics.Count);
            Assert.Equal(
                "query-policy-diagnostics-truncated",
                result.Diagnostics[31].Code);
        }

        [Fact]
        public void AnalyzeExpression_DoesNotSuggestRestrictedFieldNames()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>(
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Secret", typeof(string), canQuery: false)
                });
            var context = new ExpressionQueryContext(schema, new QueryPolicy());

            var result = context.AnalyzeExpression("Secre = 'x'");

            Assert.True(result.IsComplete);
            Assert.False(result.IsSemanticallyValid);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("unknown-property", diagnostic.Code);
            Assert.Empty(diagnostic.Suggestions);
        }

        private static void AssertOperatorDenied(ExpressionSemanticAnalysisResult result)
        {
            Assert.False(result.IsSemanticallyValid);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "query-policy-operator-denied");
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code == "query-policy-operator-denied" &&
                diagnostic.Message.Contains("Price", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class PolicyEntity
        {
            public decimal Price { get; set; }
            public decimal Discount { get; set; }
            public bool Enabled { get; set; }
            public string Name { get; set; }
            public string Secret { get; set; }
            public DateTime CreatedAt { get; set; }
            public PolicyCustomer Customer { get; set; }
            public List<PolicyChild> Children { get; set; }
        }

        private sealed class PolicyCustomer
        {
            public string Name { get; set; }
            public PolicyAddress Address { get; set; }
        }

        private sealed class PolicyAddress
        {
            public string City { get; set; }
        }

        private sealed class PolicyChild
        {
            public string Name { get; set; }
        }
    }
}
