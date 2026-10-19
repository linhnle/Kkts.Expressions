using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OpenSearch.Net;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    [Trait("Provider", "OpenSearch")]
    [Collection(OpenSearchCollection.Name)]
    public sealed class FixtureSmokeTest
    {
        private readonly OpenSearchFixture _fixture;

        public FixtureSmokeTest(OpenSearchFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task StartsIndexesAndRefreshes()
        {
            var count = await OpenSearchFixture.Ensure(_fixture.Client.LowLevel.CountAsync<StringResponse>(OpenSearchFixture.IndexName, PostData.String("{}")));
            Assert.Equal(_fixture.Items.Count, (int)JsonNode.Parse(count.Body)["count"]);

            var mapping = await OpenSearchFixture.Ensure(_fixture.Client.LowLevel.Indices.GetMappingAsync<StringResponse>(OpenSearchFixture.IndexName));
            var properties = JsonNode.Parse(mapping.Body)[OpenSearchFixture.IndexName]["mappings"]["properties"];
            Assert.Equal("nested", (string)properties["lines"]["properties"]["taxes"]["type"]);
            Assert.Equal("boolean", (string)properties["metaPresent"]["type"]);
            Assert.Equal("keyword", (string)properties["title"]["fields"]["raw"]["type"]);

            var meta = _fixture.Items.Select(OpenSearchFixture.ToDocument).Select(d => d["meta"]?.ToJsonString() ?? "null").ToArray();
            Assert.Contains("{}", meta);
            Assert.Contains("{\"label\":null,\"score\":null}", meta);
            Assert.Contains("null", meta);
        }
    }
}
