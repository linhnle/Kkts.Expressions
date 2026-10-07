using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public partial class InterpreterTest
    {
        [Theory]
        [InlineData("en-US")]
        [InlineData("vi-VN")]
        [InlineData("fr-FR")]
        [InlineData("de-DE")]
        public async Task ParsePredicate_NumericLiterals_AllEntryPoints_IgnoreCurrentCulture(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var entity = new TestEntity { Double = 8.3 };
                foreach (var query in new[]
                {
                    "Double = 8.3",
                    "Double in [2.0,4.0,8.3,16.12]",
                    "(Double = 8.3 or Double = 2.0) and Double > 0.0",
                    "Double + 0.2 = 8.5",
                    "1.5 + 2 = 3.5"
                })
                {
                    var sync = Interpreter.ParsePredicate<TestEntity>(query);
                    var asyncResult = await Interpreter.ParsePredicateAsync<TestEntity>(query);
                    var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
                    var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
                    foreach (var result in new[] { sync, asyncResult })
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
                foreach (var query in new[] { "Double = 8,3", "Double = 8.3.0" })
                {
                    var sync = Interpreter.ParsePredicate<TestEntity>(query);
                    var asyncResult = await Interpreter.ParsePredicateAsync<TestEntity>(query);
                    var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity));
                    var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity));
                    foreach (var result in new EvaluationResultBase[] { sync, asyncResult, runtime, runtimeAsync })
                    {
                        Assert.False(result.Succeeded);
                        Assert.NotNull(result.Exception);
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
