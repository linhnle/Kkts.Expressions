using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class ValueFormattingTest : PredicateTestBase
    {
        [Fact]
        public void CultureIndependence()
        {
            var original = CultureInfo.CurrentCulture;
            var originalUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
                Assert.Equal("8,3", 8.3m.ToString());
                var interpreted = Interpreter.ParsePredicate<Data>("Price > 8.3").Result;
                var date = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
                var score = 1.25;

                JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), "{\"range\":{\"price\":{\"gt\":8.3}}}");
                Assert.Contains("8.3", new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted).ToJsonString());
                AssertClause(x => x.Score < score, "{\"range\":{\"score\":{\"lt\":1.25}}}");
                AssertClause(x => x.Created == date, "{\"term\":{\"created\":\"2024-03-04T05:06:07.0000000Z\"}}");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                CultureInfo.CurrentUICulture = originalUi;
            }
        }

        [Fact]
        public void BuiltInTypes()
        {
            var guid = Guid.Parse("3F2504E0-4F89-11D3-9A0C-0305E82C3301");
            var utc = new DateTime(2024, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            var unspecified = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
            var local = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);
            var offset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(7));
            var max = ulong.MaxValue;
            var min = long.MinValue;

            AssertClause(x => x.Key == guid, "{\"term\":{\"key\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"}}");
            AssertClause(x => x.Created == utc, "{\"term\":{\"created\":\"2024-01-02T03:04:05.0060000Z\"}}");
            AssertClause(x => x.Created == unspecified, "{\"term\":{\"created\":\"2024-01-02T03:04:05.0000000\"}}");
            AssertClause(x => x.Created == local, "{\"term\":{\"created\":\"" + local.ToString("o", CultureInfo.InvariantCulture) + "\"}}");
            AssertClause(x => x.Updated >= offset, "{\"range\":{\"updated\":{\"gte\":\"2024-01-02T03:04:05.0000000+07:00\"}}}");
            AssertClause(x => x.ULongValue == max, "{\"term\":{\"uLongValue\":18446744073709551615}}");
            AssertClause(x => x.LongValue == min, "{\"term\":{\"longValue\":-9223372036854775808}}");
            AssertClause(x => x.Grade == 'A', "{\"term\":{\"grade.keyword\":\"A\"}}");
            AssertClause(x => x.IsEnabled == true, "{\"term\":{\"isEnabled\":true}}");
            AssertClause(x => x.Price == 1.50m, "{\"term\":{\"price\":1.50}}");
            AssertClause(x => x.Score == 0.1, "{\"term\":{\"score\":0.1}}");
            AssertClause(x => x.Ratio == 0.1f, "{\"term\":{\"ratio\":0.1}}");
        }

        [Fact]
        public void EnumNames_GloballyAndPerField()
        {
            var global = OpenSearchMapping.For<Data>().EnumFormat(EnumFormat.Name).Build();
            var perField = OpenSearchMapping.For<Data>().Field(x => x.Status, f => f.EnumFormat(EnumFormat.Name)).Build();
            var interpreted = Interpreter.ParsePredicate<Data>("Status = 'Active'").Result;

            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>(global).TranslatePredicate(interpreted), "{\"term\":{\"status\":\"Active\"}}");
            AssertClause(x => x.Status == Status.Active, "{\"term\":{\"status\":\"Active\"}}", perField);
            AssertClause(x => x.NullableStatus == Status.Active, "{\"term\":{\"nullableStatus\":1}}", perField);
            AssertClause(x => new[] { Status.Active, Status.Inactive }.Contains(x.Status), "{\"terms\":{\"status\":[\"Active\",\"Inactive\"]}}", global);
            var undefined = (Status)42;
            AssertFails(x => x.Status == undefined, OpenSearchDiagnosticCodes.UnsupportedValue, global);
            AssertFails(x => x.Status > Status.Active, OpenSearchDiagnosticCodes.UnsupportedValue, global);
        }

        [Fact]
        public void TimeSpanWithoutConverter_Fails()
        {
            var duration = TimeSpan.FromMinutes(5);

            var diagnostic = AssertFails(x => x.Duration > duration, OpenSearchDiagnosticCodes.UnsupportedValue);

            Assert.Equal("Duration", diagnostic.MemberPath);
            Assert.Contains("TimeSpan", diagnostic.Message);
            Assert.DoesNotContain("00:05:00", diagnostic.Message);
            Assert.DoesNotContain("00:05:00", diagnostic.ExpressionText);
        }

        [Fact]
        public void Converters_ByTypeAndField()
        {
            var duration = TimeSpan.FromMinutes(5);
            var byType = OpenSearchMapping.For<Data>().Converter<TimeSpan>(v => (long)v.TotalMilliseconds).Build();
            var byField = OpenSearchMapping.For<Data>()
                .Converter<TimeSpan>(v => (long)v.TotalMilliseconds)
                .Field(x => x.Duration, f => f.Converter<TimeSpan>(v => v.ToString("c", CultureInfo.InvariantCulture)))
                .Build();
            var returnsNull = OpenSearchMapping.For<Data>().Converter<TimeSpan>(v => null).Build();

            AssertClause(x => x.Duration > duration, "{\"range\":{\"duration\":{\"gt\":300000}}}", byType);
            AssertClause(x => x.Duration == duration, "{\"term\":{\"duration\":\"00:05:00\"}}", byField);
            AssertFails(x => x.Duration == duration, OpenSearchDiagnosticCodes.UnsupportedValue, returnsNull);
        }

        [Fact]
        public void NonFinite_Fails()
        {
            AssertFails(x => x.Score > double.NaN, OpenSearchDiagnosticCodes.UnsupportedValue);
            AssertFails(x => x.Score == double.PositiveInfinity, OpenSearchDiagnosticCodes.UnsupportedValue);
            AssertFails(x => x.Ratio < float.NegativeInfinity, OpenSearchDiagnosticCodes.UnsupportedValue);
            AssertFails(x => new[] { 1d, double.NaN }.Contains(x.Score), OpenSearchDiagnosticCodes.UnsupportedValue);
        }

        [Fact]
        public void CapturedVariables()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("user.id", 7);
            var interpreted = Interpreter.ParsePredicate<Data>("Id = $user.id", variableResolver: resolver).Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), "{\"term\":{\"id\":7}}");

            var now = Interpreter.ParsePredicate<Data>("Created < $utcnow").Result;
            var result = new OpenSearchQueryTranslator<Data>().TranslatePredicate(now);
            Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
            var value = result.Value["bool"]["filter"][0]["range"]["created"]["lt"].GetValue<string>();
            Assert.True(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) <= DateTime.UtcNow);
            Assert.EndsWith("Z", value);

            var closure = 42;
            AssertClause(x => x.Id == closure, "{\"term\":{\"id\":42}}");
        }
    }
}
