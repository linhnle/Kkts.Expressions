# Kkts.Expressions.OpenSearch

Translates [Kkts.Expressions](https://github.com/linhnle/Kkts.Expressions)
predicates, ordering clauses, and pagination into OpenSearch Query DSL JSON.
Translation is offline and deterministic. Constructs that cannot be expressed
with LINQ-to-Objects semantics fail with diagnostics; there is no script or
approximate fallback.

```sh
dotnet add package Kkts.Expressions.OpenSearch
```

```csharp
using Kkts.Expressions;
using Kkts.Expressions.OpenSearch;

var predicate = Interpreter.ParsePredicate<Order>("Number = 'A-1' and Total >= 100").Result;
var translator = new OpenSearchQueryTranslator<Order>();
var result = translator.TranslatePredicate(predicate);
if (!result.Succeeded) throw new OpenSearchTranslationException(result.Diagnostics);
string json = result.Value.ToJsonString();
```

<!-- example: quick-start -->
```json
{"bool":{"filter":[{"term":{"number.keyword":"A-1"}},{"range":{"total":{"gte":100}}}]}}
```

Configure field names, string field kinds, case-insensitive fields, enum
formats, value converters, `nested` paths, and object presence markers with
`OpenSearchMapping.For<T>()`. Use `TranslateSort`, `TranslatePagination`, and
`TranslateSearch` (including `Condition<T>`) for complete search bodies.

See the [OpenSearch guide](https://github.com/linhnle/Kkts.Expressions/blob/main/docs/opensearch.md)
for mapping configuration, supported constructs, null/case/nested semantics,
the diagnostics catalog, and known risks. For `OpenSearch.Client` integration,
use `Kkts.Expressions.OpenSearch.Client`.
