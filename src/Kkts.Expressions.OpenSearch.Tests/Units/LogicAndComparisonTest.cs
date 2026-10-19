using System;
using System.Text.Json.Nodes;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class LogicAndComparisonTest : PredicateTestBase
    {
        [Fact]
        public void MixedLogic()
        {
            var predicate = Interpreter.ParsePredicate<Data>("(id = 1 or intValue = 2) and not(longValue = 3)").Result;

            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate),
                "{\"bool\":{\"should\":[{\"term\":{\"id\":1}},{\"term\":{\"intValue\":2}}],\"minimum_should_match\":1}}",
                "{\"bool\":{\"must_not\":[{\"term\":{\"longValue\":3}}]}}");
        }

        [Fact]
        public void Flattening()
        {
            JsonAssert.Filter(Translate(x => x.Id == 1 && x.IntValue == 2 && x.LongValue == 3),
                "{\"term\":{\"id\":1}}", "{\"term\":{\"intValue\":2}}", "{\"term\":{\"longValue\":3}}");
            AssertClause(x => x.Id == 1 || (x.IntValue == 2 || x.LongValue == 3),
                "{\"bool\":{\"should\":[{\"term\":{\"id\":1}},{\"term\":{\"intValue\":2}},{\"term\":{\"longValue\":3}}],\"minimum_should_match\":1}}");
        }

        [Fact]
        public void ConstantPredicates()
        {
            AssertClause(x => true, "{\"match_all\":{}}");
            AssertClause(x => false, "{\"match_none\":{}}");
            AssertClause(x => x.Id == 1 || true, "{\"match_all\":{}}");
            AssertClause(x => !(x.Id == 1 && false), "{\"match_all\":{}}");
        }

        [Fact]
        public void PartialFailure_ReturnsNoQueryAndAccumulatesDiagnostics()
        {
            var predicate = Interpreter.ParsePredicate<Data>("Id = 1 and Price + 1 > 5").Result;

            var result = new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate);

            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(OpenSearchDiagnosticCodes.UnsupportedExpression, diagnostic.Code);
            Assert.Contains("+", diagnostic.ExpressionText);

            var multiple = Translate(x => x.Id + 1 > 5 || x.Name.ToLower() == "a");
            Assert.Equal(2, multiple.Diagnostics.Count);
            Assert.Null(multiple.Value);
        }

        [Fact]
        public void ThrowingApi_CarriesSameDiagnostics()
        {
            var result = Translate(x => x.Id + 1 > 5);
            var exception = Assert.Throws<OpenSearchTranslationException>(() => result.GetValueOrThrow());
            Assert.Equal(result.Diagnostics, exception.Diagnostics);
        }

        [Fact]
        public void Comparisons()
        {
            AssertClause(x => x.Id == 1, "{\"term\":{\"id\":1}}");
            AssertClause(x => x.Id != 1, "{\"bool\":{\"must_not\":[{\"term\":{\"id\":1}}]}}");
            AssertClause(x => x.Id < 1, "{\"range\":{\"id\":{\"lt\":1}}}");
            AssertClause(x => x.Id <= 1, "{\"range\":{\"id\":{\"lte\":1}}}");
            AssertClause(x => x.Id > 1, "{\"range\":{\"id\":{\"gt\":1}}}");
            AssertClause(x => x.Id >= 1, "{\"range\":{\"id\":{\"gte\":1}}}");
            AssertClause(x => x.Price >= 2.5m, "{\"range\":{\"price\":{\"gte\":2.5}}}");
            AssertClause(x => x.Name == "a", "{\"term\":{\"name.keyword\":\"a\"}}");
            var date = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            AssertClause(x => x.Created > date, "{\"range\":{\"created\":{\"gt\":\"2024-01-02T00:00:00.0000000Z\"}}}");
        }

        [Fact]
        public void ReversedOperands()
        {
            AssertClause(x => 5 < x.IntValue, "{\"range\":{\"intValue\":{\"gt\":5}}}");
            AssertClause(x => 5 >= x.IntValue, "{\"range\":{\"intValue\":{\"lte\":5}}}");
            AssertClause(x => 5 == x.IntValue, "{\"term\":{\"intValue\":5}}");
            AssertClause(x => 5 < x.Customer.Age, "{\"range\":{\"customer.age\":{\"gt\":5}}}");
        }

        [Fact]
        public void NullSemantics()
        {
            AssertClause(x => x.NullableInteger == null, "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"nullableInteger\"}}]}}");
            AssertClause(x => null == x.NullableInteger, "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"nullableInteger\"}}]}}");
            AssertClause(x => x.NullableInteger != null, "{\"exists\":{\"field\":\"nullableInteger\"}}");
            AssertClause(x => x.Name == null, "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"name\"}}]}}");
#pragma warning disable CS0464
            AssertClause(x => x.NullableInteger > null, "{\"match_none\":{}}");
#pragma warning restore CS0464
            AssertClause(x => x.NullableInteger != 5, "{\"bool\":{\"must_not\":[{\"term\":{\"nullableInteger\":5}}]}}");
            var interpreted = Interpreter.ParsePredicate<Data>("NullableInteger = null").Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"nullableInteger\"}}]}}");
        }

        [Theory]
        [InlineData(5, false)]
        [InlineData(6, true)]
        [InlineData(null, true)]
        public void InequalityMatchesMissing(int? value, bool expected)
        {
            var document = value == null ? Doc() : Doc(("nullableInteger", value.Value));
            Assert.Equal(expected, Matches(Translate(x => x.NullableInteger != 5), document));
            Assert.Equal(expected, new Data { NullableInteger = value }.NullableInteger != 5);
        }

        [Fact]
        public void RangeExcludesMissing()
        {
            Assert.False(Matches(Translate(x => x.NullableInteger > 1), Doc()));
            Assert.True(Matches(Translate(x => !(x.NullableInteger > 1)), Doc()));
            Assert.True(Matches(Translate(x => x.NullableInteger > 1), Doc(("nullableInteger", 2))));
        }

        [Fact]
        public void BooleanMembers()
        {
            AssertClause(x => x.IsEnabled, "{\"term\":{\"isEnabled\":true}}");
            AssertClause(x => !x.IsEnabled, "{\"bool\":{\"must_not\":[{\"term\":{\"isEnabled\":true}}]}}");
            AssertClause(x => x.IsEnabled == false, "{\"term\":{\"isEnabled\":false}}");
            AssertClause(x => x.IsEnabled != true, "{\"bool\":{\"must_not\":[{\"term\":{\"isEnabled\":true}}]}}");
            AssertClause(x => x.NullableFlag == true, "{\"term\":{\"nullableFlag\":true}}");
            AssertClause(x => !!x.IsEnabled, "{\"term\":{\"isEnabled\":true}}");
            var interpreted = Interpreter.ParsePredicate<Data>("!IsEnabled").Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), "{\"bool\":{\"must_not\":[{\"term\":{\"isEnabled\":true}}]}}");
        }

        [Fact]
        public void NullableAndEnumOperands()
        {
            AssertClause(x => x.NullableInteger == 3, "{\"term\":{\"nullableInteger\":3}}");
            AssertClause(x => x.NullableInteger >= 3, "{\"range\":{\"nullableInteger\":{\"gte\":3}}}");
            AssertClause(x => x.Status == Status.Active, "{\"term\":{\"status\":1}}");
            AssertClause(x => x.Status > Status.Active, "{\"range\":{\"status\":{\"gte\":2}}}");
            AssertClause(x => x.NullableStatus == Status.Suspended, "{\"term\":{\"nullableStatus\":2}}");
            AssertClause(x => x.NullableStatus == null, "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"nullableStatus\"}}]}}");
            var interpreted = Interpreter.ParsePredicate<Data>("Status = 'Active' and NullableStatus != 'Inactive'").Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted),
                "{\"term\":{\"status\":1}}", "{\"bool\":{\"must_not\":[{\"term\":{\"nullableStatus\":0}}]}}");
        }
    }
}
