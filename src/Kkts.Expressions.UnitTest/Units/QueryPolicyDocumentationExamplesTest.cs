using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyDocumentationExamplesTest
    {
        [Fact]
        public void ConfigurationGuideExample_UsesPublicPolicyAndContextContracts()
        {
            var schema = ExpressionSchema.FromType<GuideProduct>(
                validProperties: new[] { "Id", "Name", "Price" });
            var policy = new QueryPolicy(
                maxExpressionLength: 4096,
                maxParenthesisDepth: 16,
                maxAtomicConditions: 64,
                maxInItems: 100,
                maxNavigationDepth: 3,
                allowCollectionAccess: false,
                allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Price"] = new[]
                    {
                        ComparisonOperator.Equal,
                        ComparisonOperator.GreaterThan
                    },
                    ["Name"] = new[]
                    {
                        ComparisonOperator.Equal,
                        ComparisonOperator.StartsWith
                    }
                });

            var context = new ExpressionQueryContext(
                schema,
                policy,
                validProperties: new[] { "Id", "Name", "Price" });

            Assert.Same(schema, context.Schema);
            Assert.Same(policy, context.Policy);
            Assert.Equal(4096, context.Policy.MaxExpressionLength);
        }

        [Fact]
        public void LegacyPredicateCallWithPositionalNullArgumentsStillCompilesAndWorks()
        {
            var result = Interpreter.ParsePredicate<GuideProduct>(
                "Id = 1",
                null,
                null,
                null);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Result);
        }

        [Fact]
        public void CountingGuideExamples_MatchPolicyAnalysisRules()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<GuideProduct>(),
                new QueryPolicy(maxAtomicConditions: 2, maxInItems: 2));

            Assert.True(context.AnalyzeExpression("NOT (Price = 1 OR Price = 2)").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Price + 1 > 10").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Name = 'a = b, (x)'").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Name in ['a', 'a']").IsSemanticallyValid);

            var excessiveItems = context.AnalyzeExpression("Name in ['a', 'a', null]");
            Assert.False(excessiveItems.IsSemanticallyValid);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(excessiveItems.Diagnostics).Code);
        }

        [Fact]
        public void RuntimeStructuredAndOrderingGuideExamples_UseContextContracts()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<GuideProduct>(
                    validProperties: new[] { "Id", "Name", "Price" }),
                new QueryPolicy(
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.GreaterThanOrEqual },
                        ["Name"] = new[] { ComparisonOperator.StartsWith }
                    }));

            var filters = context.TryBuildPredicate<GuideProduct>(new[]
            {
                new Filter { Property = "Price", Operator = ">=", Value = "10" },
                new Filter { Property = "Name", Operator = "startswith", Value = "A" }
            });
            var orderBy = context.TryBuildOrderByClause(new OrderByInfo { Property = "Price" });
            var condition = context.BuildCondition<GuideProduct>(
                new ConditionOptions
                {
                    Where = "Price >= 10",
                    Filters = new[]
                    {
                        new Filter { Property = "Name", Operator = "startswith", Value = "A" }
                    },
                    OrderBys = new List<OrderByInfo>
                    {
                        new OrderByInfo { Property = "Price", Descending = true }
                    }
                });

            Assert.True(filters.Succeeded);
            Assert.True(orderBy.Succeeded);
            Assert.True(condition.IsValid);
            Assert.Equal(2, condition.Predicates.Count);
            Assert.NotNull(condition.OrderByClause);
        }

        [Fact]
        public void TenantGuideExample_KeepsMandatoryPredicateOutsideUserInput()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<GuideProduct>(
                    validProperties: new[] { "Id", "Name", "Price" }),
                QueryPolicy.Recommended);
            var evaluation = context.ParsePredicate<GuideProduct>(
                "Name = 'allowed' OR Name = 'other'");

            Assert.True(evaluation.Succeeded);
            var tenantScoped = new[]
            {
                new GuideProduct { Id = 1, TenantId = 7, Name = "allowed" },
                new GuideProduct { Id = 2, TenantId = 7, Name = "other" },
                new GuideProduct { Id = 3, TenantId = 8, Name = "other" }
            }.AsQueryable()
                .Where(item => item.TenantId == 7)
                .Where(evaluation.Result)
                .ToArray();

            Assert.Equal(new[] { 1, 2 }, tenantScoped.Select(item => item.Id));
        }

        private sealed class GuideProduct
        {
            public int Id { get; set; }
            public int TenantId { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
        }
    }
}
