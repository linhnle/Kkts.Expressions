using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OpenSearch.Client;
using OpenSearch.Net;
using Testcontainers.OpenSearch;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    [CollectionDefinition(Name)]
    public sealed class OpenSearchCollection : ICollectionFixture<OpenSearchFixture>
    {
        public const string Name = "OpenSearch";
    }

    public sealed class OpenSearchFixture : IAsyncLifetime
    {
        // OpenSearch 2.19.1, pinned by digest.
        public const string Image = "opensearchproject/opensearch@sha256:72fe2fc84be8295906b8efca020b46c58ed45c8da60cd9b8b49e1991e38e89a4";
        public const string IndexName = "items";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly OpenSearchContainer _container = new OpenSearchBuilder(Image)
            .WithSecurityEnabled(false)
            .WithEnvironment("OPENSEARCH_JAVA_OPTS", "-Xms512m -Xmx512m")
            .Build();

        public OpenSearchClient Client { get; private set; }

        public IReadOnlyList<Item> Items { get; } = SeedData.Create();

        public static OpenSearchMapping<Item> Mapping { get; } = OpenSearchMapping.For<Item>()
            .Field(x => x.Code, f => f.Keyword())
            .Field(x => x.Title, f => f.Text("raw"))
            .Field(x => x.Email, f => f.Keyword().CaseInsensitive())
            .Field(x => x.Body, f => f.TextOnly())
            .Nested(x => x.Customer)
            .Presence(x => x.Meta, "metaPresent")
            .Nested(x => x.Lines, l => l.Presence(e => e.Product, "lines.productPresent").Nested(e => e.Taxes))
            .Build();

        public async Task InitializeAsync()
        {
            try
            {
                await _container.StartAsync();
                Client = new OpenSearchClient(new ConnectionSettings(new Uri(_container.GetConnectionString()))
                    .DisableDirectStreaming()
                    .DefaultIndex(IndexName));
                await Ensure(Client.LowLevel.Indices.CreateAsync<StringResponse>(IndexName, PostData.String(IndexMapping)));
                await Ensure(Client.LowLevel.BulkAsync<StringResponse>(IndexName, PostData.String(BulkBody()), new BulkRequestParameters { Refresh = Refresh.True }));
            }
            catch
            {
                await _container.DisposeAsync();
                throw;
            }
        }

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        public static JsonObject ToDocument(Item item)
        {
            var document = JsonSerializer.SerializeToNode(item, SerializerOptions).AsObject();
            document["metaPresent"] = item.Meta != null;
            if (item.Meta != null && item.IndexMetaAsEmptyObject) document["meta"] = new JsonObject();
            if (item.Lines != null)
            {
                var lines = document["lines"].AsArray();
                for (var i = 0; i < item.Lines.Count; i++)
                    lines[i]["productPresent"] = item.Lines[i].Product != null;
            }

            return document;
        }

        private string BulkBody()
        {
            var builder = new StringBuilder();
            foreach (var item in Items)
            {
                builder.Append("{\"index\":{\"_id\":\"").Append(item.Id).Append("\"}}\n");
                builder.Append(ToDocument(item).ToJsonString()).Append('\n');
            }

            return builder.ToString();
        }

        public static async Task<StringResponse> Ensure(Task<StringResponse> request)
        {
            var response = await request;
            if (!response.Success || response.Body.Contains("\"errors\":true"))
                throw new InvalidOperationException($"OpenSearch request failed: {response.DebugInformation}");
            return response;
        }

        private static string Text(string keywordSubfield = "keyword") =>
            "{\"type\":\"text\",\"fields\":{\"" + keywordSubfield + "\":{\"type\":\"keyword\",\"ignore_above\":256}}}";

        private static readonly string IndexMapping = new JsonObject
        {
            ["settings"] = new JsonObject { ["number_of_shards"] = 1, ["number_of_replicas"] = 0 },
            ["mappings"] = new JsonObject
            {
                ["dynamic"] = "strict",
                ["properties"] = new JsonObject
                {
                    ["id"] = Type("integer"),
                    ["code"] = Type("keyword"),
                    ["name"] = JsonNode.Parse(Text()),
                    ["title"] = JsonNode.Parse(Text("raw")),
                    ["email"] = Type("keyword"),
                    ["body"] = Type("text"),
                    ["status"] = Type("integer"),
                    ["count"] = Type("integer"),
                    ["big"] = Type("long"),
                    ["rating"] = Type("integer"),
                    ["weight"] = Type("double"),
                    ["price"] = Type("double"),
                    ["created"] = Type("date"),
                    ["closed"] = Type("date"),
                    ["customer"] = new JsonObject
                    {
                        ["type"] = "nested",
                        ["properties"] = new JsonObject { ["name"] = JsonNode.Parse(Text()), ["age"] = Type("integer") }
                    },
                    ["metaPresent"] = Type("boolean"),
                    ["meta"] = new JsonObject
                    {
                        ["properties"] = new JsonObject { ["label"] = JsonNode.Parse(Text()), ["score"] = Type("integer") }
                    },
                    ["lines"] = new JsonObject
                    {
                        ["type"] = "nested",
                        ["properties"] = new JsonObject
                        {
                            ["sku"] = JsonNode.Parse(Text()),
                            ["quantity"] = Type("integer"),
                            ["discount"] = Type("double"),
                            ["productPresent"] = Type("boolean"),
                            ["product"] = new JsonObject { ["properties"] = new JsonObject { ["name"] = JsonNode.Parse(Text()) } },
                            ["taxes"] = new JsonObject
                            {
                                ["type"] = "nested",
                                ["properties"] = new JsonObject { ["code"] = JsonNode.Parse(Text()), ["rate"] = Type("double") }
                            }
                        }
                    },
                    ["parts"] = new JsonObject
                    {
                        ["properties"] = new JsonObject { ["kind"] = JsonNode.Parse(Text()), ["size"] = Type("integer") }
                    },
                    ["tags"] = JsonNode.Parse(Text()),
                    ["numbers"] = Type("integer"),
                    ["values"] = Type("integer")
                }
            }
        }.ToJsonString();

        private static JsonObject Type(string type) => new JsonObject { ["type"] = type };
    }
}
