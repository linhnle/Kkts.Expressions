using System;
using System.Linq;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Client;
using OpenSearch.Client;
using OpenSearch.Net;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class ClientAdapterTest
    {
        private static readonly OpenSearchClient Client = new OpenSearchClient(
            new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), new InMemoryConnection())
                .DefaultIndex("data"));

        private static readonly OpenSearchMapping<Data> Mapping = OpenSearchMapping.For<Data>()
            .Nested(x => x.Customer, c => c.Nested(e => e.Address))
            .Nested(x => x.Lines)
            .Build();

        private static JsonObject Serialize(object request) =>
            JsonNode.Parse(Client.RequestResponseSerializer.SerializeToString(request)).AsObject();

        private static void AssertEquivalent(JsonObject expected, JsonObject actual)
        {
            foreach (var part in new[] { "query", "sort", "from", "size" })
            {
                Assert.True(JsonNode.DeepEquals(expected[part], actual[part]),
                    $"{part} differs.{Environment.NewLine}Expected: {expected[part]?.ToJsonString()}{Environment.NewLine}Actual:   {actual[part]?.ToJsonString()}");
            }
        }

        [Fact]
        public void SearchDescriptor_IsEquivalentToRawBody()
        {
            var translator = new OpenSearchQueryTranslator<Data>(Mapping);
            var body = translator.TranslateSearch(
                x => x.Name == "a" && x.Customer.Age > 3 && x.Lines.Any(l => l.Sku == "A" && l.Quantity > 1),
                "Name desc, Customer.Address.City, Id".TryBuildOrderByClause(typeof(Data)).Result,
                new Pagination { Offset = 40, Limit = 20 });

            var descriptor = new SearchDescriptor<Data>().ApplyTranslation(body);

            var serialized = Serialize(descriptor);
            AssertEquivalent(body.Value, serialized);
            Assert.Equal(40, (int)serialized["from"]);
            Assert.Equal("customer.address", (string)serialized["sort"][1]["customer.address.city.keyword"]["nested"]["nested"]["path"]);
        }

        [Fact]
        public void SearchRequest_IsEquivalentToRawBody()
        {
            var condition = new ConditionOptions { Where = "Id in [1, 2] and Name.startsWith('x')", OrderBy = "Id desc" }.BuildCondition<Data>();
            var body = new OpenSearchQueryTranslator<Data>().TranslateSearch(condition, new Pagination { Limit = 5 });

            var request = new SearchRequest<Data>().ApplyTranslation(body);

            AssertEquivalent(body.Value, Serialize(request));
        }

        [Fact]
        public void BodyWithoutOptionalParts_LeavesThemUnset()
        {
            var body = new OpenSearchQueryTranslator<Data>().TranslateSearch(x => x.Id == 1);
            ISearchRequest request = new SearchRequest<Data> { From = 3, Size = 7 }.ApplyTranslation(body);

            Assert.Null(request.Sort);
            Assert.Equal(3, request.From);
            Assert.Equal(7, request.Size);
            Assert.True(JsonNode.DeepEquals(body.Value["query"], Serialize(request)["query"]));
        }

        [Fact]
        public void QueryContainerAndSorts()
        {
            var translator = new OpenSearchQueryTranslator<Data>(Mapping);
            var query = translator.TranslatePredicate(x => x.Customer.Name != "a" || x.Price < 3m);
            var sort = translator.TranslateSort("Customer.Age desc, Price".TryBuildOrderByClause(typeof(Data)).Result);

            var request = new SearchRequest<Data> { Query = query.ToQueryContainer(), Sort = sort.ToSorts() };

            var serialized = Serialize(request);
            Assert.True(JsonNode.DeepEquals(query.Value, serialized["query"]), serialized["query"].ToJsonString());
            Assert.True(JsonNode.DeepEquals(sort.Value, serialized["sort"]), serialized["sort"].ToJsonString());
            var first = (FieldSort)request.Sort[0];
            Assert.Equal(SortOrder.Descending, first.Order);
            Assert.Equal("_last", first.Missing);
            Assert.Equal("customer", first.Nested.Path.Name);
        }

        [Fact]
        public void FailedTranslations_Throw()
        {
            var translator = new OpenSearchQueryTranslator<Data>();
            var failedQuery = translator.TranslatePredicate(x => x.Id + 1 == 2);
            var failedSort = translator.TranslateSort("Customer".TryBuildOrderByClause(typeof(Data)).Result);
            var failedBody = translator.TranslateSearch(x => x.Id == 1, pagination: new Pagination { Offset = 9999, Limit = 10 });

            var exception = Assert.Throws<OpenSearchTranslationException>(() => failedQuery.ToQueryContainer());
            Assert.Equal(OpenSearchDiagnosticCodes.UnsupportedExpression, exception.Diagnostics.Single().Code);
            Assert.Throws<OpenSearchTranslationException>(() => failedSort.ToSorts());
            exception = Assert.Throws<OpenSearchTranslationException>(() => new SearchDescriptor<Data>().ApplyTranslation(failedBody));
            Assert.Equal(OpenSearchDiagnosticCodes.ResultWindowExceeded, exception.Diagnostics.Single().Code);
            Assert.Throws<OpenSearchTranslationException>(() => new SearchRequest<Data>().ApplyTranslation(failedBody));
        }

        [Fact]
        public void InvalidArguments()
        {
            var predicate = new OpenSearchQueryTranslator<Data>().TranslatePredicate(x => x.Id == 1);
            Assert.Throws<ArgumentException>(() => new SearchRequest<Data>().ApplyTranslation(predicate));
            Assert.Throws<ArgumentNullException>(() => new SearchRequest<Data>().ApplyTranslation(null));
            Assert.Throws<ArgumentNullException>(() => ((OpenSearchTranslationResult<JsonObject>)null).ToQueryContainer());
        }
    }
}
