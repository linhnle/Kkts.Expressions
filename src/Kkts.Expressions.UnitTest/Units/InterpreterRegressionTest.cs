using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    [SuppressMessage("SonarAnalyzer.CSharp", "S6966", Justification = "Parity tests must exercise synchronous APIs alongside their async counterparts.")]
    public class InterpreterRegressionTest
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ParsePredicateAsync_ReturnsBeforeResolverCompletes(bool runtime)
        {
            using var source = new CancellationTokenSource();
            var resolver = new GatedResolver();
            var returned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var caller = Task.Run(async () =>
            {
                const string query = "(Integer = $amount) and Boolean = $flag";
                if (runtime)
                {
                    var parsing = Interpreter.ParsePredicateAsync(query, typeof(TestEntity), resolver, cancellationToken: source.Token);
                    returned.SetResult(true);
                    return (EvaluationResultBase)await parsing;
                }
                else
                {
                    var parsing = Interpreter.ParsePredicateAsync<TestEntity>(query, resolver, cancellationToken: source.Token);
                    returned.SetResult(true);
                    return await parsing;
                }
            });

            try
            {
                await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(caller.IsCompleted);
            }
            finally
            {
                resolver.Release.TrySetResult(true);
                await caller;
            }

            var result = await caller;
            Assert.True(result.Succeeded, result.Exception?.ToString());
            Assert.All(resolver.Tokens, token => Assert.Equal(source.Token, token));
        }

        [Theory]
        [InlineData("Integer = $amount")]
        [InlineData("(Integer = $amount)")]
        [InlineData("Integer = $amount and Boolean = $flag")]
        [InlineData("!(Integer = $other)")]
        [InlineData("not(Integer = $other)")]
        [InlineData("Integer in [$amount, $other]")]
        [InlineData("String.contains($text)")]
        [InlineData("Integer + 0 = $amount")]
        public async Task ParsePredicateAsync_AllNodeShapes_UseAsyncResolution(string query)
        {
            using var source = new CancellationTokenSource();
            var entity = new TestEntity { Integer = 4, Boolean = true, String = "ab" };
            var genericResolver = new YieldingResolver();
            var runtimeResolver = new YieldingResolver();
            var generic = await Interpreter.ParsePredicateAsync<TestEntity>(query, genericResolver, cancellationToken: source.Token);
            var runtime = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity), runtimeResolver, cancellationToken: source.Token);

            Assert.True(generic.Succeeded, generic.Exception?.ToString());
            Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
            Assert.True(generic.Result.Compile()(entity));
            Assert.True(((Expression<Func<TestEntity, bool>>)runtime.Result).Compile()(entity));
            foreach (var resolver in new[] { genericResolver, runtimeResolver })
            {
                Assert.NotEmpty(resolver.Tokens);
                Assert.All(resolver.Tokens, token => Assert.Equal(source.Token, token));
            }
        }

        [Theory]
        [InlineData("Integer = 4")]
        [InlineData("Integer = $amount")]
        [InlineData("Integer in [$amount]")]
        public async Task ParsePredicateAsync_PreCancelled_ReturnsFailure(string query)
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("amount", 4);
            var token = new CancellationToken(true);
            var generic = await Interpreter.ParsePredicateAsync<TestEntity>(query, resolver, cancellationToken: token);
            var runtime = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity), resolver, cancellationToken: token);
            foreach (var result in new EvaluationResultBase[] { generic, runtime })
            {
                Assert.False(result.Succeeded);
                Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
                Assert.Empty(result.InvalidValues);
            }
            Assert.Null(generic.Result);
            Assert.Null(runtime.Result);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.TryResolveAsync("amount", token));
        }

        [Fact]
        public async Task ParsePredicateAsync_CancellationDuringResolution_ReturnsFailure()
        {
            using var source = new CancellationTokenSource();
            var resolver = new GatedResolver();
            var parsing = Task.Run(() => Interpreter.ParsePredicateAsync<TestEntity>(
                "Integer = $amount", resolver, cancellationToken: source.Token));
            try
            {
                await resolver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                source.Cancel();
                var result = await parsing.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(result.Succeeded);
                Assert.Null(result.Result);
                Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
                Assert.Empty(result.InvalidValues);
            }
            finally
            {
                resolver.Release.TrySetResult(true);
                await parsing;
            }
        }

        [Theory]
        [InlineData("Integer")]
        [InlineData("String")]
        [InlineData("'literal'")]
        public async Task ParsePredicate_NonBoolean_AllEntryPointsReturnFailure(string query)
        {
            var generic = Interpreter.ParsePredicate<TestEntity>(query);
            var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
            foreach (var result in new EvaluationResultBase[] { generic, genericAsync, runtime, runtimeAsync })
            {
                Assert.False(result.Succeeded);
                Assert.IsType<FormatException>(result.Exception);
                Assert.Contains("Boolean", result.Exception.Message);
            }
            Assert.Null(generic.Result);
            Assert.Null(genericAsync.Result);
            Assert.Null(runtime.Result);
            Assert.Null(runtimeAsync.Result);
        }

        [Theory]
        [InlineData("!!Boolean", true)]
        [InlineData("!!!Boolean", false)]
        [InlineData("!!!!Boolean", true)]
        [InlineData("! !Boolean", true)]
        [InlineData("!!Boolean = true", true)]
        [InlineData("!!(Integer = 4)", true)]
        public async Task ParsePredicate_RepeatedNegation_IsAppliedOncePerOperator(string query, bool expected)
        {
            var entity = new TestEntity { Integer = 4, Boolean = true };
            var generic = Interpreter.ParsePredicate<TestEntity>(query);
            var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
            foreach (var result in new[] { generic, genericAsync })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, result.Result.Compile()(entity));
            }
            foreach (var result in new[] { runtime, runtimeAsync })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, ((Expression<Func<TestEntity, bool>>)result.Result).Compile()(entity));
            }
        }

        [Theory]
        [InlineData("Integer = 'bad'")]
        [InlineData("Integer in [$missing]")]
        public async Task ParsePredicate_Diagnostics_ArePreservedAcrossEntryPoints(string query)
        {
            var generic = Interpreter.ParsePredicate<TestEntity>(query);
            var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
            foreach (var result in new EvaluationResultBase[] { generic, genericAsync, runtime, runtimeAsync })
                Assert.False(result.Succeeded);
            foreach (var result in new EvaluationResultBase[] { generic, genericAsync, runtimeAsync })
            {
                Assert.Equal(runtime.InvalidValues, result.InvalidValues);
                Assert.Equal(runtime.InvalidVariables, result.InvalidVariables);
                Assert.Equal(runtime.InvalidProperties, result.InvalidProperties);
                Assert.Equal(runtime.InvalidOperators, result.InvalidOperators);
            }
        }

        [Theory]
        [InlineData("Root.Profile.Leaf.Id", true)]
        [InlineData("Root.Profile.Leaf.Missing", false)]
        [InlineData("Root.Empty.Leaf.Id", false)]
        public async Task VariableResolver_NestedPropertiesAndFields_AreTraversed(string path, bool expected)
        {
            var syncResolver = new NestedResolver();
            var asyncResolver = new NestedResolver();
            Assert.Null(syncResolver.Root.Empty);
            Assert.Equal(4, syncResolver.Root.Profile.Leaf.Id);
            Assert.Equal(expected, syncResolver.TryResolve("$" + path, out var value));
            var asynchronous = await asyncResolver.TryResolveAsync("$" + path);
            Assert.Equal(expected, asynchronous.Resolved);
            if (expected)
            {
                Assert.Equal(4, value);
                Assert.Equal(4, asynchronous.Value);
                var generic = Interpreter.ParsePredicate<TestEntity>("Integer = $" + path, syncResolver);
                var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>("Integer = $" + path, asyncResolver);
                Assert.True(generic.Succeeded, generic.Exception?.ToString());
                Assert.True(genericAsync.Succeeded, genericAsync.Exception?.ToString());
                Assert.True(generic.Result.Compile()(new TestEntity { Integer = 4 }));
                Assert.True(genericAsync.Result.Compile()(new TestEntity { Integer = 4 }));
            }
        }

        [Fact]
        public async Task VariableResolver_NestedGetterFailure_IsNotReportedAsMissing()
        {
            var resolver = new NestedResolver();
            Assert.Throws<InvalidOperationException>(() => resolver.Root.Profile.Broken);
            var exception = await Assert.ThrowsAsync<TargetInvocationException>(
                () => resolver.TryResolveAsync("$Root.Profile.Broken"));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            var result = await Interpreter.ParsePredicateAsync<TestEntity>("Integer = $Root.Profile.Broken", resolver);
            Assert.False(result.Succeeded);
            Assert.Contains("Getter failed", result.Exception?.ToString());
            Assert.Empty(result.InvalidVariables);
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("tr-TR")]
        public async Task Grammar_IsIndependentOfCurrentCulture(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var entity = new TestEntity { Integer = 4, Boolean = true, String = "ab" };
                foreach (var query in new[]
                {
                    "Integer IN [4,5] AND Boolean = TRUE",
                    "String.CONTAINS('a') OR Boolean = FALSE",
                    "String STARTSWITH 'a' AND NOT(Boolean = FALSE)"
                })
                {
                    var generic = Interpreter.ParsePredicate<TestEntity>(query);
                    var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(query);
                    var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
                    var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
                    foreach (var result in new[] { generic, genericAsync })
                    {
                        Assert.True(result.Succeeded, result.Exception?.ToString());
                        Assert.True(result.Result.Compile()(entity));
                    }
                    foreach (var result in new[] { runtime, runtimeAsync })
                    {
                        Assert.True(result.Succeeded, result.Exception?.ToString());
                        Assert.True(((Expression<Func<TestEntity, bool>>)result.Result).Compile()(entity));
                    }
                }
                Assert.Equal("in", new Filter { Operator = " IN " }.Operator);
                var orderBy = Interpreter.TryBuildOrderByClause<TestEntity>("Integer ASCENDING");
                Assert.True(orderBy.Succeeded, orderBy.Exception?.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private class YieldingResolver : VariableResolver
        {
            public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

            protected override async Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                Tokens.Add(cancellationToken);
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return Resolve(name);
            }

            protected static VariableInfo Resolve(string name)
            {
                object value;
                switch (name)
                {
                    case "amount": value = 4; break;
                    case "other": value = 5; break;
                    case "flag": value = true; break;
                    case "text": value = "ab"; break;
                    default: return new VariableInfo { Name = name };
                }
                return new VariableInfo { Name = name, Resolved = true, Value = value };
            }
        }

        private sealed class GatedResolver : YieldingResolver
        {
            public TaskCompletionSource<bool> Entered { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                Tokens.Add(cancellationToken);
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
                return Resolve(name);
            }
        }

        private sealed class NestedResolver : VariableResolver
        {
            public Root Root { get; } = new Root();
        }

        private sealed class Root
        {
            public Profile Profile { get; } = new Profile();
            [SuppressMessage("SonarAnalyzer.CSharp", "S2325", Justification = "Instance property is resolved by reflection to test null intermediate values.")]
            public Profile Empty => null;
        }

        private sealed class Profile
        {
            public Leaf Leaf = new Leaf();
            [SuppressMessage("SonarAnalyzer.CSharp", "S2325", Justification = "Instance getter is resolved by reflection to test failure propagation.")]
            public int Broken => throw new InvalidOperationException("Getter failed");
        }

        private sealed class Leaf
        {
            public int Id { get; } = 4;
        }
    }
}
