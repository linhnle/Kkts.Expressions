using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class NormalizationTest : PredicateTestBase
    {
        private static int _invocations;

        public static int StaticValue = 11;

        public static int StaticProperty { get; } = 12;

        private static int CountedValue()
        {
            _invocations++;
            return 1;
        }

        private sealed class Holder
        {
            public int Value { get; set; } = 13;

            public Holder Inner { get; set; }

            public int Calls { get; private set; }

            public int Compute()
            {
                Calls++;
                return 1;
            }
        }

        public class QueryableData
        {
            public IQueryable<OrderLine> Lines { get; set; }
        }

        [Fact]
        public void QuotedLambdas_AreStripped()
        {
            // Queryable operators receive their element predicates as quoted lambdas.
            var mapping = OpenSearchMapping.For<QueryableData>().Nested(d => d.Lines).Build();

            var result = new OpenSearchQueryTranslator<QueryableData>(mapping).TranslatePredicate(x => x.Lines.Any(l => l.Sku == "a"));

            JsonAssert.Filter(result, "{\"nested\":{\"path\":\"lines\",\"query\":{\"term\":{\"lines.sku.keyword\":\"a\"}}}}");
        }

        [Fact]
        public void Closures_StaticMembers_AndChains_AreFolded()
        {
            var holder = new Holder { Inner = new Holder { Value = 14 } };
            AssertClause(x => x.Id == holder.Value, "{\"term\":{\"id\":13}}");
            AssertClause(x => x.Id == holder.Inner.Value, "{\"term\":{\"id\":14}}");
            AssertClause(x => x.Id == StaticValue, "{\"term\":{\"id\":11}}");
            AssertClause(x => x.Id == StaticProperty, "{\"term\":{\"id\":12}}");
            AssertClause(x => x.Name == string.Empty, "{\"term\":{\"name.keyword\":\"\"}}");
            AssertClause(x => x.Id == int.MaxValue, "{\"term\":{\"id\":2147483647}}");
        }

        [Fact]
        public void NullCapturedInstance_FailsWithoutThrowing()
        {
            Holder holder = null;
            AssertFails(x => x.Id == holder.Value, OpenSearchDiagnosticCodes.UnsupportedValue);
        }

        [Fact]
        public void BuiltInDateVariables_AreFolded()
        {
            var before = DateTime.Now;
            var result = new OpenSearchQueryTranslator<Data>().TranslatePredicate(Interpreter.ParsePredicate<Data>("Created >= $now").Result);

            Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
            var value = DateTime.Parse(result.Value["bool"]["filter"][0]["range"]["created"]["gte"].GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.True(value >= before.AddSeconds(-1));
        }

        [Fact]
        public void CollectionVariables_AreFolded()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("names", new[] { "a", "b" });
            resolver.TryAdd("prices", new List<decimal> { 1.5m });

            var names = new OpenSearchQueryTranslator<Data>().TranslatePredicate(Interpreter.ParsePredicate<Data>("Name in $names", variableResolver: resolver).Result);
            var prices = new OpenSearchQueryTranslator<Data>().TranslatePredicate(Interpreter.ParsePredicate<Data>("Price not in $prices", variableResolver: resolver).Result);

            JsonAssert.Filter(names, "{\"terms\":{\"name.keyword\":[\"a\",\"b\"]}}");
            JsonAssert.Filter(prices, "{\"bool\":{\"must_not\":[{\"terms\":{\"price\":[1.5]}}]}}");
        }

        [Fact]
        public void NullableEnumAndWideningConversions()
        {
            short small = 5;
            int? nullable = 6;
            AssertClause(x => x.NullableInteger == 3, "{\"term\":{\"nullableInteger\":3}}");
            AssertClause(x => x.Id == small, "{\"term\":{\"id\":5}}");
            AssertClause(x => x.NullableInteger == nullable, "{\"term\":{\"nullableInteger\":6}}");
            AssertClause(x => x.IntValue == 5000000000L, "{\"match_none\":{}}");
            AssertClause(x => x.IntValue != 5000000000L, "{\"match_all\":{}}");
            AssertClause(x => x.IntValue < 5000000000L, "{\"exists\":{\"field\":\"intValue\"}}");
            AssertClause(x => x.ShortValue >= 3, "{\"range\":{\"shortValue\":{\"gte\":3}}}");
            AssertClause(x => x.ByteValue == 3, "{\"term\":{\"byteValue\":3}}");
            AssertClause(x => (long)x.Status == 1L, "{\"term\":{\"status\":1}}");
            AssertClause(x => x.NullableStatus != Status.Active, "{\"bool\":{\"must_not\":[{\"term\":{\"nullableStatus\":1}}]}}");
        }

        [Fact]
        public void CapturedMethodCall_FailsWithoutInvocation()
        {
            var holder = new Holder();
            _invocations = 0;

            var staticCall = AssertFails(x => x.Id == CountedValue(), OpenSearchDiagnosticCodes.UnsupportedValue);
            var instanceCall = AssertFails(x => x.Id == holder.Compute(), OpenSearchDiagnosticCodes.UnsupportedValue);
            AssertFails(x => x.Id == new[] { 1, 2 }.Length, OpenSearchDiagnosticCodes.UnsupportedValue);
            AssertFails(x => x.Id == new List<int> { 1 }.Count, OpenSearchDiagnosticCodes.UnsupportedValue);

            Assert.Equal(0, _invocations);
            Assert.Equal(0, holder.Calls);
            Assert.Contains("CountedValue", staticCall.ExpressionText);
            Assert.Contains("Compute", instanceCall.ExpressionText);
        }

        [Fact]
        public void TranslationDoesNotModifyInput()
        {
            Expression<Func<Data, bool>> predicate = x => x.Id == 1 && x.Lines.Any(l => l.Sku == "a");
            var before = predicate.ToString();

            Translate(predicate);

            Assert.Equal(before, predicate.ToString());
        }
    }
}
