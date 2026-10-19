using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using OpenSearch.Client;
using OpenSearch.Net;

namespace Kkts.Expressions.OpenSearch.Client
{
    /// <summary>Converts <c>Kkts.Expressions.OpenSearch</c> translations into <c>OpenSearch.Client</c> request objects.</summary>
    public static class OpenSearchClientExtensions
    {
        /// <summary>Wraps a translated query (for example from <c>TranslatePredicate</c>) in a <see cref="QueryContainer"/>.</summary>
        /// <exception cref="OpenSearchTranslationException">The translation failed.</exception>
        public static QueryContainer ToQueryContainer(this OpenSearchTranslationResult<JsonObject> query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return ToQueryContainer(query.GetValueOrThrow());
        }

        /// <summary>Converts a translated sort array (from <c>TranslateSort</c>) into client sort definitions.</summary>
        /// <exception cref="OpenSearchTranslationException">The translation failed.</exception>
        public static IList<ISort> ToSorts(this OpenSearchTranslationResult<JsonArray> sort)
        {
            if (sort == null) throw new ArgumentNullException(nameof(sort));
            return ToSorts(sort.GetValueOrThrow());
        }

        /// <summary>Applies the <c>query</c>, <c>sort</c>, <c>from</c>, and <c>size</c> of a translated search body (from <c>TranslateSearch</c>).</summary>
        /// <exception cref="OpenSearchTranslationException">The translation failed.</exception>
        public static TRequest ApplyTranslation<TRequest>(this TRequest request, OpenSearchTranslationResult<JsonObject> searchBody)
            where TRequest : class, ISearchRequest
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (searchBody == null) throw new ArgumentNullException(nameof(searchBody));
            var body = searchBody.GetValueOrThrow();
            if (!body.ContainsKey("query"))
                throw new ArgumentException("The translation is not a search body; use TranslateSearch, or ToQueryContainer for a predicate translation.", nameof(searchBody));

            ISearchRequest target = request;
            target.Query = ToQueryContainer((JsonObject)body["query"]);
            if (body["sort"] is JsonArray sort) target.Sort = ToSorts(sort);
            if (body["from"] is JsonNode from) target.From = from.GetValue<int>();
            if (body["size"] is JsonNode size) target.Size = size.GetValue<int>();
            return request;
        }

        /// <inheritdoc cref="ApplyTranslation{TRequest}(TRequest, OpenSearchTranslationResult{JsonObject})"/>
        public static SearchDescriptor<T> ApplyTranslation<T>(this SearchDescriptor<T> descriptor, OpenSearchTranslationResult<JsonObject> searchBody)
            where T : class =>
            ApplyTranslation<SearchDescriptor<T>>(descriptor, searchBody);

        private static QueryContainer ToQueryContainer(JsonObject query) =>
            new QueryContainer(new RawQuery(query.ToJsonString()));

        private static IList<ISort> ToSorts(JsonArray sort)
        {
            var sorts = new List<ISort>(sort.Count);
            foreach (var entry in sort)
            {
                foreach (var key in (JsonObject)entry)
                {
                    var options = (JsonObject)key.Value;
                    sorts.Add(new FieldSort
                    {
                        Field = key.Key,
                        Order = (string)options["order"] == "desc" ? SortOrder.Descending : SortOrder.Ascending,
                        Missing = (string)options["missing"],
                        Nested = ToNestedSort(options["nested"] as JsonObject)
                    });
                }
            }

            return sorts;
        }

        private static NestedSort ToNestedSort(JsonObject nested) =>
            nested == null ? null : new NestedSort
            {
                Path = (string)nested["path"],
                Nested = ToNestedSort(nested["nested"] as JsonObject)
            };
    }
}
