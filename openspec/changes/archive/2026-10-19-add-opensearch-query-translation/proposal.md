# Proposal

## Why

Kkts.Expressions turns user-supplied strings, structured filters, filter trees, and policy-checked query contexts into LINQ predicates, but applications whose search data lives in OpenSearch cannot reuse that validated pipeline and must hand-write a second, inconsistent filter translator. A companion package that translates the produced predicates, ordering, and paging into OpenSearch Query DSL lets one validated filter language drive both LINQ providers and OpenSearch.

## What Changes

- Add a new `netstandard2.0` package `Kkts.Expressions.OpenSearch` that translates `Expression<Func<T, bool>>` predicates (from any existing entry point: `Interpreter`, structured `Filter`/`FilterGroup`, `FilterNode` trees, `ExpressionQueryContext`, `Condition<T>`) into OpenSearch Query DSL as `System.Text.Json` `JsonObject` values, with no OpenSearch client dependency.
- Translate comparisons, `in`/`not in`, `contains`/`startsWith`/`endsWith`, Boolean members, null checks, `and`/`or`/`not` into `term`, `terms`, `range`, `exists`, `prefix`, `wildcard`, and `bool` queries, preserving LINQ null semantics.
- Preserve numeric conversion semantics through exact DSL rewrites where possible, including rounding boundaries when integral fields are widened to floating-point values; fail explicitly when equivalence cannot be established.
- Map CLR member paths to OpenSearch field names: camelCase by default, honoring `[JsonPropertyName]`, with a typed per-field override for name, string field kind (keyword, or text with a keyword subfield), case-insensitive matching, and value formatting (enums, `TimeSpan`).
- Support OpenSearch `nested` documents: configured nested member paths and `Enumerable.Any(collection, predicate)` produce `nested` queries; nested sort keys produce `nested` sort options.
- Support ordinary-object null checks through explicitly configured indexed Boolean presence markers, supplied by the application; do not infer object presence from indexed child fields.
- Reject null-element membership on flattened collections, whose index representation cannot distinguish null elements from absent values; retain supported non-null collection queries and scalar nullable-field membership.
- Translate ordering into `sort` and `Pagination` into `from`/`size`, validated against a configurable result window.
- Fail translation with structured diagnostics (no partial query) for constructs without a faithful DSL equivalent: arithmetic, string concatenation, computed selectors, unmapped methods, text-only fields used for exact matching, and values that cannot be formatted.
- Add a small additive public API to core so `OrderByClause` exposes its ordered key selectors and directions (sort keys are currently internal).
- Add a second package `Kkts.Expressions.OpenSearch.Client` that adapts translation results to `OpenSearch.Client` 2.x (`QueryContainer` via `RawQuery`, `ISort`, and `SearchDescriptor<T>` extensions).
- Add unit tests for DSL shape, plus Testcontainers-based OpenSearch integration tests comparing search results with LINQ-to-Objects results for the same predicates; add a guide and README section.

## Capabilities

### New Capabilities

- `opensearch-query-translation`: Translation of Kkts.Expressions predicates, ordering, and pagination into OpenSearch Query DSL, including field mapping, keyword/text handling, nested documents, value formatting, diagnostics for unsupported constructs, the core sort-key exposure it depends on, and the optional OpenSearch.Client adapter.

### Modified Capabilities

None. The `OrderByClause` addition is additive and has no existing spec; it is specified within the new capability.

## Impact

- New projects: `src/Kkts.Expressions.OpenSearch`, `src/Kkts.Expressions.OpenSearch.Client`, `src/Kkts.Expressions.OpenSearch.Tests` (added to `src/Kkts.sln`). Existing empty `bin`/`obj` leftovers under the OpenSearch folders are replaced by real projects.
- Core `Kkts.Expressions`: additive public sort-key accessor on `OrderByClause`; no behavior change to parsing, policies, or existing APIs.
- Dependencies: core translator uses only `System.Text.Json` (already referenced by core); the adapter adds `OpenSearch.Client` 2.x; tests add `Testcontainers.OpenSearch` 4.16.0 and integration tests require Docker and carry a `Provider=OpenSearch` trait so DSL unit tests can run without Docker via a filter, mirroring the trait-based selection of the existing EF Core relational tests.
- Non-goals: full-text relevance queries (`match`), aggregations, highlighting, index/mapping management, Painless script fallbacks, executing searches, and automatic discovery of index mappings.
