using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreeBuildApiTest
    {
        [Fact]
        public void LiteralPrefixStringsAreNotResolvedButExplicitReferencesAre()
        {
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("id", 7));
            var literal = FilterNode.Condition("Name", "=", FilterValue.String("$id"));
            var reference = FilterNode.Condition("Id", "=", FilterValue.Variable("id"));

            Assert.True(literal.BuildPredicate<FilterEntity>(resolver)
                .Compile()(new FilterEntity { Name = "$id" }));
            Assert.True(reference.BuildPredicate<FilterEntity>(resolver)
                .Compile()(new FilterEntity { Id = 7 }));
        }

        [Fact]
        public void NestedFilterGuidePredicateExampleCompilesAndExecutes()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("userId", 42);
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Status", "=", FilterValue.String("Active")),
                FilterNode.Condition("OwnerId", "=", FilterValue.Variable("userId"))
            });

            var attempt = tree.TryBuildPredicate<Order>(resolver);
            Assert.True(attempt.Succeeded);
            Func<Order, bool> predicate = attempt.Result.Compile();
            Assert.True(predicate(new Order { Status = "Active", OwnerId = 42 }));
            Assert.False(predicate(new Order { Status = "Inactive", OwnerId = 42 }));
        }

        [Fact]
        public void ScalarListAndWholeCollectionReferencesResolveOnlyAtBuildTime()
        {
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("first", 2));
            Assert.True(resolver.TryAdd("ids", new[] { 3, 5 }));
            var scalarList = FilterNode.Condition(
                "Id",
                "in",
                FilterValue.Collection(new[]
                {
                    FilterValue.Variable("first"),
                    FilterValue.Number("9")
                }));
            var collection = FilterNode.Condition(
                "Id",
                "in",
                FilterValue.Variable("ids"));

            Assert.True(scalarList.BuildPredicate<FilterEntity>(resolver)
                .Compile()(new FilterEntity { Id = 2 }));
            Assert.False(scalarList.BuildPredicate<FilterEntity>(resolver)
                .Compile()(new FilterEntity { Id = 3 }));
            Assert.True(collection.BuildPredicate<FilterEntity>(resolver)
                .Compile()(new FilterEntity { Id = 5 }));
        }

        [Fact]
        public async Task AsyncBuilderResolvesReferencesAndPropagatesCancellation()
        {
            var resolver = new DeferredResolver();
            var tree = FilterNode.Condition("Id", "=", FilterValue.Variable("id"));
            var predicate = await tree.BuildPredicateAsync<FilterEntity>(resolver);

            Assert.True(predicate.Compile()(new FilterEntity { Id = 11 }));
            Assert.Equal(new[] { "id" }, resolver.Requests);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                tree.TryBuildPredicateAsync<FilterEntity>(resolver, cancellationToken: cancellation.Token));
        }

        [Fact]
        public void FailedResolutionOrConversionReturnsNoPredicateAndPointsToValue()
        {
            var unavailable = FilterNode.Condition("Id", "=", FilterValue.Variable("missing"))
                .TryBuildPredicate<FilterEntity>();
            Assert.False(unavailable.Succeeded);
            Assert.Null(unavailable.Result);
            Assert.Contains(unavailable.Diagnostics, diagnostic =>
                diagnostic.Code == "filter-tree-variable-unavailable" &&
                diagnostic.InputPath == "/value");

            var incompatible = FilterNode.Condition("Id", "=", FilterValue.String("not-a-number"))
                .TryBuildPredicate<FilterEntity>();
            Assert.False(incompatible.Succeeded);
            Assert.Null(incompatible.Result);
            Assert.NotEmpty(incompatible.Diagnostics);
        }

        [Fact]
        public async Task RuntimeTypeOverloadsReturnLambdasAndEvaluationDiagnostics()
        {
            var validTree = FilterNode.Condition("Id", "=", FilterValue.Number("3"));
            var runtimePredicate = validTree.BuildPredicate(typeof(FilterEntity));
            Assert.True(((Func<FilterEntity, bool>)runtimePredicate.Compile())(
                new FilterEntity { Id = 3 }));

            var invalidTree = FilterNode.Condition("Missing", "=", FilterValue.Number("3"));
            var evaluation = invalidTree.TryBuildPredicate(typeof(FilterEntity));
            Assert.False(evaluation.Succeeded);
            Assert.Null(evaluation.Result);
            Assert.NotEmpty(evaluation.Diagnostics);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                invalidTree.BuildPredicateAsync(typeof(FilterEntity)));
        }

        [Fact]
        public void StaticFieldValidationHappensBeforeResolverExecution()
        {
            var resolver = new DeferredResolver();
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Variable("id")),
                FilterNode.Condition("Missing", "=", FilterValue.Number("1"))
            });

            var result = tree.TryBuildPredicate<FilterEntity>(resolver);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Empty(resolver.Requests);
        }

        [Fact]
        public void RuntimeVariableTypeAndCollectionFailuresDoNotReturnPartialPredicates()
        {
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("id", "not-a-number"));
            Assert.True(resolver.TryAdd("ids", new object[] { 1, "not-a-number" }));

            var scalar = FilterNode.Condition("Id", "=", FilterValue.Variable("id"))
                .TryBuildPredicate<FilterEntity>(resolver);
            var collection = FilterNode.Condition("Id", "in", FilterValue.Variable("ids"))
                .TryBuildPredicate<FilterEntity>(resolver);

            Assert.False(scalar.Succeeded);
            Assert.Null(scalar.Result);
            Assert.Contains(scalar.Diagnostics, diagnostic =>
                diagnostic.Code == "filter-tree-runtime-conversion");
            Assert.False(collection.Succeeded);
            Assert.Null(collection.Result);
            Assert.Contains(collection.Diagnostics, diagnostic =>
                diagnostic.Code == "filter-tree-runtime-conversion" &&
                diagnostic.InputPath == "/value/1");
        }

        private sealed class FilterEntity
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class Order
        {
            public string Status { get; set; }
            public int OwnerId { get; set; }
        }

        private sealed class DeferredResolver : VariableResolver
        {
            public string[] Requests => _requests.ToArray();

            private readonly System.Collections.Generic.List<string> _requests =
                new System.Collections.Generic.List<string>();

            protected override Task<VariableInfo> TryResolveCore(
                string name,
                CancellationToken cancellationToken)
            {
                _requests.Add(name);
                return Task.FromResult(new VariableInfo
                {
                    Name = name,
                    Resolved = name == "id",
                    Value = 11
                });
            }
        }
    }
}
