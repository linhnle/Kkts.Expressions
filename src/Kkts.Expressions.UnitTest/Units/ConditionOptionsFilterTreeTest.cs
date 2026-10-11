using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ConditionOptionsFilterTreeTest
    {
        [Fact]
        public async Task ConditionOptionsAppendsTreeAfterExistingInputsAcrossEntryPoints()
        {
            var options = CreateOptions(FilterNode.Condition(
                "Integer",
                ">=",
                FilterValue.Number("4")));

            var syncGeneric = options.BuildCondition<TestEntity>();
            var syncRuntime = options.BuildCondition(typeof(TestEntity));
            var asyncGeneric = await options.BuildConditionAsync<TestEntity>();
            var asyncRuntime = await options.BuildConditionAsync(typeof(TestEntity));

            foreach (var condition in new[] { syncGeneric, asyncGeneric })
                AssertConditionPredicateOrder(condition.Predicates);
            foreach (var condition in new[] { syncRuntime, asyncRuntime })
                AssertConditionPredicateOrder(condition.Predicates);
        }

        [Fact]
        public async Task InvalidTreeMakesMixedConditionAndOrderingUnavailable()
        {
            var options = new ConditionOptions
            {
                Filters = new[] { NewFilter("Integer", "=", "1") },
                Where = "Integer = 2",
                FilterTree = FilterNode.Condition("Missing", "=", FilterValue.Number("3")),
                OrderBy = "Integer desc"
            };

            var sync = options.BuildCondition<TestEntity>();
            var async = await options.BuildConditionAsync<TestEntity>();

            Assert.False(sync.IsValid);
            Assert.Null(sync.Predicates);
            Assert.Null(sync.OrderByClause);
            Assert.Contains(sync.Error.EvaluationResult.Diagnostics, diagnostic =>
                diagnostic.Code == "unknown-property" &&
                diagnostic.InputPath == "/FilterTree/field");
            Assert.False(async.IsValid);
            Assert.Null(async.Predicates);
            Assert.Null(async.OrderByClause);
        }

        [Fact]
        public void NullTreeIsOmittedFromConditionOptionsJson()
        {
            var payload = JsonSerializer.Serialize(new ConditionOptions { Where = "Integer = 1" });

            Assert.DoesNotContain("\"FilterTree\"", payload);
            Assert.Contains("\"Where\":\"Integer = 1\"", payload);
        }

        [Fact]
        public void PolicyContextAggregatesTreeWithWhereAndLegacyConditionsBeforeResolution()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy(maxAtomicConditions: 2));
            var resolver = new CountingResolver();
            var options = new ConditionOptions
            {
                Where = "Integer = $value",
                Filters = new[] { NewFilter("Integer", "=", "2") },
                FilterTree = FilterNode.Condition(
                    "Integer",
                    "=",
                    FilterValue.Number("3")),
                OrderBy = "Integer"
            };

            var condition = context.BuildCondition(options, resolver);

            Assert.False(condition.IsValid);
            Assert.Null(condition.Predicates);
            Assert.Null(condition.OrderByClause);
            var diagnostic = Assert.Single(condition.Error.EvaluationResult.Diagnostics);
            Assert.Equal("query-policy-condition-count-exceeded", diagnostic.Code);
            Assert.Equal("/FilterTree/op", diagnostic.InputPath);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        [Fact]
        public async Task AsyncPolicyContextAggregatesTreeTextAndConditionBudgets()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy(maxExpressionLength: 20));
            var resolver = new CountingResolver();
            var options = new ConditionOptions
            {
                Where = "Integer = $value",
                FilterTree = FilterNode.Condition(
                    "Integer",
                    "=",
                    FilterValue.String("2"))
            };

            var condition = await context.BuildConditionAsync(options, resolver);

            Assert.False(condition.IsValid);
            Assert.Null(condition.Predicates);
            var diagnostic = Assert.Single(condition.Error.EvaluationResult.Diagnostics);
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal("/FilterTree/value", diagnostic.InputPath);
            Assert.Equal(25, diagnostic.ObservedValue);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        private static ConditionOptions CreateOptions(FilterNode tree)
        {
            return new ConditionOptions
            {
                Filters = new[] { NewFilter("Integer", "=", "1") },
                FilterGroups = new[]
                {
                    new FilterGroup
                    {
                        Filters = new List<Filter> { NewFilter("Integer", "=", "2") }
                    }
                },
                Where = "Integer = 3",
                FilterTree = tree
            };
        }

        private static Filter NewFilter(string property, string @operator, string value)
            => new Filter { Property = property, Operator = @operator, Value = value };

        private static void AssertConditionPredicateOrder(IEnumerable<System.Linq.Expressions.LambdaExpression> predicates)
        {
            var ordered = predicates.ToArray();
            Assert.Equal(4, ordered.Length);
            Assert.Contains("== 1", ordered[0].ToString());
            Assert.Contains("== 2", ordered[1].ToString());
            Assert.Contains("== 3", ordered[2].ToString());
            Assert.Contains(">= 4", ordered[3].ToString());
        }

        private sealed class CountingResolver : VariableResolver
        {
            internal int ResolveCalls;

            protected override System.Threading.Tasks.Task<VariableInfo> TryResolveCore(
                string name,
                System.Threading.CancellationToken cancellationToken)
            {
                ResolveCalls++;
                return base.TryResolveCore(name, cancellationToken);
            }
        }
    }
}
