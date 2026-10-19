# Kkts.Expressions.OpenSearch.Client

Applies [Kkts.Expressions.OpenSearch](https://www.nuget.org/packages/Kkts.Expressions.OpenSearch)
translations to `OpenSearch.Client` 2.x requests.

```sh
dotnet add package Kkts.Expressions.OpenSearch.Client
```

- `ToQueryContainer()` wraps a translated query in a `RawQuery`.
- `ToSorts()` converts a translated sort to `FieldSort` instances with order,
  missing, and nested paths.
- `ApplyTranslation(body)` sets `query`, `sort`, `from`, and `size` from a
  `TranslateSearch` result on a `SearchDescriptor<T>` or any `ISearchRequest`.

Failed translations throw `OpenSearchTranslationException` with the translation
diagnostics. Serialized requests are JSON-equivalent to the raw translation.

```csharp
using Kkts.Expressions;
using Kkts.Expressions.OpenSearch;
using Kkts.Expressions.OpenSearch.Client;
using OpenSearch.Client;

var response = await client.SearchAsync<Order>(s => s
    .Index("orders")
    .ApplyTranslation(body));

var request = new SearchRequest<Order>("orders")
{
    Query = mapped.TranslatePredicate(x => x.Total > 10).ToQueryContainer(),
    Sort = mapped.TranslateSort("Total desc".TryBuildOrderByClause(typeof(Order)).Result).ToSorts()
};
```

The second request serializes as:

<!-- example: client -->
```json
{"query":{"bool":{"filter":[{"range":{"total":{"gt":10}}}]}},"sort":[{"total":{"order":"desc","missing":"_last"}}]}
```

See the [OpenSearch guide](https://github.com/linhnle/Kkts.Expressions/blob/main/docs/opensearch.md)
for mapping configuration and semantics.
