using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyRuntimeTest
    {
        [Fact]
        public void ParsePredicate_RejectsPolicyFailureWithoutReturningPredicateOrResolvingVariables()
        {
            var context = CreateContext(maxExpressionLength: 100);
            var resolver = new CountingResolver();
            const string expression = "  Price > $value";

            var editor = context.AnalyzeExpression(expression);
            var runtime = context.ParsePredicate<RuntimeEntity>(expression, resolver);

            Assert.False(runtime.Succeeded);
            Assert.Null(runtime.Result);
            var failure = Assert.IsType<QueryPolicyException>(runtime.Exception);
            var runtimeDiagnostic = Assert.Single(runtime.Diagnostics);
            Assert.Equal("query-policy-operator-denied", runtimeDiagnostic.Code);
            Assert.Equal(
                editor.Diagnostics.Single(diagnostic => diagnostic.Code == runtimeDiagnostic.Code).Start,
                runtimeDiagnostic.Start);
            Assert.Single(failure.Diagnostics);
            Assert.Equal(0, resolver.ResolveCalls);
        }

        [Fact]
        public void ParsePredicate_ValidatesActualServerInputAndPreservesAllowedVariableBehavior()
        {
            var context = CreateContext(maxExpressionLength: 100);
            var resolver = new CountingResolver();
            var allowed = context.ParsePredicate<RuntimeEntity>("Price = $value", resolver);

            Assert.True(allowed.Succeeded);
            Assert.NotNull(allowed.Result);
            Assert.Equal(1, resolver.ResolveCalls);
            Assert.True(allowed.Result.Compile()(new RuntimeEntity { Price = 5 }));

            var changedInput = context.ParsePredicate<RuntimeEntity>("Price > 1", resolver);
            Assert.False(changedInput.Succeeded);
            Assert.Null(changedInput.Result);
            Assert.Equal("query-policy-operator-denied", Assert.Single(changedInput.Diagnostics).Code);
        }

        [Fact]
        public void ParsePredicate_EnforcesSourceLengthBeforeRuntimeParserAndUsesOriginalOffsets()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(maxExpressionLength: 8));

            var result = context.ParsePredicate<RuntimeEntity>("Price = 1 ");

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal(8, diagnostic.Start);
            Assert.Equal(10, diagnostic.ObservedValue);
            Assert.IsType<QueryPolicyException>(result.Exception);
        }

        [Fact]
        public void ParsePredicate_UsesEditorDiagnosticsForEveryStaticBudget()
        {
            var cases = new[]
            {
                (new QueryPolicy(maxParenthesisDepth: 1), "((Price = 1)"),
                (new QueryPolicy(maxAtomicConditions: 1), "Price = 1 AND Price = 2"),
                (new QueryPolicy(maxInItems: 1), "Price in [1, 2]")
            };

            foreach (var (policy, expression) in cases)
            {
                var context = new ExpressionQueryContext(ExpressionSchema.FromType<RuntimeEntity>(), policy);
                var editorDiagnostic = Assert.Single(context.AnalyzeExpression(expression).Diagnostics);
                var runtime = context.ParsePredicate<RuntimeEntity>(expression);
                var runtimeDiagnostic = Assert.Single(runtime.Diagnostics);

                Assert.Equal(editorDiagnostic.Code, runtimeDiagnostic.Code);
                Assert.Equal(editorDiagnostic.Start, runtimeDiagnostic.Start);
                Assert.Equal(editorDiagnostic.Length, runtimeDiagnostic.Length);
                Assert.Equal(editorDiagnostic.ConfiguredLimit, runtimeDiagnostic.ConfiguredLimit);
                Assert.Equal(editorDiagnostic.ObservedValue, runtimeDiagnostic.ObservedValue);
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_EnforcesPolicyAndPreservesCancellation()
        {
            var context = CreateContext(maxExpressionLength: 100);
            var denied = await context.ParsePredicateAsync<RuntimeEntity>("Price > 1");

            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("query-policy-operator-denied", Assert.Single(denied.Diagnostics).Code);

            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    context.ParsePredicateAsync<RuntimeEntity>("Price = 1", cancellationToken: source.Token));
            }
        }

        [Fact]
        public void RuntimeTypeParsePredicate_UsesSchemaEntityTypeAndRejectsMismatches()
        {
            var context = CreateContext(maxExpressionLength: 100);

            Assert.True(context.ParsePredicate("Price = 1").Succeeded);
            Assert.Throws<ArgumentException>(() => context.ParsePredicate<OtherEntity>("Price = 1"));
        }

        [Fact]
        public async Task Contexts_IsolatePolicyStateAcrossConcurrentOperations()
        {
            var equalContext = new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(
                    maxAtomicConditions: 1,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.Equal }
                    }));
            var greaterContext = new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(
                    maxAtomicConditions: 1,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.GreaterThan }
                    }));

            var operations = Enumerable.Range(0, 40)
                .Select(index => index % 2 == 0
                    ? equalContext.ParsePredicateAsync<RuntimeEntity>("Price = 1")
                    : greaterContext.ParsePredicateAsync<RuntimeEntity>("Price > 1"));
            var results = await Task.WhenAll(operations);

            Assert.All(results, result => Assert.True(result.Succeeded));
            Assert.True(equalContext.ParsePredicate<RuntimeEntity>("Price > 1").Diagnostics.Count > 0);
            Assert.True(greaterContext.ParsePredicate<RuntimeEntity>("Price = 1").Diagnostics.Count > 0);
        }

        [Fact]
        public void DirectPredicateBuilders_EnforceCanonicalFieldAndOperatorRules()
        {
            var schema = ExpressionSchema.FromType<RuntimeEntity>(
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["cost"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
                    ["Price"] = new[] { ComparisonOperator.Equal }
                }));

            var allowed = context.TryBuildPredicate<RuntimeEntity>("cost", ComparisonOperator.Equal, 5m);
            Assert.True(allowed.Succeeded);
            Assert.True(allowed.Result.Compile()(new RuntimeEntity { Price = 5m }));

            var denied = context.TryBuildPredicate<RuntimeEntity>("Price", ComparisonOperator.GreaterThan, 1m);
            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("query-policy-operator-denied", Assert.Single(denied.Diagnostics).Code);
            Assert.Throws<QueryPolicyException>(() =>
                context.BuildPredicate<RuntimeEntity>("cost", ComparisonOperator.GreaterThan, 1m));
        }

        [Fact]
        public void DirectPredicateBuilders_CheckInputLengthWithoutCallingArbitraryToString()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(maxExpressionLength: 6));
            var tooLong = context.TryBuildPredicate<RuntimeEntity>("Price", ComparisonOperator.Equal, "123");

            Assert.False(tooLong.Succeeded);
            Assert.Null(tooLong.Result);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(tooLong.Diagnostics).Code);
            Assert.Equal("Value", tooLong.Diagnostics[0].InputPath);

            var value = new ToStringProbe();
            var unsupported = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.Equal,
                value);
            Assert.False(unsupported.Succeeded);
            Assert.Equal(0, value.ToStringCalls);
        }

        [Fact]
        public async Task DirectPredicateBuilders_AsyncSurfaceMatchesSynchronousPolicyFailure()
        {
            var context = CreateContext(maxExpressionLength: 100);

            var sync = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.GreaterThan,
                1m);
            var asyncResult = await context.TryBuildPredicateAsync<RuntimeEntity>(
                "Price",
                ComparisonOperator.GreaterThan,
                1m);

            Assert.False(asyncResult.Succeeded);
            Assert.Null(asyncResult.Result);
            Assert.Equal(sync.Diagnostics[0].Code, asyncResult.Diagnostics[0].Code);
            Assert.Equal(sync.Diagnostics[0].InputPath, asyncResult.Diagnostics[0].InputPath);
        }

        [Fact]
        public void ParsePredicate_SnapshotsMembershipVariablesWithinTheConfiguredBound()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var sequence = new SinglePassSequence(1m, 2m);
            var resolver = new SequenceResolver(sequence);

            var result = context.ParsePredicate<RuntimeEntity>("Price in $items", resolver);

            Assert.True(result.Succeeded);
            Assert.Equal(1, resolver.ResolveCalls);
            Assert.Equal(1, sequence.EnumerationCalls);
            Assert.Equal(3, sequence.MoveNextCalls);
            Assert.Equal(2, sequence.CurrentReads);
            Assert.Equal(1, sequence.DisposeCalls);
            Assert.True(result.Result.Compile()(new RuntimeEntity { Price = 2m }));
            Assert.Equal(1, sequence.EnumerationCalls);
        }

        [Fact]
        public void ParsePredicate_StopsMembershipEnumerationAtLimitPlusOne()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var sequence = new SinglePassSequence(1m, 2m, 3m, 4m);

            var result = context.ParsePredicate<RuntimeEntity>(
                "Price in $items",
                new SequenceResolver(sequence));

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-in-items-exceeded", diagnostic.Code);
            Assert.Equal(2, diagnostic.ConfiguredLimit);
            Assert.Equal(3, diagnostic.ObservedValue);
            Assert.True(diagnostic.ObservedValueIsLowerBound);
            Assert.Equal("Price in $items".IndexOf("$items", StringComparison.Ordinal), diagnostic.Start);
            Assert.Equal("$items".Length, diagnostic.Length);
            Assert.Equal(3, sequence.MoveNextCalls);
            Assert.Equal(2, sequence.CurrentReads);
            Assert.Equal(1, sequence.DisposeCalls);
        }

        [Fact]
        public void ParsePredicate_CountsDuplicateAndNullVariableItems()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<NullableRuntimeEntity>(),
                new QueryPolicy(
                    maxInItems: 2,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.In }
                    }));
            var values = new decimal?[] { null, null };

            var result = context.ParsePredicate<NullableRuntimeEntity>(
                "Price in $items",
                new SequenceResolver(values));

            Assert.True(result.Succeeded);
            Assert.True(result.Result.Compile()(new NullableRuntimeEntity { Price = null }));
            Assert.False(result.Result.Compile()(new NullableRuntimeEntity { Price = 0m }));
        }

        [Fact]
        public void ParsePredicate_StopsAnInfiniteMembershipSequenceAtLimitPlusOne()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var sequence = new InfiniteSequence();

            var result = context.ParsePredicate<RuntimeEntity>(
                "Price in $items",
                new SequenceResolver(sequence));

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(result.Diagnostics).Code);
            Assert.Equal(3, sequence.MoveNextCalls);
            Assert.Equal(1, sequence.DisposeCalls);
        }

        [Fact]
        public void ParsePredicate_BoundsMembershipAfterArithmeticLeftOperand()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<ArithmeticRuntimeEntity>(),
                new QueryPolicy(
                    maxInItems: 1,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.In },
                        ["Discount"] = new[] { ComparisonOperator.In }
                    }));
            var result = context.ParsePredicate<ArithmeticRuntimeEntity>(
                "Price + Discount in $items",
                new SequenceResolver(new[] { 3m }));

            Assert.True(result.Succeeded);
            Assert.True(result.Result.Compile()(new ArithmeticRuntimeEntity { Price = 1m, Discount = 2m }));
        }

        [Fact]
        public void ParsePredicate_RejectsQueryableMembershipWithoutEnumeratingIt()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var query = Enumerable.Range(0, 3)
                .Select<int, decimal>(_ => throw new InvalidOperationException("Must not enumerate query-backed values."))
                .AsQueryable();

            var result = context.ParsePredicate<RuntimeEntity>(
                "Price in $items",
                new SequenceResolver(query));

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-in-items-unverifiable", Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void DirectMembershipBuilders_CountListItemsBeforeDeduplication()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var withinLimit = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                "1, 1");
            var overLimit = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                "1, 1, 1");

            Assert.True(withinLimit.Succeeded);
            Assert.False(overLimit.Succeeded);
            Assert.Null(overLimit.Result);
            var diagnostic = Assert.Single(overLimit.Diagnostics);
            Assert.Equal("query-policy-in-items-exceeded", diagnostic.Code);
            Assert.Equal(2, diagnostic.ConfiguredLimit);
            Assert.Equal(3, diagnostic.ObservedValue);
            Assert.True(diagnostic.ObservedValueIsLowerBound);
            Assert.Equal("Value", diagnostic.InputPath);
        }

        [Fact]
        public void DirectMembershipBuilders_BoundAndSnapshotEnumerableValues()
        {
            var context = CreateMembershipContext(maxItems: 2);
            var acceptedSequence = new SinglePassSequence(1m, 2m);
            var accepted = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                acceptedSequence);

            Assert.True(accepted.Succeeded);
            Assert.True(accepted.Result.Compile()(new RuntimeEntity { Price = 2m }));
            Assert.Equal(1, acceptedSequence.EnumerationCalls);

            var rejectedSequence = new SinglePassSequence(1m, 2m, 3m, 4m);
            var rejected = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                rejectedSequence);

            Assert.False(rejected.Succeeded);
            Assert.Null(rejected.Result);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(rejected.Diagnostics).Code);
            Assert.Equal(3, rejectedSequence.MoveNextCalls);
            Assert.Equal(1, rejectedSequence.EnumerationCalls);

            var mutable = new List<decimal> { 1m };
            var mutableResult = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                mutable);
            mutable[0] = 2m;
            Assert.True(mutableResult.Succeeded);
            Assert.True(mutableResult.Result.Compile()(new RuntimeEntity { Price = 1m }));
            Assert.False(mutableResult.Result.Compile()(new RuntimeEntity { Price = 2m }));
        }

        [Fact]
        public void ParsePredicate_UnlimitedMembershipKeepsExistingEnumerableBehavior()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Price"] = new[] { ComparisonOperator.In }
                }));
            var result = context.ParsePredicate<RuntimeEntity>(
                "Price in $items",
                new SequenceResolver(new[] { 1m, 2m }));

            Assert.True(result.Succeeded);
            Assert.True(result.Result.Compile()(new RuntimeEntity { Price = 2m }));
        }

        [Fact]
        public void DirectMembershipBuilders_AllowEmptyCollectionAtZeroLimit()
        {
            var context = CreateMembershipContext(maxItems: 0);
            var empty = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                string.Empty);
            var oneItem = context.TryBuildPredicate<RuntimeEntity>(
                "Price",
                ComparisonOperator.In,
                "1");

            Assert.True(empty.Succeeded);
            Assert.False(oneItem.Succeeded);
            Assert.Equal("query-policy-in-items-exceeded", Assert.Single(oneItem.Diagnostics).Code);
        }

        private static ExpressionQueryContext CreateContext(int maxExpressionLength)
        {
            return new ExpressionQueryContext(
                ExpressionSchema.FromType<RuntimeEntity>(),
                new QueryPolicy(
                    maxExpressionLength: maxExpressionLength,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.Equal }
                    }));
        }

        private static ExpressionQueryContext CreateMembershipContext(int maxItems)
        {
            return new ExpressionQueryContext(
                    ExpressionSchema.FromType<RuntimeEntity>(),
                    new QueryPolicy(
                        maxInItems: maxItems,
                        allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                        {
                            ["Price"] = new[] { ComparisonOperator.In }
                        }));
        }

        private sealed class RuntimeEntity
        {
            public decimal Price { get; set; }
        }

        private sealed class OtherEntity
        {
            public int Id { get; set; }
        }

        private sealed class NullableRuntimeEntity
        {
            public decimal? Price { get; set; }
        }

        private sealed class ArithmeticRuntimeEntity
        {
            public decimal Price { get; set; }

            public decimal Discount { get; set; }
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
                    Value = 5m
                });
            }
        }

        private sealed class ToStringProbe
        {
            internal int ToStringCalls;

            public override string ToString()
            {
                ++ToStringCalls;
                return "untrusted";
            }
        }

        private sealed class SequenceResolver : VariableResolver
        {
            private readonly object _value;

            internal SequenceResolver(object value)
            {
                _value = value;
            }

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
                    Value = _value
                });
            }
        }

        private sealed class SinglePassSequence : IEnumerable<decimal>
        {
            private readonly decimal[] _values;

            internal SinglePassSequence(params decimal[] values)
            {
                _values = values;
            }

            internal int EnumerationCalls;
            internal int MoveNextCalls;
            internal int CurrentReads;
            internal int DisposeCalls;

            public IEnumerator<decimal> GetEnumerator()
            {
                ++EnumerationCalls;
                if (EnumerationCalls > 1)
                    throw new InvalidOperationException("The membership source is single-pass.");
                return new SequenceEnumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class SequenceEnumerator : IEnumerator<decimal>
            {
                private readonly SinglePassSequence _owner;
                private int _index = -1;

                internal SequenceEnumerator(SinglePassSequence owner) => _owner = owner;

                public decimal Current
                {
                    get
                    {
                        ++_owner.CurrentReads;
                        return _owner._values[_index];
                    }
                }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    ++_owner.MoveNextCalls;
                    ++_index;
                    return _index < _owner._values.Length;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose() => ++_owner.DisposeCalls;
            }
        }

        private sealed class InfiniteSequence : IEnumerable<decimal>
        {
            internal int MoveNextCalls;
            internal int DisposeCalls;

            public IEnumerator<decimal> GetEnumerator() => new InfiniteEnumerator(this);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class InfiniteEnumerator : IEnumerator<decimal>
            {
                private readonly InfiniteSequence _owner;

                internal InfiniteEnumerator(InfiniteSequence owner) => _owner = owner;

                public decimal Current => 1m;

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    ++_owner.MoveNextCalls;
                    return true;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose() => ++_owner.DisposeCalls;
            }
        }
    }
}
