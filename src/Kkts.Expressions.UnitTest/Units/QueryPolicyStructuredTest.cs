using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyStructuredTest
    {
        [Fact]
        public void FilterSequences_EnforceAggregateConditionAndMembershipLimits()
        {
            var context = CreateContext(new QueryPolicy(
                maxAtomicConditions: 1,
                maxInItems: 1,
                allowedOperators: Operators(ComparisonOperator.Equal, ComparisonOperator.In)));

            var conditionFailure = context.TryBuildPredicate<TestEntity>(new[]
            {
                NewFilter("Price", "=", "1"),
                NewFilter("Price", "=", "2")
            });
            Assert.False(conditionFailure.Succeeded);
            Assert.Null(conditionFailure.Result);
            Assert.Equal("query-policy-condition-count-exceeded", Assert.Single(conditionFailure.Diagnostics).Code);
            Assert.Equal("Filters[1].Operator", conditionFailure.Diagnostics[0].InputPath);

            var membershipFailure = context.TryBuildPredicate<TestEntity>(new[]
            {
                NewFilter("Price", "in", "1, 1")
            });
            Assert.False(membershipFailure.Succeeded);
            Assert.Null(membershipFailure.Result);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(membershipFailure.Diagnostics).Code);
            Assert.Equal("Filters[0].Value", membershipFailure.Diagnostics[0].InputPath);
            Assert.Equal(2, membershipFailure.Diagnostics[0].ObservedValue);
            Assert.True(membershipFailure.Diagnostics[0].ObservedValueIsLowerBound);
        }

        [Fact]
        public void FilterGroups_EnforceGroupDepthAndRetainOrComposition()
        {
            var deniedContext = CreateContext(new QueryPolicy(
                maxParenthesisDepth: 0,
                allowedOperators: Operators(ComparisonOperator.Equal)));
            var group = new FilterGroup { Filters = new List<Filter> { NewFilter("Price", "=", "1") } };

            var denied = deniedContext.TryBuildPredicate<TestEntity>(group);
            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("query-policy-parenthesis-depth-exceeded", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal("FilterGroup", denied.Diagnostics[0].InputPath);

            var allowedContext = CreateContext(new QueryPolicy(
                maxParenthesisDepth: 1,
                maxAtomicConditions: 2,
                allowedOperators: Operators(ComparisonOperator.Equal)));
            var groups = allowedContext.TryBuildPredicate<TestEntity>(new[]
            {
                group,
                new FilterGroup { Filters = new List<Filter> { NewFilter("Price", "=", "2") } }
            });

            Assert.True(groups.Succeeded);
            Assert.True(groups.Result.Compile()(new TestEntity { Price = 2m }));
            Assert.False(groups.Result.Compile()(new TestEntity { Price = 3m }));
        }

        [Fact]
        public void FilterGroupSequenceDiagnosticsIdentifyTheFailingNestedValue()
        {
            var context = CreateContext(new QueryPolicy(
                maxParenthesisDepth: 1,
                maxInItems: 0,
                allowedOperators: Operators(ComparisonOperator.In)));
            var result = context.TryBuildPredicate<TestEntity>(new[]
            {
                new FilterGroup { Filters = new List<Filter>() },
                new FilterGroup { Filters = new List<Filter> { NewFilter("Price", "in", "1") } }
            });

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(result.Diagnostics).Code);
            Assert.Equal("FilterGroups[1].Filters[0].Value", result.Diagnostics[0].InputPath);
            Assert.Equal(0, result.Diagnostics[0].Start);
            Assert.Equal(0, result.Diagnostics[0].Length);
        }

        [Fact]
        public void StructuredPermissions_ResolveAliasesAndNormalizeOperatorAliases()
        {
            var schema = ExpressionSchema.FromType<TestEntity>(
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["cost"] = new[] { ComparisonOperator.Equal },
                    ["Price"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan }
                }));

            var accepted = context.TryBuildPredicate<TestEntity>(NewFilter("cost", "==", "5"));
            var denied = context.TryBuildPredicate<TestEntity>(NewFilter("Price", ">", "1"));

            Assert.True(accepted.Succeeded);
            Assert.True(accepted.Result.Compile()(new TestEntity { Price = 5m }));
            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("query-policy-operator-denied", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal("Filter.Operator", denied.Diagnostics[0].InputPath);
        }

        [Fact]
        public async Task AsyncStructuredAdmissionPrecedesVariableResolution()
        {
            var context = CreateContext(new QueryPolicy(
                maxAtomicConditions: 1,
                allowedOperators: Operators(ComparisonOperator.Equal)));
            var resolver = new CountingResolver();

            var result = await context.TryBuildPredicateAsync<TestEntity>(
                new[]
                {
                    NewFilter("Price", "=", "$value"),
                    NewFilter("Price", "=", "2")
                },
                resolver);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-condition-count-exceeded", Assert.Single(result.Diagnostics).Code);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        [Fact]
        public void ConditionOptions_ApplyOneBudgetAcrossWhereAndStructuredFilters()
        {
            var context = CreateContext(new QueryPolicy(
                maxAtomicConditions: 1,
                allowedOperators: Operators(ComparisonOperator.Equal)));
            var resolver = new CountingResolver();
            var options = new ConditionOptions
            {
                Where = "Price = $value",
                Filters = new[] { NewFilter("Price", "=", "2") },
                OrderBy = "Price"
            };

            var condition = context.BuildCondition(options, resolver);

            Assert.False(condition.IsValid);
            Assert.Null(condition.Predicates);
            Assert.Null(condition.OrderByClause);
            Assert.Equal("query-policy-condition-count-exceeded",
                Assert.Single(condition.Error.EvaluationResult.Diagnostics).Code);
            Assert.Equal("Filters[0].Operator",
                condition.Error.EvaluationResult.Diagnostics[0].InputPath);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        [Fact]
        public async Task AsyncConditionOptions_PreflightAllInputsBeforeResolverWork()
        {
            var context = CreateContext(new QueryPolicy(
                maxExpressionLength: 15,
                allowedOperators: Operators(ComparisonOperator.Equal)));
            var resolver = new CountingResolver();
            var options = new ConditionOptions
            {
                Where = "Price = $value",
                Filters = new[] { NewFilter("Price", "=", "1") }
            };

            var condition = await context.BuildConditionAsync(options, resolver);

            Assert.False(condition.IsValid);
            Assert.Null(condition.Predicates);
            Assert.Equal("query-policy-expression-length-exceeded",
                Assert.Single(condition.Error.EvaluationResult.Diagnostics).Code);
            Assert.Equal("Filters[0].Value", condition.Error.EvaluationResult.Diagnostics[0].InputPath);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        private static ExpressionQueryContext CreateContext(QueryPolicy policy)
            => new ExpressionQueryContext(ExpressionSchema.FromType<TestEntity>(), policy);

        private static Dictionary<string, IEnumerable<ComparisonOperator>> Operators(params ComparisonOperator[] values)
            => new Dictionary<string, IEnumerable<ComparisonOperator>> { ["Price"] = values };

        private static Filter NewFilter(string property, string @operator, string value)
            => new Filter { Property = property, Operator = @operator, Value = value };

        private sealed class TestEntity
        {
            public decimal Price { get; set; }
        }

        private sealed class CountingResolver : VariableResolver
        {
            internal int ResolveCalls;

            protected override Task<VariableInfo> TryResolveCore(
                string name,
                CancellationToken cancellationToken)
            {
                ++ResolveCalls;
                return Task.FromResult(new VariableInfo
                {
                    Name = name,
                    Resolved = true,
                    Value = 1m
                });
            }
        }
    }
}
