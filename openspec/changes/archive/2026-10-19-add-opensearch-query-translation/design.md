# Design

## Context

- Every Kkts.Expressions entry point (string parsing, `Filter`/`FilterGroup`, `FilterNode` trees, `ExpressionQueryContext`, `ConditionOptions.BuildCondition`) converges on `Expression<Func<T, bool>>`. Policies, allowlists, field mappings, and variables are already resolved by the time a predicate exists.
- The emitted tree shapes are narrow and known: `Equal`/`NotEqual`/range `BinaryExpression`s over member chains and constants (with `Convert` for nullable/enum/widening), `AndAlso`/`OrElse`/`Not`, `string.Contains/StartsWith/EndsWith` calls (`Interpreter.String*Method`), membership as `Enumerable.Contains(collection, value)` optionally wrapped in `Not` (`TypedFilterExpressionBuilder.BuildMembership`), arithmetic via `Add`/`Subtract`/`string.Concat` (`Internal/Nodes/Addition.cs`, `Subtraction.cs`), and public expression fields inlined via `ResolvedQueryField.Compose`.
- `OrderByClause` stores keys in internal `OrderByParser` (`PropertyName`/`PropertyField`/`Descending` plus `ThenBys`) and exposes only `Sort` methods, so external packages cannot read sort keys.
- `Pagination.Offset` is authoritative; `Page` derives offset from the limit at assignment time.
- Core is `netstandard2.0`, already references `System.Text.Json` 10.0.12, and the core `ExpressionDiagnostic` is span-based with an internal constructor.
- `src/Kkts.Expressions.OpenSearch` and `src/Kkts.Expressions.OpenSearch.Tests` contain only stale `bin`/`obj` output and are not in `src/Kkts.sln`.

## Goals / Non-Goals

**Goals:**
- A single visitor-based translator over LINQ expression trees, with no compilation and reflection limited to reading captured values and member attributes.
- Faithful LINQ-to-Objects semantics, including nulls and same-element nested matching, or an explicit failure.
- A thin client adapter that cannot drift from the raw JSON output.

**Non-Goals:**
- Translating the string grammar or filter trees directly; reading live index mappings; supporting Elasticsearch-specific DSL differences; async APIs (translation is CPU-only and synchronous).

## Decisions

### D1. Translate LINQ trees, not syntax trees
Input is `LambdaExpression` with one parameter returning `bool`. This covers all entry points and application-authored predicates (`Any`, `Contains` on arrays) and inherits policy enforcement for free.
*Alternative*: walk `ExpressionSyntaxTree`/`FilterNode` — duplicates binding, variable, and field-mapping logic and misses structured filters and app predicates.

### D2. Public API shape (package `Kkts.Expressions.OpenSearch`, namespace `Kkts.Expressions.OpenSearch`)
- `OpenSearchMapping<T>`: immutable, built via `OpenSearchMapping<T>.Create(b => ...)` or `OpenSearchMapping.For<T>()` fluent builder:
  - `NamingPolicy(JsonNamingPolicy)` (default `JsonNamingPolicy.CamelCase`), `DefaultStringField(StringFieldKind)` (default text + `keyword` subfield), `EnumFormat(EnumFormat.Number|Name)`, `Converter<TValue>(Func<TValue, JsonNode>)`, `MaxResultWindow(int)` (default 10,000).
  - `Field(x => x.Member.Path, f => f.Name("a.b").Keyword() | .Text("raw") | .TextOnly().CaseInsensitive().EnumFormat(...).Converter(...))`.
  - `Nested(x => x.Lines)` / `Nested(x => x.Customer)`; nested paths inside nested paths via `Nested(x => x.Lines, n => n.Nested(l => l.Taxes))`.
  - `Presence(x => x.Customer, "customerPresent")` registers an indexed Boolean marker for ordinary-object null checks. The path is explicit, is not keyword-suffixed, and must be valid in the object's enclosing document scope. Reject scalar/collection/computed selectors, blank or malformed paths, duplicate registrations, and registrations conflicting with a nested declaration for the same object.
- `OpenSearchQueryTranslator<T>` (constructed with a mapping; thread-safe, reusable) exposing `TranslatePredicate`, `TranslatePredicates(IEnumerable<...>)`, `TranslateSort(OrderByClause)`, `TranslatePagination(Pagination)`, `TranslateSearch(predicate(s), OrderByClause, Pagination)`, `TranslateSearch(Condition<T>, Pagination)`. Non-generic `OpenSearchQueryTranslator` accepts `LambdaExpression`/`Condition` with a `Type`.
- Results: `OpenSearchTranslationResult<TOutput>` with `Succeeded`, `Value` (`JsonObject`/`JsonArray`; null on failure), `Diagnostics`, `ToJsonString()`, and `GetValueOrThrow()` throwing `OpenSearchTranslationException`.
- `OpenSearchTranslationDiagnostic { Code, Message, MemberPath, ExpressionText }` with codes as public string constants in `OpenSearchDiagnosticCodes`, e.g. `opensearch-unsupported-expression`, `opensearch-unsupported-method`, `opensearch-unmapped-field`, `opensearch-text-field-exact-match`, `opensearch-unsupported-value`, `opensearch-flattened-collection`, `opensearch-result-window-exceeded`, `opensearch-invalid-condition`.
*Alternative*: reuse core `ExpressionDiagnostic` — rejected, it is span-based and its constructor is internal.

### D3. Translation pipeline
1. **Normalize** (one recursive pass): strip `Quote`; retain conversion chains on field operands until their semantic effects have been analyzed. Only proven value-preserving conversions can be removed from comparison semantics; C# numeric widening alone is not proof. Fold parameter-free subtrees consisting only of `Constant`, `MemberAccess` on constants/static members, `Convert`, and `NewArrayInit` into constants via reflection. Any other parameter-free node fails (`opensearch-unsupported-value`). No `Compile()`.
2. **Bind**: classify each operand as *field reference* (member chain rooted at the lambda parameter or a current `Any` element parameter), *constant*, or *unsupported*. Field references resolve through the mapping to `(fieldPath, exactPath, nestedPaths[], clrType, conversionChain, settings)`; member-resolution results are cached per mapping keyed by member chain, while conversion chains remain specific to the operand.
3. **Emit** an internal immutable clause model (`Term`, `Terms`, `Range`, `Exists`, `Prefix`, `Wildcard`, `Bool`, `Nested`, `MatchAll`, `MatchNone`), then **serialize** to `JsonObject` in fixed property order. The model makes flattening, double-negation handling, and nested wrapping simple and testable without string comparison.
Diagnostics accumulate across the whole tree (no early exit) so users see all problems at once; any diagnostic suppresses output.

### D3a. Exact numeric conversion rewrites
Preserve conversions separately from field identity. For integral-to-`float`/`double` conversion, the CLR conversion is monotone over the integral source domain. Find the exact lower and upper source endpoints for each comparison using bounded binary searches over that domain, comparing explicitly converted values with the target using CLR floating-point semantics. Use overflow-safe integral bounds and midpoint arithmetic, not floating-point approximations to endpoints. Equality can produce a singleton term, a closed range, or no matches; ordered comparisons produce source-domain ranges; inequality complements equality. Membership unions the exact equality intervals, coalescing overlaps while retaining deterministic output. Preserve nullable missing-value behavior when complementing clauses.

Use typed conversion handlers without compiling expressions or invoking user-defined conversion operators. Cover signed/unsigned source bounds, negative values, halfway rounding, and the regions around 2^24 and 2^53. For other conversions, use a proven exact rewrite or report `opensearch-unsupported-conversion`; do not claim that every conversion has a DSL equivalent. A configured value converter that prevents establishing numeric equivalence also requires an explicit diagnostic. The index must preserve the integral source values for these rewrites to be exact.

### D4. Null semantics
LINQ-to-Objects treats a missing field as null. Rules: `== null` → `must_not exists`; `!= null` → `exists`; `!= v` → `must_not term`; `Not(p)` → `must_not p`. Range/term never match missing values. `in` with a null element → `should [terms, must_not exists]`. Because `must_not` matches missing documents, these compose correctly under arbitrary negation. String functions on a null field evaluate to no-match, matching EF Core and avoiding LINQ-to-Objects `NullReferenceException` (documented; the equivalence suite uses null-safe reference predicates for these cases).

### D5. Strings and case sensitivity
Exact operations target `exactPath` (keyword field, or `field.<subfield>`). Contains/EndsWith use `wildcard` (escape `\`, `*`, `?`); StartsWith uses `prefix` (no escaping needed). Empty operand → `exists` on `exactPath`. Case-insensitive fields set `case_insensitive: true` on `term`/`prefix`/`wildcard`; since `terms` does not support it, case-insensitive `in` emits `bool.should` of `term`s.
*Alternative*: default to `match`/`match_phrase` on text fields — rejected, tokenized relevance matching differs from substring semantics.

### D6. Nested documents and collections
- A field whose chain crosses a configured nested path is wrapped at the leaf: `nested { path, query: leaf }`, innermost path first, so `Not` stays outside. This is correct for single-valued nested objects.
- `Enumerable.Any(source, lambda)` where `source` is a field reference to a configured nested collection: translate the lambda body with its parameter bound to the collection path, then wrap once in `nested`. Leaf-level wrapping is suppressed for the paths already opened by the enclosing `Any`. `Any()` without predicate → `nested { match_all }`.
- Non-nested collections: `Enumerable.Contains(field, nonNullConstant)` and `Any` whose body is exactly one positive leaf (equality to a non-null value, membership with exclusively non-null candidates, range, prefix/wildcard) are emitted on the flattened field path. `Contains(null)` and null-bearing membership within `Any` fail with `opensearch-flattened-collection`: `[null, 1]` and `[1]` have indistinguishable indexed values. Other unsafe shapes fail with the same diagnostic. Scalar nullable-field membership retains D4's rules; null checks on scalar fields inside an opened nested-element scope remain supported.
- Null checks on ordinary object members require `Presence(x => x.Customer, "customerPresent")`: `== null` emits a Boolean term with `false`; `!= null` emits a Boolean term with `true`. The application must index the marker on every document, true even for `{}` and all-null objects, false for null. No marker registration yields `opensearch-presence-marker-required`; do not use `exists` on object paths or infer presence from children. Markers respect the enclosing nested scope where applicable. Nested-object null checks retain presence via `nested { match_all }`, complemented for equality with null.

### D7. Values
`Utf8JsonWriter`-free `JsonValue.Create` per CLR type; `DateTime`/`DateTimeOffset` with `"o"` format via `InvariantCulture`; `Guid` `"D"`; `char` → string; enum → underlying integral (default) or `Enum.GetName` (configurable; undefined enum values fail). Converters registered per type or field take precedence. `float`/`double` NaN/Infinity fail.

### D8. Core sort-key exposure
Add to core: `public sealed class OrderByKey { LambdaExpression KeySelector; bool Descending; }` and `OrderByClause.GetKeys(Type elementType)` / `GetKeys<T>()` returning `IReadOnlyList<OrderByKey>`. Implemented by reusing the existing `CreateKeyExpression(parameter, name, field)` for the first key and each `ThenBy`, so selectors are identical to what `Sort` applies. Pure, additive, no compilation.
*Alternatives*: `InternalsVisibleTo` for the OpenSearch assembly (couples package versions to internals) or re-parsing the order string in the package (loses mappings and public expression fields).

### D9. Sort and pagination
Each key → `{ <exactPath or fieldPath>: { order, missing } }`, with `missing` `_first` for ascending and `_last` for descending (LINQ treats null as smallest), plus `nested: { path }` (nested chain for multi-level) when applicable. Pagination emits `from = Offset`, `size = Limit`; checks `from + size <= MaxResultWindow` using `long` arithmetic.

### D10. Search body and conditions
`TranslateSearch` returns `{ query, sort?, from?, size? }`. Multiple predicates are AND-ed into one top-level `bool.filter`. `Condition<T>` with `IsValid == false` → `opensearch-invalid-condition`, carrying no partial output. The top-level query is always `{"bool":{"filter":[...]}}` (D3 flattening applies to the root conjunction).

### D11. Client adapter (`Kkts.Expressions.OpenSearch.Client`)
Targets `netstandard2.0`, references `OpenSearch.Client` 2.x (currently 2.2.0). Converts the query via `new RawQuery(json)` wrapped in `QueryContainer`; converts sort entries into `FieldSort` (`Field`, `Order`, `Missing`, `Nested = new NestedSort { Path }`); exposes `ApplyTranslation(OpenSearchTranslationResult<JsonObject>)` for `SearchDescriptor<T>` and any `ISearchRequest`, plus `ToQueryContainer()`/`ToSorts()`. Failed results throw `OpenSearchTranslationException`.
*Alternative*: build typed `TermQuery`/`BoolQuery` objects — duplicates DSL emission and risks drift; `RawQuery` keeps a single source of truth.

### D12. Projects and tests
- `src/Kkts.Expressions.OpenSearch` (netstandard2.0, packable, version aligned with core 3.1.x line, `ProjectReference` to core), `src/Kkts.Expressions.OpenSearch.Client` (netstandard2.0, packable), `src/Kkts.Expressions.OpenSearch.Tests` (net10.0, xunit 2.5.0 as in existing test projects, `Testcontainers.OpenSearch` 4.16.0, `OpenSearch.Client` for executing searches). All added to `src/Kkts.sln`; stale `bin`/`obj` directories are deleted first.
- Unit tests assert JSON with `JsonNode.DeepEquals`. Integration tests carry `[Trait("Provider", "OpenSearch")]`, start one pinned OpenSearch 2.x image per collection fixture with an explicit index mapping (keyword, text+subfields, nested, date, enum), seed documents, refresh, and compare hit ids/order with LINQ-to-Objects over the same in-memory seed data.
- `OrderByClause.GetKeys` tests go in the existing `Kkts.Expressions.UnitTest` project.

## Risks / Trade-offs

- [Dynamic-mapping `keyword` subfields use `ignore_above: 256`; longer strings are not indexed for exact matching] → Document; recommend explicit mappings; equivalence data stays within limits.
- [OpenSearch `date` fields have millisecond precision; sub-millisecond `DateTime` bounds can change `>`/`<` results] → Document; recommend `date_nanos` or rounding; seed data uses millisecond values (as EF relational tests do).
- [`DateTime` with `Unspecified` kind is interpreted as UTC by OpenSearch] → Emit as-is and document; users can register a `DateTime` converter.
- [`wildcard` with leading `*` is expensive and may be disabled by `search.allow_expensive_queries`] → Document; recommend the `wildcard` field type or n-gram subfields; no automatic fallback.
- [Mapping config can disagree with the real index mapping, producing silently wrong results] → Explicit config, defaults matching dynamic mapping, and documentation; reading mappings is a non-goal.
- [Missing or stale object-presence markers change null-check results] → Require an application-owned indexing/backfill contract covering every document; document and test null, empty, all-null, and populated objects. The translator neither creates markers nor verifies live mappings.
- [Dropping lossy widening conversions changes numeric matches] → Preserve conversion chains, derive exact integral source intervals using CLR rounding, test boundary neighbors and domain endpoints, and reject conversions without a proven rewrite.
- [Numeric precision: `decimal`/`long` values beyond double precision on `double`/`float` fields] → Emit exact JSON numbers; document field-type guidance.
- [`Pagination.Page` ordering pitfall] → Translate `Offset` only and document it.
- [Expression trees from future core features may introduce new node shapes] → Unknown shapes fail explicitly; tests pin shapes of current entry points.

## Migration Plan

Purely additive: new packages and one additive core API. Ship core with `OrderByClause.GetKeys` first (or together), then the two OpenSearch packages depending on that core version. Rollback is removing the package references; no data migration.
