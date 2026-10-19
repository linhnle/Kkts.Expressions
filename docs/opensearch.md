# Translating expressions to OpenSearch queries

`Kkts.Expressions.OpenSearch` translates the LINQ predicates, ordering clauses,
and pagination produced by Kkts.Expressions into
[OpenSearch Query DSL](https://opensearch.org/docs/latest/query-dsl/) JSON.
`Kkts.Expressions.OpenSearch.Client` applies those translations to
`OpenSearch.Client` 2.x requests.

Translation is deterministic and offline. The translator never contacts a
cluster, never inspects an index mapping, and never falls back to scripts or
approximations. When a construct cannot be expressed with identical semantics,
translation fails with diagnostics instead of returning a query.

- [Installation](#installation)
- [Quick start](#quick-start)
- [Mapping configuration](#mapping-configuration)
- [Supported constructs](#supported-constructs)
- [Null, case, and string semantics](#null-case-and-string-semantics)
- [Nested documents and collections](#nested-documents-and-collections)
- [Presence markers for object null checks](#presence-markers-for-object-null-checks)
- [Numeric conversions](#numeric-conversions)
- [Sorting, pagination, and search bodies](#sorting-pagination-and-search-bodies)
- [OpenSearch.Client adapter](#opensearchclient-adapter)
- [Diagnostics](#diagnostics)
- [Risks and limitations](#risks-and-limitations)

## Installation

```sh
dotnet add package Kkts.Expressions.OpenSearch
# Optional: OpenSearch.Client 2.x integration
dotnet add package Kkts.Expressions.OpenSearch.Client
```

```csharp
using Kkts.Expressions;
using Kkts.Expressions.OpenSearch;
using Kkts.Expressions.OpenSearch.Client; // adapter only
```

The core package targets `netstandard2.0`, depends only on `Kkts.Expressions`
and `System.Text.Json`, and returns `System.Text.Json.Nodes` values.

The examples in this guide use this model:

```csharp
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

public class Customer { public string Name { get; set; } public string Email { get; set; } }
public class Address { public string City { get; set; } }
public class OrderLine { public string Sku { get; set; } public int Quantity { get; set; } public decimal? Discount { get; set; } }
```

## Quick start

Any predicate works as input: a typed lambda, `Interpreter.ParsePredicate`,
structured `Filter`/`FilterGroup` lists, `FilterNode` trees, or an
`ExpressionQueryContext` with a `QueryPolicy`. Equivalent predicates from
different entry points produce identical JSON, and translation never changes
or relaxes the outcome of policy validation, which runs before translation.

```csharp
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

The top-level query is always a `bool` query with clauses in `filter` context,
so results are not relevance-scored. Results are all-or-nothing: a result
either has a `Value` or a non-empty `Diagnostics` list. `GetValueOrThrow()` and
`ToJsonString()` throw `OpenSearchTranslationException` for failed results.

## Mapping configuration

Without configuration, `OpenSearchMapping<T>.Default` assumes these mappings:

- Field names are camelCase CLR member names, separated with `.` for object
  members. `[JsonPropertyName]` attributes are honoured.
- Strings are `text` fields with a `keyword` subfield. Exact operations
  (equality, membership, string functions, sorting) use `<field>.keyword`, and
  null checks use the base field.
- Enums are indexed as their underlying numbers.
- The maximum result window is 10,000.

Override individual members or the defaults with a builder:

```csharp
var mapping = OpenSearchMapping.For<Order>()
    .Field(x => x.Number, f => f.Name("order_no").Keyword())
    .Field(x => x.Notes, f => f.TextOnly())
    .Field(x => x.Customer.Email, f => f.Keyword().CaseInsensitive())
    .EnumFormat(EnumFormat.Name)
    .Nested(x => x.Lines)
    .Presence(x => x.ShippingAddress, "shippingAddressPresent")
    .Build();
var mapped = new OpenSearchQueryTranslator<Order>(mapping);
var query = mapped.TranslatePredicate(x => x.Number == "A-1" && x.Status == OrderStatus.Placed);
```

<!-- example: mapping -->
```json
{"bool":{"filter":[{"term":{"order_no":"A-1"}},{"term":{"status":"Placed"}}]}}
```

Builder options:

| Option | Purpose |
| --- | --- |
| `NamingPolicy(JsonNamingPolicy)` | Field naming for unmapped members; `null` keeps CLR names. |
| `DefaultStringField(StringFieldKind)` | `StringFieldKind.Keyword`, `StringFieldKind.Text("raw")`, or `StringFieldKind.TextOnly`. |
| `EnumFormat(EnumFormat)` | `Number` (default) or `Name`. Ordered enum comparisons and enum sorting require `Number`. |
| `Converter<TValue>(Func<TValue, JsonNode>)` | Formats values of a type, such as `TimeSpan`, which has no default format. |
| `MaxResultWindow(int)` | Your index's `index.max_result_window`. |
| `Field(selector, f => ...)` | Per-field `Name`, `Keyword`, `Text(subfield)`, `TextOnly`, `CaseInsensitive`, `EnumFormat`, and `Converter`. |
| `Nested(selector[, configure])` | Marks an object or collection as an OpenSearch `nested` path. The `configure` callback maps members inside it, including inner nested paths. |
| `Presence(selector, markerPath)` | Registers a Boolean presence marker for an ordinary object. |

Selectors must be member chains from the parameter. Duplicate registrations
throw `ArgumentException`. `Build()` validates the configuration and throws
`InvalidOperationException` for conflicts, such as duplicate field paths or
markers on nested chains. A built mapping is immutable and thread-safe.
`OpenSearchQueryTranslator` is stateless.

## Supported constructs

| Construct | Translation |
| --- | --- |
| `&&`, `\|\|`, `!` | `bool.filter`, `bool.should` with `minimum_should_match: 1`, `bool.must_not`; directly nested AND/OR are flattened |
| `==`, `!=` with a value | `term`; `must_not` of `term` |
| `<`, `<=`, `>`, `>=` | `range` with `lt`, `lte`, `gt`, `gte` |
| `== null`, `!= null` (scalars) | `must_not` `exists`; `exists` |
| `values.Contains(x.Field)`, `in`, `not in` | `terms` over non-null distinct values; a null element adds a "missing" disjunct; empty list is `match_none` / `match_all` |
| `StartsWith`, `EndsWith`, `Contains` | `prefix`, `wildcard` `*text`, `wildcard` `*text*` |
| constant `true` / `false` | `match_all` / `match_none` |
| `nested.Any(e => ...)`, `nested.Any()` | one `nested` query, so conditions apply to the same element |
| `scalarArray.Contains(value)`, `flattened.Any(e => single positive leaf)` | `term`, `terms`, `range`, `prefix`, or `wildcard` on the flattened field |

Values may be constants, captured variables or members, interpreter variables
such as `$now`, and collection variables. Value expressions are never
executed: method calls, constructors, and operations between captured values
fail with `opensearch-unsupported-value`. Supported value types are numbers,
`bool`, `string`, `char`, `Guid`, `DateTime`, `DateTimeOffset`, enums, and
types with a registered converter. Numbers and dates are formatted with the
invariant culture, and dates use the round-trip `"o"` format.

```csharp
mapped.TranslatePredicate(x =>
    (x.Total > 10 && x.Total <= 500 || x.Id != 7) && new[] { 1, 2, 3 }.Contains(x.ItemCount));
```

<!-- example: operators -->
```json
{"bool":{"filter":[{"bool":{"should":[{"bool":{"filter":[{"range":{"total":{"gt":10}}},{"range":{"total":{"lte":500}}}]}},{"bool":{"must_not":[{"term":{"id":7}}]}}],"minimum_should_match":1}},{"terms":{"itemCount":[1,2,3]}}]}}
```

The translator does not support arithmetic, string concatenation, comparisons
between two fields, other methods (`ToLower`, `string.IsNullOrEmpty`,
`Equals`), conditional/coalesce expressions, type tests, or members of
scalars such as `Name.Length` or `CreatedAt.Year`. Computed public schema
fields, such as `x.Name + x.Code`, also fail. All of these produce diagnostics.

## Null, case, and string semantics

**Nulls.** Translation follows LINQ-to-Objects semantics. An absent field,
an explicit `null`, and an empty array all count as missing:

- `field == null` becomes `must_not exists`.
- `field != value` becomes `must_not term`, so it also matches documents where
  the field is missing.
- `in` with a null element also matches missing values.
- Comparing a value with `null` using `<`, `<=`, `>`, or `>=` is always false
  and becomes `match_none`.

```csharp
mapped.TranslatePredicate(x => x.ShippedAt == null || x.Customer.Name != "Ann");
```

<!-- example: nulls -->
```json
{"bool":{"filter":[{"bool":{"should":[{"bool":{"must_not":[{"exists":{"field":"shippedAt"}}]}},{"bool":{"must_not":[{"term":{"customer.name.keyword":"Ann"}}]}}],"minimum_should_match":1}}]}}
```

**Case.** String equality, membership, and functions are case-sensitive
(ordinal), like LINQ-to-Objects. A field configured with `CaseInsensitive()`
emits `"case_insensitive": true` on `term`, `prefix`, and `wildcard`.
Membership on such a field becomes a `should` of case-insensitive `term`s,
because `terms` has no case-insensitive option.

**String functions.** `*`, `?`, and `\` in the operand are escaped so they
match literally. An empty operand matches every non-null value, as in .NET,
and becomes `exists`. A null operand fails.

```csharp
mapped.TranslatePredicate(x => x.Customer.Email.StartsWith("Ann@") && x.Customer.Name.Contains("o*n"));
```

<!-- example: string-functions -->
```json
{"bool":{"filter":[{"prefix":{"customer.email":{"value":"Ann@","case_insensitive":true}}},{"wildcard":{"customer.name.keyword":"*o\\*n*"}}]}}
```

**Text-only fields.** A `TextOnly()` field cannot be used for exact
operations or sorting; these fail with `opensearch-text-field-exact-match`.
Null checks still work.

## Nested documents and collections

Fields under a `Nested` object are wrapped in a `nested` query. Negation is
applied outside the `nested` query, matching LINQ semantics.
`Any(predicate)` over a nested collection becomes a single `nested` query, so
every condition applies to the same element. `Any()` becomes `nested` with
`match_all`. Nested paths inside other nested paths produce nested `nested`
queries with full field paths. A path already opened by an enclosing `Any`
is not wrapped again.

```csharp
mapped.TranslatePredicate(x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2));
```

<!-- example: nested -->
```json
{"bool":{"filter":[{"nested":{"path":"lines","query":{"bool":{"filter":[{"term":{"lines.sku.keyword":"A"}},{"range":{"lines.quantity":{"gt":2}}}]}}}}]}}
```

A null check on a nested object, such as `x.Customer == null`, becomes
`must_not nested match_all`.

**Flattened (non-nested) collections.** OpenSearch flattens arrays of objects
and scalars into multi-valued fields and does not keep each element's values
together. Only shapes that the flattened representation answers exactly are
translated:

- `Contains(value)` with a non-null value.
- `Any(e => <one positive leaf>)`, where the leaf is a comparison, string
  function, or membership with non-null candidates.

```csharp
mapped.TranslatePredicate(x => x.Tags.Contains("gift"));
```

<!-- example: flattened -->
```json
{"bool":{"filter":[{"term":{"tags.keyword":"gift"}}]}}
```

These shapes fail with `opensearch-flattened-collection`; register the
collection as `Nested` to use them:

- `Any` with more than one condition.
- Negated, inequality, and null tests on elements.
- `Any()` without a predicate.
- `Contains(null)`.
- `Any` membership whose candidates include null, such as
  `x.Values.Any(v => new int?[] { null, 1 }.Contains(v))`.

Null elements are dropped when an array is indexed, so these cannot be
answered exactly. Null-bearing membership on scalar fields
(`NullableField in [null, 1]`) and element null checks inside `nested` scopes
are unaffected.

## Presence markers for object null checks

OpenSearch cannot tell a null ordinary object (`"shippingAddress": null`)
from an empty object, or from one whose children are all null. Child-field
existence cannot be used either. The translator therefore never infers object
presence. A null check on an ordinary object requires a Boolean marker field
that you register and index:

```csharp
mapped.TranslatePredicate(x => x.ShippingAddress == null);
```

<!-- example: presence -->
```json
{"bool":{"filter":[{"term":{"shippingAddressPresent":false}}]}}
```

- `== null` matches `false` and `!= null` matches `true`.
- Marker paths are used exactly as registered, with no keyword suffix.
- Inside a nested scope, register the marker on the nested builder with a
  full path, for example
  `.Nested(x => x.Lines, l => l.Presence(e => e.Product, "lines.productPresent"))`.
- Without a marker, the null check fails with
  `opensearch-presence-marker-required`.

You are responsible for the marker data. Map the marker as `boolean`, and
index `true` for every non-null object, including empty and all-null objects,
and `false` for every null object. Do this on every document in scope. When
you add a marker to an existing index, backfill it (for example with
`_update_by_query`) before relying on it. The translator never creates,
populates, or verifies markers.

## Numeric conversions

C# lifts mixed-type comparisons, such as `intField > 1.5`, into conversions
like `(double)x.ItemCount > 1.5`. The translator rewrites these so the
OpenSearch result matches CLR evaluation exactly:

- Fractional bounds are rounded to the integral boundary. `> 1.5` becomes
  `gte 2`.
- Equality with a value the field cannot hold becomes `match_none`;
  inequality becomes `match_all`.
- Values outside the field's range become `exists` or `match_none`.
- For long fields compared with `double`, an exact preimage range is computed
  from the double's rounding interval.

```csharp
mapped.TranslatePredicate(x => x.ItemCount > 1.5 && x.ItemCount != 2.5);
```

<!-- example: conversions -->
```json
{"bool":{"filter":[{"range":{"itemCount":{"gte":2}}}]}}
```

These fail with `opensearch-unsupported-conversion`:

- Conversions of `float`, `double`, or `decimal` fields to other types.
- Conversions on fields that have a converter.
- Conversions to or from `char` in ordered comparisons.
- Sort keys converted to a floating-point type.

## Sorting, pagination, and search bodies

`TranslateSort` reads `OrderByClause.GetKeys`. Each key becomes a field sort:

- `missing` is `_first` for ascending and `_last` for descending, matching
  LINQ null ordering.
- Strings use the keyword field.
- Keys under nested paths get a `nested` option.

Computed, text-only, object, and collection keys fail.

`TranslatePagination` emits `from` = `Offset` and `size` = `Limit`. It fails
when `from + size` exceeds `MaxResultWindow`, reporting both values.

`TranslateSearch` composes a body from one or more predicates (combined with
AND), an optional ordering clause, and optional pagination. It also accepts a
`Condition<T>` directly. An invalid condition fails with
`opensearch-invalid-condition`. Diagnostics from all parts are reported
together.

```csharp
var condition = new ConditionOptions
{
    Where = "Status = 'Placed' and Total >= 100",
    OrderBy = "CreatedAt desc, Number"
}.BuildCondition<Order>();
var body = mapped.TranslateSearch(condition, new Pagination { PageSize = 20, Page = 3 });
```

<!-- example: search -->
```json
{"query":{"bool":{"filter":[{"term":{"status":"Placed"}},{"range":{"total":{"gte":100}}}]}},"sort":[{"createdAt":{"order":"desc","missing":"_last"}},{"order_no":{"order":"asc","missing":"_first"}}],"from":40,"size":20}
```

Non-generic overloads accept `LambdaExpression` and `Condition` for element
types known only at run time:

```csharp
new OpenSearchQueryTranslator(OpenSearchMapping.CreateDefault(type))
```

## OpenSearch.Client adapter

`Kkts.Expressions.OpenSearch.Client` converts successful translations into
`OpenSearch.Client` objects:

- `ToQueryContainer()` wraps a translated query in a `RawQuery`.
- `ToSorts()` returns `FieldSort` instances with order, missing, and nested
  paths.
- `ApplyTranslation(body)` sets `query`, `sort`, `from`, and `size` on a
  `SearchDescriptor<T>` or any `ISearchRequest`. Parts missing from the body
  are left unchanged.

Failed translations throw `OpenSearchTranslationException`, carrying the
diagnostics. The serialized request is JSON-equivalent to the raw translation.

```csharp
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

## Diagnostics

Each `OpenSearchTranslationDiagnostic` has a `Code`, a `Message`, the CLR
`MemberPath` when known, and the `ExpressionText` of the failing node.
Translation collects every problem rather than stopping at the first:

```csharp
var failed = mapped.TranslatePredicate(x => x.Total + 1 > 2 && x.Notes == "late" && x.Tags.Contains(null));
failed.Diagnostics.Select(d => d.Code);
```

<!-- example: diagnostics -->
```json
["opensearch-unsupported-expression","opensearch-text-field-exact-match","opensearch-flattened-collection"]
```

| Code (`OpenSearchDiagnosticCodes`) | Raised for |
| --- | --- |
| `opensearch-unsupported-expression` (`UnsupportedExpression`) | Arithmetic, concatenation, field-to-field comparisons, conditionals, type tests, foreign parameters, computed fields, wrong predicate shape. |
| `opensearch-unsupported-method` (`UnsupportedMethod`) | Methods other than the supported string functions and collection operations. |
| `opensearch-unmapped-field` (`UnmappedField`) | Members of scalars (for example `Name.Length`), and sort keys that are not member chains. |
| `opensearch-text-field-exact-match` (`TextFieldExactMatch`) | Exact operations or sorting on text-only strings. |
| `opensearch-unsupported-value` (`UnsupportedValue`) | Computed or non-finite values, unformattable types, null string operands, ordered comparisons on enum names. |
| `opensearch-unsupported-conversion` (`UnsupportedConversion`) | Conversions that cannot be rewritten exactly. |
| `opensearch-flattened-collection` (`FlattenedCollection`) | Collection shapes that need `nested` mapping. |
| `opensearch-presence-marker-required` (`PresenceMarkerRequired`) | Ordinary-object null checks without a registered marker. |
| `opensearch-result-window-exceeded` (`ResultWindowExceeded`) | `from + size` above the maximum result window. |
| `opensearch-invalid-condition` (`InvalidCondition`) | A `Condition` whose `IsValid` is `false`. |

## Risks and limitations

- **Your index mapping must match the configured mapping.** The translator
  cannot detect mismatches; for example, a `text` field queried as a keyword
  silently returns wrong results.
- **`ignore_above`.** Keyword values longer than `ignore_above` are not
  indexed. They do not match `term`, `prefix`, or `wildcard`, and they sort as
  missing.
- **Date precision.** OpenSearch `date` fields store milliseconds and
  `date_nanos` store nanoseconds, while .NET ticks are 100 ns. Equality on
  values with sub-millisecond ticks can differ from in-memory results.
- **Unspecified `DateTime`.** A `DateTime` with `Kind = Unspecified` is
  emitted without an offset, and OpenSearch treats it as UTC. A `Local` value
  is emitted with the machine's offset. String literals parsed by the
  interpreter follow its [DateTime rules](../README.md#datetime-and-datetimeoffset-strings).
  Prefer UTC values or `DateTimeOffset`.
- **Expensive wildcards.** `EndsWith` and `Contains` produce leading-wildcard
  queries that scan the term dictionary. A cluster with
  `search.allow_expensive_queries: false` rejects them, and so does a cluster
  that rejects `case_insensitive` options.
- **Number precision.** OpenSearch compares `float` and `half_float` fields
  at their indexed precision. Very large `long`/`unsigned_long` values are
  emitted exactly, but clients that parse responses as doubles may round them.
- **`Pagination.Page` ordering.** `Page` computes `Offset` from the limit in
  effect when `Page` is set. Set `PageSize` or `Limit` first:
  `new Pagination { PageSize = 20, Page = 3 }` gives `from` 40, whereas
  `{ Page = 3, PageSize = 20 }` gives `from` 100. `Limit` is capped at
  `Pagination.MaxLimit`, and deep paging beyond the result window needs
  `search_after`, which this package does not generate.
- **Tie-breaking.** OpenSearch orders equal sort keys arbitrarily. Add a
  unique key, such as `Id`, as the last sort key for stable pages.
