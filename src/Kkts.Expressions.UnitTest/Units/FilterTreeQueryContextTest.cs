using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreeQueryContextTest
    {
        [Fact]
        public void ContextValidationAndConstructionEnforceTreeDepthAndConditionBudgets()
        {
            var policy = new QueryPolicy(maxAtomicConditions: 4)
                .WithMaxFilterTreeDepth(1);
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<QueryEntity>(), policy);
            var allowed = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Number("1"))
            });
            var tooDeep = FilterNode.And(new[]
            {
                FilterNode.Not(FilterNode.Condition("Id", "=", FilterValue.Number("1")))
            });

            Assert.True(context.ValidateFilterTree(allowed).Succeeded);
            var failed = context.TryBuildPredicate(tooDeep);
            Assert.False(failed.Succeeded);
            Assert.Null(failed.Result);
            Assert.Contains(failed.Diagnostics, diagnostic =>
                diagnostic.Code == "query-policy-filter-tree-depth-exceeded" &&
                diagnostic.InputPath == "/and/0/not");
            Assert.Throws<QueryPolicyException>(() => context.BuildPredicate(tooDeep));

            var wrongType = Assert.Throws<ArgumentException>(() => context.BuildPredicate<OtherEntity>(allowed));
            Assert.Equal("entityType", wrongType.ParamName);
        }

        [Fact]
        public void StaticPolicyFailuresPreventVariableResolution()
        {
            var resolver = new CountingResolver();
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxAtomicConditions: 1));
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Variable("id")),
                FilterNode.Condition("Secret", "=", FilterValue.Number("1"))
            });

            var result = context.TryBuildPredicate(tree, resolver);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal(0, resolver.ResolutionCalls);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "query-policy-condition-count-exceeded");
        }

        [Fact]
        public void ContextBuilderEnforcesResolvedCollectionLimitAndDisposesEnumerator()
        {
            var sequence = new CountingEnumerable(infinite: true);
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("ids", sequence));
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxInItems: 2));
            var tree = FilterNode.Condition("Id", "in", FilterValue.Variable("ids"));

            var result = context.TryBuildPredicate(tree, resolver);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-in-items-exceeded", diagnostic.Code);
            Assert.Equal("/value", diagnostic.InputPath);
            Assert.Equal(3, sequence.MoveNextCalls);
            Assert.True(sequence.Disposed);
        }

        [Fact]
        public void ContextBuilderEnumeratesAcceptedResolvedCollectionOnce()
        {
            var sequence = new CountingEnumerable(infinite: false, values: new[] { 1, 3 });
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("ids", sequence));
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxInItems: 2));
            var tree = FilterNode.Condition("Id", "in", FilterValue.Variable("ids"));

            var result = context.TryBuildPredicate<QueryEntity>(tree, resolver);

            Assert.True(result.Succeeded);
            Assert.Equal(1, sequence.EnumeratorCalls);
            Assert.Equal(3, sequence.MoveNextCalls);
            Assert.True(sequence.Disposed);
            Assert.True(result.Result.Compile()(new QueryEntity { Id = 3 }));
        }

        [Fact]
        public void ContextBuilderRejectsQueryableMembershipWhenSizeCannotBeVerified()
        {
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("ids", new[] { 1, 2, 3 }.AsQueryable()));
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxInItems: 2));

            var result = context.TryBuildPredicate(
                FilterNode.Condition("Id", "in", FilterValue.Variable("ids")),
                resolver);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-in-items-unverifiable", Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void PolicyAwareJsonIngressEnforcesDepthAndMembershipBeforeCompletingTree()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxInItems: 2).WithMaxFilterTreeDepth(1));

            var tooDeep = FilterTreeJson.TryDeserialize(
                "{\"and\":[{\"not\":{\"field\":\"Id\",\"op\":\"=\",\"value\":1}}]}",
                context);
            Assert.False(tooDeep.Succeeded);
            Assert.Null(tooDeep.Result);
            var depthDiagnostic = Assert.Single(tooDeep.Diagnostics);
            Assert.Equal("query-policy-filter-tree-depth-exceeded", depthDiagnostic.Code);
            Assert.Equal("/and/0/not", depthDiagnostic.InputPath);

            var tooManyItems = FilterTreeJson.TryDeserialize(
                "{\"field\":\"Id\",\"op\":\"in\",\"value\":[1,2,3]}",
                context);
            Assert.False(tooManyItems.Succeeded);
            Assert.Null(tooManyItems.Result);
            var itemDiagnostic = Assert.Single(tooManyItems.Diagnostics);
            Assert.Equal("query-policy-in-items-exceeded", itemDiagnostic.Code);
            Assert.Equal("/value/2", itemDiagnostic.InputPath);
            Assert.Equal(3, itemDiagnostic.ObservedValue);
        }

        [Fact]
        public void PolicyAwareJsonIngressPerformsSchemaAndVariableValidation()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy());
            var undeclared = FilterTreeJson.TryDeserialize(
                "{\"field\":\"Id\",\"op\":\"=\",\"value\":{\"variable\":\"id\"}}",
                context);
            Assert.False(undeclared.Succeeded);
            Assert.Null(undeclared.Result);
            Assert.Equal("undeclared-variable", Assert.Single(undeclared.Diagnostics).Code);

            var declared = FilterTreeJson.TryDeserialize(
                "{\"field\":\"Id\",\"op\":\"=\",\"value\":{\"variable\":\"id\"}}",
                context,
                new ExpressionVariableSchema(new[]
                {
                    new ExpressionVariableDefinition("id", typeof(int))
                }));
            Assert.True(declared.Succeeded);
        }

        [Fact]
        public void PolicyAwareJsonIngressCountsLeafTextAndAggregateConditions()
        {
            var textContext = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxExpressionLength: 5));
            var tooLong = FilterTreeJson.TryDeserialize(
                "{\"field\":\"Id\",\"op\":\"=\",\"value\":123}",
                textContext);
            Assert.False(tooLong.Succeeded);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(tooLong.Diagnostics).Code);
            Assert.Equal("/value", tooLong.Diagnostics[0].InputPath);
            Assert.Equal(6, tooLong.Diagnostics[0].ObservedValue);
            Assert.Equal(0, tooLong.Diagnostics[0].Start);
            Assert.Equal(0, tooLong.Diagnostics[0].Length);

            var conditionContext = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxAtomicConditions: 1));
            var tooMany = FilterTreeJson.TryDeserialize(
                "{\"and\":[{\"field\":\"Id\",\"op\":\"=\",\"value\":1},{\"field\":\"Id\",\"op\":\"=\",\"value\":2}]}",
                conditionContext);
            Assert.False(tooMany.Succeeded);
            Assert.Equal("query-policy-condition-count-exceeded", Assert.Single(tooMany.Diagnostics).Code);
            Assert.Equal("/and/1/field", tooMany.Diagnostics[0].InputPath);
        }

        [Fact]
        public async Task AsyncContextBuilderCancelsWhileSnapshottingAndReturnsNoPartialResult()
        {
            using var cancellation = new CancellationTokenSource();
            var sequence = new CountingEnumerable(
                infinite: true,
                cancelAfterMove: cancellation);
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("ids", sequence));
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxInItems: 20));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                context.TryBuildPredicateAsync(
                    FilterNode.Condition("Id", "in", FilterValue.Variable("ids")),
                    resolver,
                    cancellation.Token));
            Assert.True(sequence.Disposed);
        }

        [Fact]
        public async Task GenericRuntimeAndSyncAsyncContextBuildsHaveParity()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                QueryPolicy.Recommended);
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", ">=", FilterValue.Number("2")),
                FilterNode.Not(FilterNode.Condition("Secret", "=", FilterValue.Number("1")))
            });

            var genericSync = context.TryBuildPredicate<QueryEntity>(tree);
            var runtimeSync = context.TryBuildPredicate(tree);
            var genericAsync = await context.TryBuildPredicateAsync<QueryEntity>(tree);
            var runtimeAsync = await context.TryBuildPredicateAsync(tree);

            Assert.True(genericSync.Succeeded);
            Assert.True(runtimeSync.Succeeded);
            Assert.True(genericAsync.Succeeded);
            Assert.True(runtimeAsync.Succeeded);
            var entity = new QueryEntity { Id = 3, Secret = 0 };
            Assert.True(genericSync.Result.Compile()(entity));
            Assert.True(((Func<QueryEntity, bool>)runtimeSync.Result.Compile())(entity));
            Assert.True(genericAsync.Result.Compile()(entity));
            Assert.True(((Func<QueryEntity, bool>)runtimeAsync.Result.Compile())(entity));
        }

        [Fact]
        public void ContextValidationCapsSemanticDiagnosticsAtThirtyTwo()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy());
            var tree = FilterNode.And(Enumerable.Range(0, 40)
                .Select(index => FilterNode.Condition(
                    "Missing" + index,
                    "=",
                    FilterValue.Number("1"))));

            var result = context.ValidateFilterTree(tree);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal(32, result.Diagnostics.Count);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "query-policy-diagnostics-truncated");
            Assert.True(result.IsTruncated);
        }

        private sealed class QueryEntity
        {
            public int Id { get; set; }
            public int Secret { get; set; }
        }

        private sealed class OtherEntity
        {
            public int Id { get; set; }
        }

        private sealed class CountingResolver : VariableResolver
        {
            public int ResolutionCalls { get; private set; }

            protected override Task<VariableInfo> TryResolveCore(
                string name,
                CancellationToken cancellationToken)
            {
                ResolutionCalls++;
                return Task.FromResult(new VariableInfo { Name = name });
            }
        }

        private sealed class CountingEnumerable : IEnumerable<int>
        {
            private readonly bool _infinite;
            private readonly int[] _values;
            private readonly CancellationTokenSource _cancelAfterMove;

            internal CountingEnumerable(
                bool infinite,
                int[] values = null,
                CancellationTokenSource cancelAfterMove = null)
            {
                _infinite = infinite;
                _values = values ?? Array.Empty<int>();
                _cancelAfterMove = cancelAfterMove;
            }

            public int EnumeratorCalls { get; private set; }
            public int MoveNextCalls { get; private set; }
            public bool Disposed { get; private set; }

            public IEnumerator<int> GetEnumerator()
            {
                EnumeratorCalls++;
                return new CountingEnumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class CountingEnumerator : IEnumerator<int>
            {
                private readonly CountingEnumerable _owner;
                private int _index;

                internal CountingEnumerator(CountingEnumerable owner) => _owner = owner;

                public int Current => _owner._infinite
                    ? _index - 1
                    : _owner._values[_index - 1];

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    _owner.MoveNextCalls++;
                    _index++;
                    if (_owner._cancelAfterMove != null && _owner.MoveNextCalls == 1)
                        _owner._cancelAfterMove.Cancel();
                    return _owner._infinite || _index <= _owner._values.Length;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose() => _owner.Disposed = true;
            }
        }
    }
}
