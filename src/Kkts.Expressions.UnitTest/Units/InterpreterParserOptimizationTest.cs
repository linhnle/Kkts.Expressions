using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Kkts.Expressions.UnitTest.Units
{
    public class InterpreterParserOptimizationTest
    {
        private readonly ITestOutputHelper _output;

        public InterpreterParserOptimizationTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParsePredicate_LongLiteral_AllocationGrowthIsBounded(bool asynchronous)
        {
            const int length = 16384;
            var shortQuery = "String = 'x'";
            var longQuery = $"String = '{new string('x', length)}'";
            Action<string> parse = query =>
            {
                var result = asynchronous
                    ? Interpreter.ParsePredicateAsync<TestEntity>(query).GetAwaiter().GetResult()
                    : Interpreter.ParsePredicate<TestEntity>(query);
                Assert.True(result.Succeeded, result.Exception?.ToString());
            };

            parse(shortQuery);
            parse(longQuery);
            var shortAllocation = MeasureAllocation(() => parse(shortQuery));
            var longAllocation = MeasureAllocation(() => parse(longQuery));
            _output.WriteLine($"Allocated bytes per parse: short={shortAllocation}, long={longAllocation}");

            Assert.True(longAllocation - shortAllocation < length * 20,
                $"Allocation growth was {longAllocation - shortAllocation} bytes for {length} literal characters.");
        }

        [Theory]
        [InlineData("Integer = 4 or Integer = 5 and Boolean = false", true)]
        [InlineData("(Integer = 4 or Integer = 5) and Boolean = false", false)]
        [InlineData("not((Integer = 5 or Integer = 6)) and String.contains(('a' + 'b'))", true)]
        [InlineData("Integer + 1 + 2 + 3 = 10 and (1 + 2 + 3) = 6", true)]
        [InlineData("String + 'c' + 'd' = 'abcd' and Integer + 1 = 5", true)]
        [InlineData("Integer in [1, 4, 7] and !(Integer = 5)", true)]
        [InlineData("!(!Boolean) and !Boolean = false", true)]
        [InlineData("!(!(!Boolean)) or !(!(Integer = 4))", true)]
        [InlineData("String.contains('a') and String.endswith('b') and String.startswith('a')", true)]
        [InlineData("Integer = 5 and Boolean = true or Integer = 4 and String = 'ab' or Boolean = false", true)]
        [InlineData("1 + 2 + 'x' = '3x' and 1 + (2 + 'x') = '12x'", true)]
        public async Task ParsePredicate_BufferTransitions_PreserveSemantics(string query, bool expected)
        {
            var entity = new TestEntity { Integer = 4, Boolean = true, String = "ab" };
            var sync = Interpreter.ParsePredicate<TestEntity>(query);
            var asynchronous = await Interpreter.ParsePredicateAsync<TestEntity>(query);

            foreach (var result in new[] { sync, asynchronous })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, result.Result.Compile()(entity));
            }
        }

        [Fact]
        public async Task ParsePredicate_LongAdditionChain_PreservesAssociativity()
        {
            var query = string.Join(" + ", Enumerable.Repeat("1", 128)) + " = 128";
            var sync = Interpreter.ParsePredicate<TestEntity>(query);
            var asynchronous = await Interpreter.ParsePredicateAsync<TestEntity>(query);

            foreach (var result in new[] { sync, asynchronous })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(result.Result.Compile()(new TestEntity()));
            }
        }

        [Theory]
        [InlineData("and")]
        [InlineData("or")]
        public async Task ParsePredicate_LongLogicalChain_PreservesSemantics(string op)
        {
            var query = string.Join($" {op} ", Enumerable.Repeat("Integer = 4", 128));
            var sync = Interpreter.ParsePredicate<TestEntity>(query);
            var asynchronous = await Interpreter.ParsePredicateAsync<TestEntity>(query);

            foreach (var result in new[] { sync, asynchronous })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(result.Result.Compile()(new TestEntity { Integer = 4 }));
                Assert.False(result.Result.Compile()(new TestEntity { Integer = 5 }));
            }
        }

        [Theory]
        [InlineData("Integer + = 5")]
        [InlineData("Integer = 4 and")]
        [InlineData("Integer = 4 or (Boolean = true")]
        [InlineData("String.contains()")]
        public async Task ParsePredicate_InvalidReduction_ReturnsFailure(string query)
        {
            var sync = Interpreter.ParsePredicate<TestEntity>(query);
            var asynchronous = await Interpreter.ParsePredicateAsync<TestEntity>(query);

            Assert.False(sync.Succeeded);
            Assert.Null(sync.Result);
            Assert.NotNull(sync.Exception);
            Assert.False(asynchronous.Succeeded);
            Assert.Null(asynchronous.Result);
            Assert.Equal(sync.Exception.GetType(), asynchronous.Exception?.GetType());
            Assert.Equal(sync.Exception.Message, asynchronous.Exception?.Message);
        }

        private static long MeasureAllocation(Action action)
        {
            const int iterations = 10;
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; ++i)
                action();
            return (GC.GetAllocatedBytesForCurrentThread() - start) / iterations;
        }
    }
}
