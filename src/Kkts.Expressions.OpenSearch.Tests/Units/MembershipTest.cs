using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class MembershipTest : PredicateTestBase
    {
        private static OpenSearchTranslationResult<JsonObject> Parse(string text, VariableResolver resolver = null) =>
            new OpenSearchQueryTranslator<Data>().TranslatePredicate(Interpreter.ParsePredicate<Data>(text, variableResolver: resolver).Result);

        [Fact]
        public void BasicMembership_DeduplicatesInFirstSeenOrder()
        {
            JsonAssert.Filter(Parse("Id in [1, 2, 2]"), "{\"terms\":{\"id\":[1,2]}}");
            JsonAssert.Filter(Parse("Id in [3, 1, 3, 2]"), "{\"terms\":{\"id\":[3,1,2]}}");
            JsonAssert.Filter(Parse("Name in ['b', 'a', 'b']"), "{\"terms\":{\"name.keyword\":[\"b\",\"a\"]}}");
        }

        [Fact]
        public void NotIn_IsMustNot()
        {
            JsonAssert.Filter(Parse("Id not in [1, 2]"), "{\"bool\":{\"must_not\":[{\"terms\":{\"id\":[1,2]}}]}}");
        }

        [Fact]
        public void EmptyList()
        {
            JsonAssert.Filter(Parse("Id in []"), "{\"match_none\":{}}");
            JsonAssert.Filter(Parse("Id not in []"), "{\"match_all\":{}}");
        }

        [Fact]
        public void ListSources_ConstantCapturedAndVariable()
        {
            var captured = new List<int> { 4, 5 };
            var array = new[] { 6, 7 };
            IReadOnlyCollection<int> readOnly = new HashSet<int> { 8 };
            AssertClause(x => captured.Contains(x.Id), "{\"terms\":{\"id\":[4,5]}}");
            AssertClause(x => array.Contains(x.Id), "{\"terms\":{\"id\":[6,7]}}");
            AssertClause(x => Enumerable.Contains(array, x.Id), "{\"terms\":{\"id\":[6,7]}}");
            AssertClause(x => readOnly.Contains(x.Id), "{\"terms\":{\"id\":[8]}}");
            AssertClause(x => new[] { 1, 2 }.Contains(x.Id), "{\"terms\":{\"id\":[1,2]}}");

            var resolver = new VariableResolver();
            resolver.TryAdd("ids", new List<int> { 9, 10 });
            JsonAssert.Filter(Parse("Id in $ids", resolver), "{\"terms\":{\"id\":[9,10]}}");
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void NullElement_MatchesMissing(int? value, bool expected)
        {
            var result = Parse("NullableInteger in [null, 1]");
            var document = value == null ? Doc() : Doc(("nullableInteger", value.Value));

            Assert.Equal(expected, Matches(result, document));
            Assert.Equal(expected, new int?[] { null, 1 }.Contains(value));
        }

        [Fact]
        public void NotInWithNulls()
        {
            Assert.True(Matches(Parse("NullableInteger not in [1, 2]"), Doc()));
            Assert.False(Matches(Parse("NullableInteger not in [null, 1]"), Doc()));
            Assert.True(Matches(Parse("NullableInteger not in [null, 1]"), Doc(("nullableInteger", 2))));
            Assert.False(Matches(Parse("NullableInteger not in [null, 1]"), Doc(("nullableInteger", 1))));
            JsonAssert.Filter(Parse("NullableInteger in [null]"), "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"nullableInteger\"}}]}}");
        }

        [Fact]
        public void CaseInsensitiveMembership_UsesShouldOfTerms()
        {
            var mapping = OpenSearchMapping.For<Data>().Field(x => x.Name, f => f.CaseInsensitive()).Build();
            var names = new[] { "a", "B" };

            AssertClause(x => names.Contains(x.Name),
                "{\"bool\":{\"should\":[{\"term\":{\"name.keyword\":{\"value\":\"a\",\"case_insensitive\":true}}},{\"term\":{\"name.keyword\":{\"value\":\"B\",\"case_insensitive\":true}}}],\"minimum_should_match\":1}}",
                mapping);
            Assert.True(Matches(Translate(x => names.Contains(x.Name), mapping), Doc(("name.keyword", "b"))));
        }

        [Fact]
        public void EnumAndGuidMembership()
        {
            var key = Guid.Parse("3F2504E0-4F89-11D3-9A0C-0305E82C3301");
            JsonAssert.Filter(Parse("Status in ['Active', 'Suspended']"), "{\"terms\":{\"status\":[1,2]}}");
            AssertClause(x => new[] { key }.Contains(x.Key), "{\"terms\":{\"key\":[\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"]}}");
        }
    }
}
