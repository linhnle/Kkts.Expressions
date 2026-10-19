using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kkts.Expressions.OpenSearch.Client;
using OpenSearch.Client;
using OpenSearch.Net;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    // Mirrors the model and examples in docs/opensearch.md and the package READMEs.
    public class DocumentationExampleTest
    {
        public enum OrderStatus { Draft, Placed, Shipped }

        public class Order
        {
            public int Id { get; set; }
            public string Number { get; set; }
            public string Notes { get; set; }
            public OrderStatus Status { get; set; }
            public decimal Total { get; set; }
            public int ItemCount { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? ShippedAt { get; set; }
            public Customer Customer { get; set; }
            public Address ShippingAddress { get; set; }
            public List<OrderLine> Lines { get; set; }
            public List<string> Tags { get; set; }
        }

        public class Customer
        {
            public string Name { get; set; }
            public string Email { get; set; }
        }

        public class Address
        {
            public string City { get; set; }
        }

        public class OrderLine
        {
            public string Sku { get; set; }
            public int Quantity { get; set; }
            public decimal? Discount { get; set; }
        }

        private static OpenSearchMapping<Order> CreateMapping() =>
            OpenSearchMapping.For<Order>()
                .Field(x => x.Number, f => f.Name("order_no").Keyword())
                .Field(x => x.Notes, f => f.TextOnly())
                .Field(x => x.Customer.Email, f => f.Keyword().CaseInsensitive())
                .EnumFormat(EnumFormat.Name)
                .Nested(x => x.Lines)
                .Presence(x => x.ShippingAddress, "shippingAddressPresent")
                .Build();

        private static IReadOnlyDictionary<string, JsonNode> Examples()
        {
            var examples = new Dictionary<string, JsonNode>();

            // quick-start
            var predicate = Interpreter.ParsePredicate<Order>("Number = 'A-1' and Total >= 100").Result;
            var translator = new OpenSearchQueryTranslator<Order>();
            var result = translator.TranslatePredicate(predicate);
            if (!result.Succeeded) throw new OpenSearchTranslationException(result.Diagnostics);
            examples["quick-start"] = result.Value;

            // mapping
            var mapping = CreateMapping();
            var mapped = new OpenSearchQueryTranslator<Order>(mapping);
            examples["mapping"] = mapped.TranslatePredicate(x => x.Number == "A-1" && x.Status == OrderStatus.Placed).Value;

            // operators
            examples["operators"] = mapped.TranslatePredicate(x =>
                (x.Total > 10 && x.Total <= 500 || x.Id != 7) && new[] { 1, 2, 3 }.Contains(x.ItemCount)).Value;

            // string-functions
            examples["string-functions"] = mapped.TranslatePredicate(x =>
                x.Customer.Email.StartsWith("Ann@") && x.Customer.Name.Contains("o*n")).Value;

            // nulls
            examples["nulls"] = mapped.TranslatePredicate(x => x.ShippedAt == null || x.Customer.Name != "Ann").Value;

            // nested
            examples["nested"] = mapped.TranslatePredicate(x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2)).Value;

            // flattened
            examples["flattened"] = mapped.TranslatePredicate(x => x.Tags.Contains("gift")).Value;

            // presence
            examples["presence"] = mapped.TranslatePredicate(x => x.ShippingAddress == null).Value;

            // conversions
            examples["conversions"] = mapped.TranslatePredicate(x => x.ItemCount > 1.5 && x.ItemCount != 2.5).Value;

            // search
            var condition = new ConditionOptions
            {
                Where = "Status = 'Placed' and Total >= 100",
                OrderBy = "CreatedAt desc, Number"
            }.BuildCondition<Order>();
            var body = mapped.TranslateSearch(condition, new Pagination { PageSize = 20, Page = 3 });
            examples["search"] = body.Value;

            // diagnostics
            var failed = mapped.TranslatePredicate(x => x.Total + 1 > 2 && x.Notes == "late" && x.Tags.Contains(null));
            examples["diagnostics"] = new JsonArray(failed.Diagnostics.Select(d => (JsonNode)d.Code).ToArray());

            // client
            var request = new SearchDescriptor<Order>()
                .Index("orders")
                .ApplyTranslation(body);
            var client = new OpenSearchClient(new ConnectionSettings(new Uri("http://localhost:9200")));
            var json = JsonNode.Parse(client.RequestResponseSerializer.SerializeToString(request)).AsObject();
            if (!JsonNode.DeepEquals(json["query"], body.Value["query"]) || !JsonNode.DeepEquals(json["sort"], body.Value["sort"]))
                throw new InvalidOperationException("Adapter output differs from the raw body.");
            var queryOnly = new SearchRequest<Order>("orders")
            {
                Query = mapped.TranslatePredicate(x => x.Total > 10).ToQueryContainer(),
                Sort = mapped.TranslateSort("Total desc".TryBuildOrderByClause(typeof(Order)).Result).ToSorts()
            };
            examples["client"] = JsonNode.Parse(client.RequestResponseSerializer.SerializeToString(queryOnly));

            return examples;
        }

        private static string DocsPath(string relative)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "README.md")) || directory != null && !Directory.Exists(Path.Combine(directory.FullName, "docs")))
                directory = directory.Parent;
            return Path.Combine(directory.FullName, relative);
        }

        private static Dictionary<string, JsonNode> Documented(string relative)
        {
            var text = File.ReadAllText(DocsPath(relative));
            return Regex.Matches(text, @"<!-- example: (?<name>[a-z-]+) -->\s*```json\s*(?<json>.*?)```", RegexOptions.Singleline)
                .Cast<Match>()
                .ToDictionary(m => m.Groups["name"].Value, m => JsonNode.Parse(m.Groups["json"].Value));
        }

        [Theory]
        [InlineData("docs/opensearch.md")]
        public void DocumentedJson_MatchesTranslations(string document)
        {
            var examples = Examples();
            var documented = Documented(document);

            Assert.Equal(examples.Keys.OrderBy(k => k), documented.Keys.OrderBy(k => k));
            foreach (var example in documented)
            {
                Assert.True(JsonNode.DeepEquals(example.Value, examples[example.Key]),
                    $"{document} example '{example.Key}' differs.{Environment.NewLine}Actual: {examples[example.Key].ToJsonString()}");
            }
        }

        [Theory]
        [InlineData("README.md", "quick-start")]
        [InlineData("src/Kkts.Expressions.OpenSearch/README.md", "quick-start")]
        [InlineData("src/Kkts.Expressions.OpenSearch.Client/README.md", "client")]
        public void ReadmeJson_MatchesTranslations(string document, string example)
        {
            var documented = Documented(document);

            Assert.Equal(new[] { example }, documented.Keys);
            Assert.True(JsonNode.DeepEquals(documented[example], Examples()[example]));
        }
    }
}
