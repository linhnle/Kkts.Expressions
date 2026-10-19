using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace Kkts.Expressions.OpenSearch.Tests
{
    internal static class JsonAssert
    {
        public static void Equal(string expectedJson, JsonNode actual)
        {
            var expected = JsonNode.Parse(expectedJson);
            if (!JsonNode.DeepEquals(expected, actual))
                throw new XunitException($"JSON mismatch.\nExpected: {expected?.ToJsonString()}\nActual:   {actual?.ToJsonString()}");
        }

        /// <summary>Asserts the root bool query contains exactly the given filter clauses.</summary>
        public static void Filter(OpenSearchTranslationResult<JsonObject> result, params string[] clauses)
        {
            Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
            Equal("{\"bool\":{\"filter\":[" + string.Join(",", clauses) + "]}}", result.Value);
        }

        public static OpenSearchTranslationDiagnostic Fails<TOutput>(OpenSearchTranslationResult<TOutput> result, string code) where TOutput : JsonNode
        {
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(code, diagnostic.Code);
            return diagnostic;
        }

        public static string Canonical(string json) => JsonNode.Parse(json).ToJsonString(new JsonSerializerOptions());
    }
}
