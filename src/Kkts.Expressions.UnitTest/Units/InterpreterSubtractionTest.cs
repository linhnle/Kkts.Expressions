using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public partial class InterpreterTest
    {
        [Theory]
        [InlineData("PInt-1 = 3")]
        [InlineData("PInt - PInt = 0")]
        [InlineData("PDouble - PInt = -1.5")]
        [InlineData("(PInt + 2) - 1 >= 5")]
        [InlineData("(PInt - 1) = (5 - 2)")]
        [InlineData("PInt = 5 - 1")]
        [InlineData("PInt = (5 - 2) + 1")]
        [InlineData("(PInt - 1) + (2 - 1) = 4")]
        [InlineData("10 - 3 + 2 = 9")]
        [InlineData("10 + 3 - 2 = 11")]
        [InlineData("10 - 3 - 2 = 5")]
        [InlineData("10 - (3 + 2) = 5")]
        [InlineData("PInt - 1 > 0 and not(PInt - 5 > 0) or Boolean = true")]
        [InlineData("!(PInt - 1 = 4)")]
        [InlineData("PInt - 1 in [3,5]")]
        [InlineData("PInt - null = null")]
        [InlineData("null - PInt = null")]
        [InlineData("NInt - 1 = null")]
        [InlineData("PInt - -5 = 9")]
        [InlineData("PInt--5 = 9")]
        [InlineData("PInt - (-5) = 9")]
        [InlineData("PInt + -5 = -1")]
        [InlineData("-5 + 2 = -3")]
        [InlineData("-5 - -2 = -3")]
        [InlineData("PDouble > -0.5")]
        [InlineData("-0.5 < PDouble")]
        [InlineData("1 - 1.5 = -0.5")]
        [InlineData("10 - 3 + 'x' = '7x'")]
        [InlineData("'result:' + (10 - 3) = 'result:7'")]
        [InlineData("String.contains('a' + (10 - 3))")]
        [InlineData("String.contains(('a' + (10 - 3)) + '-')")]
        [InlineData("String = 'a7-b'")]
        public async Task ParsePredicate_Subtraction_AllEntryPoints(string query)
        {
            await AssertSubtractionPredicate(query, new PlusEntity { PInt = 4, PDouble = 2.5, String = "a7-b" });
        }

        private static async Task AssertSubtractionPredicate(string query, PlusEntity entity,
            VariableResolver resolver = null, IEnumerable<string> allowed = null, IDictionary<string, string> mapping = null)
        {
            var sync = Interpreter.ParsePredicate<PlusEntity>(query, resolver, allowed, mapping);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query, resolver, allowed, mapping);
            var runtime = Interpreter.ParsePredicate(query, typeof(PlusEntity), resolver, allowed, mapping);
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity), resolver, allowed, mapping);
            foreach (var result in new[] { sync, asyncResult })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(result.Result.Compile()(entity));
            }
            foreach (var result in new[] { runtime, runtimeAsync })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(((Expression<Func<PlusEntity, bool>>)result.Result).Compile()(entity));
            }
        }

        [Theory]
        [InlineData("PInt - = 5")]
        [InlineData("PInt -")]
        [InlineData("PInt - 1")]
        [InlineData("PInt - - = 5")]
        [InlineData("PInt - - 5 = 5")]
        [InlineData("PInt ---5 = 5")]
        [InlineData("- = 0")]
        [InlineData("-PInt = 5")]
        [InlineData("-$missing = 5")]
        [InlineData("-(PInt + 1) = 5")]
        [InlineData("+5 - 1 = 4")]
        [InlineData("1e2 - 1 = 99")]
        [InlineData("1L - 1 = 0")]
        [InlineData("PInt in [10-3]")]
        [InlineData("PInt * 2 = 8")]
        [InlineData("PInt / 2 = 2")]
        [InlineData("-9223372036854775809 - 0 = 0")]
        public async Task ParsePredicate_Subtraction_InvalidSyntax(string query)
        {
            await AssertSubtractionFailure(query);
        }

        [Theory]
        [InlineData("Boolean - 1 = 0")]
        [InlineData("String - 1 = 0")]
        [InlineData("'5' - 2 = 3")]
        [InlineData("DateTime - DateTime = 0")]
        [InlineData("Duration - Duration = 0")]
        [InlineData("Timestamp - Timestamp = 0")]
        [InlineData("Option - 1 = 0")]
        [InlineData("Custom - Custom = Custom")]
        [InlineData("null - null = null")]
        [InlineData("PDecimal - PDouble = 0")]
        [InlineData("PDecimal - PFloat = 0")]
        [InlineData("PULong - PInt = 0")]
        [InlineData("PULong - (2 - 3) = 0")]
        [InlineData("PULong - (2147483647 + 1) = 0")]
        [InlineData("10 + 'x' - 3 = '7x'")]
        public async Task ParsePredicate_Subtraction_InvalidOperands(string query)
        {
            var results = await AssertSubtractionFailure(query);
            Assert.All(results, result => Assert.Contains("-", result.InvalidOperators));
            if (query == "Boolean - 1 = 0")
                Assert.All(results, result => Assert.Contains("index 9", result.Exception.ToString()));
        }

        private static async Task<EvaluationResultBase[]> AssertSubtractionFailure(string query,
            IEnumerable<string> allowed = null)
        {
            var sync = Interpreter.ParsePredicate<PlusEntity>(query, validProperties: allowed);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query, validProperties: allowed);
            var runtime = Interpreter.ParsePredicate(query, typeof(PlusEntity), validProperties: allowed);
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity), validProperties: allowed);
            var results = new EvaluationResultBase[] { sync, asyncResult, runtime, runtimeAsync };
            Assert.All(results, result =>
            {
                Assert.False(result.Succeeded);
                if (allowed == null) Assert.NotNull(result.Exception);
            });
            Assert.Null(sync.Result);
            Assert.Null(asyncResult.Result);
            Assert.Null(runtime.Result);
            Assert.Null(runtimeAsync.Result);
            return results;
        }

        [Theory]
        [MemberData(nameof(PlusNumericPairs))]
        public async Task ParsePredicate_Subtraction_NumericPairMatrix(string left, string right, Type expected, bool nullable)
        {
            var entity = new PlusEntity();
            SetNumericOperands(entity, new[] { left, right });
            var query = $"{left} - {right} = 0";
            var sync = Interpreter.ParsePredicate<PlusEntity>(query);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query);
            foreach (var result in new[] { sync, asyncResult })
            {
                if (expected == null)
                {
                    Assert.False(result.Succeeded);
                    Assert.Contains("-", result.InvalidOperators);
                    Assert.Null(result.Result);
                    continue;
                }
                Assert.True(result.Succeeded, result.Exception?.ToString());
                var difference = Assert.IsAssignableFrom<BinaryExpression>(((BinaryExpression)result.Result.Body).Left);
                Assert.Equal(ExpressionType.Subtract, difference.NodeType);
                Assert.Equal(nullable ? typeof(Nullable<>).MakeGenericType(expected) : expected, difference.Type);
                Assert.Equal(nullable, difference.IsLiftedToNull);
                var inspector = new PlusTreeInspector();
                inspector.Visit(result.Result);
                Assert.False(inspector.HasInvocation);
                Assert.True(result.Result.Compile()(entity));
                if (!nullable) continue;
                typeof(PlusEntity).GetProperty(left.StartsWith('N') ? left : right).SetValue(entity, null);
                Assert.False(result.Result.Compile()(entity));
                SetNumericOperands(entity, new[] { left, right }.Where(name => name.StartsWith('N')));
            }
        }

        [Theory]
        [InlineData("-2147483648", typeof(int))]
        [InlineData("-2147483649", typeof(long))]
        [InlineData("-9223372036854775808", typeof(long))]
        [InlineData("2147483648", typeof(uint))]
        [InlineData("9223372036854775808", typeof(ulong))]
        [InlineData("18446744073709551615", typeof(ulong))]
        [InlineData("-0.5", typeof(double))]
        public async Task ParsePredicate_Subtraction_LiteralTypes(string literal, Type expected)
        {
            var query = $"{literal} - 0 = {literal}";
            await AssertSubtractionPredicate(query, new PlusEntity());
            var result = Interpreter.ParsePredicate<PlusEntity>(query);
            Assert.Equal(expected, ((BinaryExpression)result.Result.Body).Left.Type);
        }

        [Theory]
        [InlineData("PULong - (3 - 2) = 4", typeof(ulong))]
        [InlineData("PULong - (3 + 2 - 4) = 4", typeof(ulong))]
        [InlineData("PUInt - (3 - 2) = 4", typeof(uint))]
        [InlineData("NUInt - 1 = 4", typeof(uint?))]
        [InlineData("PUInt - (2 - 3) = 6", typeof(long))]
        [InlineData("PUInt - (2147483647 + 1) = 2147483653", typeof(long))]
        public async Task ParsePredicate_Subtraction_ConstantConversions(string query, Type expected)
        {
            await AssertSubtractionPredicate(query, new PlusEntity { PULong = 5, PUInt = 5, NUInt = 5 });
            Assert.Equal(expected, ((BinaryExpression)Interpreter.ParsePredicate<PlusEntity>(query).Result.Body).Left.Type);
        }

        [Fact]
        public async Task ParsePredicate_Subtraction_NullsOverflowAndFloatingPoint()
        {
            var entity = new PlusEntity
            {
                PInt = int.MinValue, RInt = int.MaxValue, PUInt = 0, PULong = 0,
                PByte = 100, RByte = 200, PDouble = double.PositiveInfinity, PDecimal = decimal.MinValue
            };
            foreach (var query in new[]
            {
                "PInt - 1 = RInt", "PUInt - 1 = 4294967295", "PULong - 1 = 18446744073709551615",
                "PByte - RByte = -100", "PDouble - PDouble != PDouble - PDouble",
                "NInt - 1 = null", "1 - NInt = null", "NInt - NDouble = null"
            })
                await AssertSubtractionPredicate(query, entity);
            var overflow = Interpreter.ParsePredicate<PlusEntity>("PDecimal - 1 = PDecimal");
            Assert.True(overflow.Succeeded, overflow.Exception?.ToString());
            Assert.Throws<OverflowException>(() => overflow.Result.Compile()(entity));
            entity.NInt = 5;
            await AssertSubtractionPredicate("NInt - 1 = 4", entity);
            Assert.False(Interpreter.ParsePredicate<PlusEntity>("NInt - 1 = null").Result.Compile()(entity));
            entity.PInt = -5;
            await AssertSubtractionPredicate("PInt = -5", entity);
        }

        [Fact]
        public async Task ParsePredicate_Subtraction_VariablesAndValidation()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("amount", 2);
            resolver.TryAdd("fraction", 0.5);
            resolver.TryAdd("target", 2);
            resolver.TryAdd("nil", null);
            resolver.TryAdd("values", new[] { 2, 3 });
            var entity = new PlusEntity { PInt = 4, Parent = new ParentEntity { Id = 2 } };
            var mapping = new Dictionary<string, string> { ["stock"] = "PInt", ["reserved"] = "Parent.Id" };
            foreach (var query in new[]
            {
                "stock - reserved = 2", "stock - $amount = $target", "$amount - stock = -2",
                "stock - $amount > 0",
                "$amount - $fraction = 1.5", "stock - $nil = null", "$nil - stock = null",
                "stock - $amount in $values", "stock - -5 = 9"
            })
                await AssertSubtractionPredicate(query, entity, resolver, new[] { "stock", "reserved" }, mapping);
            foreach (var query in new[] { "PInt - PDouble = 0", "PDouble - PInt = 0" })
            {
                var results = await AssertSubtractionFailure(query, new[] { "PInt" });
                Assert.All(results, result => Assert.Contains("PDouble", result.InvalidProperties));
            }
            foreach (var query in new[] { "PInt - $missing = 0", "$missing - PInt = 0", "PInt - Missing = 0" })
            {
                var results = await AssertSubtractionFailure(query);
                Assert.All(results, result => Assert.NotEmpty(result.InvalidVariables));
                if (query.Contains("Missing"))
                    Assert.All(results, result => Assert.Contains("Missing", result.InvalidProperties));
            }
        }

        [Fact]
        public async Task ParsePredicate_Subtraction_InvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                await AssertSubtractionPredicate("PDouble - -0.5 = 3", new PlusEntity { PDouble = 2.5 });
                await AssertSubtractionFailure("PDouble - -0,5 = 3");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }

    public partial class InterpreterAsyncTest
    {
        [Theory]
        [InlineData("PInt - $increment = 2")]
        [InlineData("$target - (PInt - $increment) = 4")]
        [InlineData("not(PInt - $increment != 2) and Boolean = $flag")]
        public async Task ParsePredicateAsync_Subtraction_AsyncOnlyResolver(string query)
        {
            using var source = new CancellationTokenSource();
            foreach (var runtime in new[] { false, true })
            {
                var resolver = new PlusResolver(source.Token);
                var entity = new InterpreterTest.PlusEntity { PInt = 4 };
                if (runtime)
                {
                    var result = await Interpreter.ParsePredicateAsync(query, typeof(InterpreterTest.PlusEntity),
                        resolver, cancellationToken: source.Token);
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    Assert.True(((Expression<Func<InterpreterTest.PlusEntity, bool>>)result.Result).Compile()(entity));
                }
                else
                {
                    var result = await Interpreter.ParsePredicateAsync<InterpreterTest.PlusEntity>(
                        query, resolver, cancellationToken: source.Token);
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    Assert.True(result.Result.Compile()(entity));
                }
                Assert.NotEmpty(resolver.Calls);
                Assert.All(resolver.Calls.Values, count => Assert.Equal(1, count));
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_Subtraction_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var generic = await Interpreter.ParsePredicateAsync<InterpreterTest.PlusEntity>(
                "PInt - $increment = 2", new PlusResolver(source.Token), cancellationToken: source.Token);
            var runtime = await Interpreter.ParsePredicateAsync("PInt - $increment = 2",
                typeof(InterpreterTest.PlusEntity), new PlusResolver(source.Token), cancellationToken: source.Token);
            foreach (var result in new EvaluationResultBase[] { generic, runtime })
            {
                Assert.False(result.Succeeded);
                Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
                Assert.Empty(result.InvalidValues);
            }
            Assert.Null(generic.Result);
            Assert.Null(runtime.Result);
        }
    }
}
