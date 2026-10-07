using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class TimeSpanTest
    {
        [Theory]
        [InlineData("02:30:00", 90000000000L)]
        [InlineData("1.02:30:00", 954000000000L)]
        [InlineData("-02:30:00", -90000000000L)]
        [InlineData("00:00:00.1234567", 1234567L)]
        [InlineData("00:00:00", 0L)]
        public void Cast_ValidDuration_AllOverloads(string value, long ticks)
        {
            var expected = TimeSpan.FromTicks(ticks);
            Assert.Equal(expected, value.Cast<TimeSpan>());
            Assert.Equal(expected, value.Cast(typeof(TimeSpan)));
            Assert.Equal(expected, value.Cast(typeof(TimeSpan?)));
            Assert.True(value.TryCast<TimeSpan>(out var generic));
            Assert.Equal(expected, generic);
            Assert.True(value.TryCast(typeof(TimeSpan), out var runtime));
            Assert.Equal(expected, runtime);
            Assert.True(value.TryCast(typeof(TimeSpan?), out var nullable));
            Assert.Equal(expected, nullable);
        }

        [Fact]
        public void Cast_FormatProvider_OverridesCurrentCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var expected = TimeSpan.FromMilliseconds(1500);
                Assert.Equal(expected, "00:00:01.5".Cast<TimeSpan>());
                Assert.Equal(expected, "00:00:01,5".Cast<TimeSpan>(CultureInfo.CurrentCulture));
                Assert.True("00:00:01,5".TryCast(typeof(TimeSpan), out var result, CultureInfo.CurrentCulture));
                Assert.Equal(expected, result);
                Assert.False("00:00:01,5".TryCast<TimeSpan>(out _));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Cast_EmptyDuration_RequiresNullable(string value)
        {
            Assert.Null(value.Cast(typeof(TimeSpan?)));
            Assert.True(value.TryCast(typeof(TimeSpan?), out var nullable));
            Assert.Null(nullable);
            Assert.Throws<FormatException>(() => value.Cast<TimeSpan>());
            Assert.False(value.TryCast<TimeSpan>(out var result));
            Assert.Equal(default(TimeSpan), result);
            Assert.False(value.TryCast(typeof(TimeSpan), out var runtime));
            Assert.Null(runtime);
        }

        [Theory]
        [InlineData("not a duration")]
        [InlineData("00:xx:00")]
        public void Cast_InvalidDuration_ReportsFailure(string value)
        {
            Assert.Throws<FormatException>(() => value.Cast<TimeSpan>());
            Assert.False(value.TryCast<TimeSpan>(out var generic));
            Assert.Equal(default(TimeSpan), generic);
            Assert.False(value.TryCast(typeof(TimeSpan?), out var nullable));
            Assert.Null(nullable);
        }

        [Theory]
        [InlineData("00:99:00")]
        [InlineData("10675199.02:48:05.4775808")]
        public void Cast_Overflow_ReportsFailure(string value)
        {
            Assert.Throws<OverflowException>(() => value.Cast<TimeSpan>());
            Assert.False(value.TryCast<TimeSpan>(out var result));
            Assert.Equal(default(TimeSpan), result);
        }

        [Theory]
        [InlineData("=", "02:30:00")]
        [InlineData("!=", "01:00:00")]
        [InlineData("<", "03:00:00")]
        [InlineData("<=", "02:30:00")]
        [InlineData(">", "02:00:00")]
        [InlineData(">=", "02:30:00")]
        [InlineData("in", "'01:00:00', '02:30:00'")]
        public async Task Duration_Comparisons_AllEntryPoints(string comparison, string value)
        {
            var entity = new DurationEntity
            {
                Duration = TimeSpan.FromMinutes(150),
                NullableDuration = TimeSpan.FromMinutes(150)
            };
            var nonMatchingDuration = comparison == "!=" ? TimeSpan.FromHours(1) :
                comparison == ">" || comparison == ">=" ? TimeSpan.Zero : TimeSpan.FromDays(2);
            var nonMatching = new DurationEntity
            {
                Duration = nonMatchingDuration,
                NullableDuration = nonMatchingDuration
            };
            foreach (var property in new[] { "Duration", "NullableDuration" })
            {
                var literal = comparison == "in" ? $"[{value}]" : $"'{value}'";
                var query = $"{property} {comparison} {literal}";
                var sync = Interpreter.ParsePredicate<DurationEntity>(query);
                var asyncResult = await Interpreter.ParsePredicateAsync<DurationEntity>(query);
                var runtime = Interpreter.ParsePredicate(query, typeof(DurationEntity));
                var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(DurationEntity));
                foreach (var result in new[] { sync, asyncResult })
                {
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    Assert.True(result.Result.Compile()(entity));
                    Assert.False(result.Result.Compile()(nonMatching));
                }
                foreach (var result in new[] { runtime, runtimeAsync })
                {
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    Assert.True(((Expression<Func<DurationEntity, bool>>)result.Result).Compile()(entity));
                }

                var filter = new Filter { Property = property, Operator = comparison, Value = value };
                Assert.True(filter.BuildPredicate<DurationEntity>().Compile()(entity));
                Assert.False(filter.BuildPredicate<DurationEntity>().Compile()(nonMatching));
                Assert.True((await filter.BuildPredicateAsync<DurationEntity>()).Compile()(entity));
                Assert.False((await filter.BuildPredicateAsync<DurationEntity>()).Compile()(nonMatching));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Duration_Variables_SupportStringsAndTypedValues(bool typed)
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("duration", typed ? (object)TimeSpan.FromMinutes(150) : "02:30:00");
            var entity = new DurationEntity
            {
                Duration = TimeSpan.FromMinutes(150),
                NullableDuration = TimeSpan.FromMinutes(150)
            };
            foreach (var property in new[] { "Duration", "NullableDuration" })
            {
                foreach (var query in new[] { $"{property} = $duration", $"{property} in [$duration]" })
                {
                    var sync = Interpreter.ParsePredicate<DurationEntity>(query, variableResolver: resolver);
                    var asyncResult = await Interpreter.ParsePredicateAsync<DurationEntity>(query, variableResolver: resolver);
                    foreach (var result in new[] { sync, asyncResult })
                    {
                        Assert.True(result.Succeeded, result.Exception?.ToString());
                        Assert.True(result.Result.Compile()(entity));
                    }
                }
                foreach (var comparison in new[] { "=", "in" })
                {
                    var filter = new Filter { Property = property, Operator = comparison, Value = "$duration" };
                    Assert.True(filter.BuildPredicate<DurationEntity>(variableResolver: resolver).Compile()(entity));
                    Assert.True((await filter.BuildPredicateAsync<DurationEntity>(variableResolver: resolver)).Compile()(entity));
                }
            }
        }

        [Fact]
        public async Task Duration_NullAndInvalidValues_PreserveDiagnostics()
        {
            var entity = new DurationEntity();
            var sync = Interpreter.ParsePredicate<DurationEntity>("NullableDuration = null");
            var asyncResult = await Interpreter.ParsePredicateAsync<DurationEntity>("NullableDuration = null");
            Assert.True(sync.Succeeded, sync.Exception?.ToString());
            Assert.True(asyncResult.Succeeded, asyncResult.Exception?.ToString());
            Assert.True(sync.Result.Compile()(entity));
            Assert.True(asyncResult.Result.Compile()(entity));
            var filter = new Filter { Property = "NullableDuration", Operator = "=", Value = "" };
            Assert.True(filter.BuildPredicate<DurationEntity>().Compile()(entity));
            Assert.True((await filter.BuildPredicateAsync<DurationEntity>()).Compile()(entity));

            var invalid = Interpreter.ParsePredicate<DurationEntity>("Duration = 'invalid'");
            var invalidAsync = await Interpreter.ParsePredicateAsync<DurationEntity>("Duration = 'invalid'");
            foreach (var result in new[] { invalid, invalidAsync })
            {
                Assert.False(result.Succeeded);
                Assert.IsType<FormatException>(result.Exception);
                Assert.Contains("invalid", result.InvalidValues);
            }
        }

        public class DurationEntity
        {
            public TimeSpan Duration { get; set; }
            public TimeSpan? NullableDuration { get; set; }
        }
    }
}
