using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kkts.Expressions.OpenSearch.Client;
using OpenSearch.Net;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    public abstract class EquivalenceTestBase
    {
        protected EquivalenceTestBase(OpenSearchFixture fixture) => Fixture = fixture;

        protected OpenSearchFixture Fixture { get; }

        protected static OpenSearchQueryTranslator<Item> Translator { get; } = new OpenSearchQueryTranslator<Item>(OpenSearchFixture.Mapping);

        /// <summary>
        /// Executes <paramref name="predicate"/> in OpenSearch (raw body and adapter) and compares hit ids with LINQ-to-Objects.
        /// <paramref name="reference"/> replaces the compiled predicate where LINQ-to-Objects would dereference null.
        /// </summary>
        protected async Task AssertSameIds(Expression<Func<Item, bool>> predicate, Func<Item, bool> reference = null)
        {
            var expected = Fixture.Items.Where(reference ?? predicate.Compile()).Select(x => x.Id).OrderBy(x => x).ToArray();
            var body = Translator.TranslateSearch(predicate, pagination: new Pagination { Limit = 100 });
            Assert.True(body.Succeeded, $"{predicate}: {string.Join("; ", body.Diagnostics)}");

            var raw = (await SearchRaw(body.Value)).OrderBy(x => x).ToArray();
            var adapter = (await SearchWithAdapter(body)).OrderBy(x => x).ToArray();

            Assert.True(expected.SequenceEqual(raw), Describe(predicate.ToString(), body.Value, expected, raw));
            Assert.True(expected.SequenceEqual(adapter), Describe(predicate.ToString() + " (adapter)", body.Value, expected, adapter));
        }

        /// <summary>Executes a search body and compares the ordered hit ids with the reference sequence.</summary>
        protected async Task AssertSameOrder(
            IEnumerable<Item> reference,
            Expression<Func<Item, bool>> predicate,
            OrderByClause orderBy,
            Pagination pagination)
        {
            var expected = reference.Select(x => x.Id).ToArray();
            var body = Translator.TranslateSearch(predicate, orderBy, pagination);
            Assert.True(body.Succeeded, string.Join("; ", body.Diagnostics));

            var raw = await SearchRaw(body.Value);
            var adapter = await SearchWithAdapter(body);

            Assert.True(expected.SequenceEqual(raw), Describe("ordered search", body.Value, expected, raw));
            Assert.True(expected.SequenceEqual(adapter), Describe("ordered search (adapter)", body.Value, expected, adapter));
        }

        protected async Task<int[]> SearchRaw(JsonObject body)
        {
            var response = await OpenSearchFixture.Ensure(Fixture.Client.LowLevel.SearchAsync<StringResponse>(
                OpenSearchFixture.IndexName, PostData.String(body.ToJsonString())));
            return JsonNode.Parse(response.Body)["hits"]["hits"].AsArray().Select(h => int.Parse((string)h["_id"])).ToArray();
        }

        private async Task<int[]> SearchWithAdapter(OpenSearchTranslationResult<JsonObject> body)
        {
            var response = await Fixture.Client.SearchAsync<Item>(s => s.Index(OpenSearchFixture.IndexName).Source(false).ApplyTranslation(body));
            Assert.True(response.IsValid, response.DebugInformation);
            return response.Hits.Select(h => int.Parse(h.Id)).ToArray();
        }

        private static string Describe(string name, JsonObject body, int[] expected, int[] actual) =>
            $"{name}{Environment.NewLine}Query:    {body.ToJsonString()}{Environment.NewLine}" +
            $"Expected: [{string.Join(", ", expected)}]{Environment.NewLine}Actual:   [{string.Join(", ", actual)}]";
    }
}
