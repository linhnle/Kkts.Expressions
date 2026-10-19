# Tasks

## 1. Expose sort keys from core `OrderByClause`

- [x] 1.1 Add public `OrderByKey` (`KeySelector`, `Descending`) and `OrderByClause.GetKeys(Type)` / `GetKeys<T>()` reusing `CreateKeyExpression` for the first key and each `ThenBy`; verify with new tests in `Kkts.Expressions.UnitTest` that `"name desc, id"`, `OrderByInfo` lists, string property mappings, and public expression fields return keys in order with selectors structurally equal to those `Sort` applies, and that the existing `OrderByTest` suite still passes.
- [x] 1.2 Document `GetKeys` in the README sorting section; verify the documented example compiles in a documentation-example test.

## 2. Scaffold the OpenSearch projects

- [x] 2.1 Delete stale `bin`/`obj` under `src/Kkts.Expressions.OpenSearch*`; create `Kkts.Expressions.OpenSearch` (netstandard2.0, packable, core `ProjectReference`, `System.Text.Json`), `Kkts.Expressions.OpenSearch.Client` (netstandard2.0, `OpenSearch.Client` 2.2.0), and `Kkts.Expressions.OpenSearch.Tests` (net10.0, xunit 2.5.0, `Testcontainers.OpenSearch` 4.16.0); add all to `src/Kkts.sln`; verify `dotnet build src/Kkts.sln` succeeds.

## 3. Mapping configuration and field resolution

- [x] 3.1 Implement immutable `OpenSearchMapping<T>` and its fluent builder (naming policy, default string kind, enum format, converters, max result window, per-field overrides, nested paths including nested-in-nested, and typed ordinary-object Boolean presence-marker registration); verify unit tests for defaults, builder immutability after `Build`, invalid selectors (non-member chains, other parameters), duplicate/conflicting field registrations, invalid nested declarations, and invalid/conflicting presence-marker selectors and paths.
- [x] 3.2 Implement member-chain resolution to field path, exact (keyword) path, nested path chain, and settings, honoring `[JsonPropertyName]`, naming policy, whole-path overrides, and the default `text` + `keyword` subfield; verify tests for camelCase multi-segment paths, attribute/override precedence, keyword/custom subfield/text-only resolution, nested path detection, and per-mapping cache reuse.

## 4. Predicate translation core

- [x] 4.1 Implement result types (`OpenSearchTranslationResult<T>`, `OpenSearchTranslationDiagnostic`, `OpenSearchDiagnosticCodes`, `OpenSearchTranslationException`) and the internal clause model with deterministic JSON serialization; verify serialization unit tests for every clause kind and byte-identical output on repeated runs.
- [x] 4.2 Implement normalization: strip `Quote`, retain field conversion chains until equivalence is established, fold parameter-free constant/captured member/convert/array-init subtrees via reflection without `Compile`, and reject other parameter-free computations; verify tests with closures, static members, built-in date variables, collection variables, nullable/enum/widening conversions, and a captured method call that must fail without being invoked.
- [x] 4.2a Implement exact conversion-aware comparison/membership rewrites, including integral-to-float/double source-interval searches using CLR rounding and overflow-safe domain arithmetic; verify equality, inequality, ranges, membership, nullable negation, signed/unsigned endpoints, halfway rounding, and neighbors of 2^24/2^53 against CLR evaluation, and explicit conversion diagnostics for cases without a proven rewrite.
- [x] 4.3 Implement logical composition (`AndAlso`/`OrElse`/`Not`, flattening, root `bool.filter`, `match_all`/`match_none` for constants) and diagnostic accumulation without partial output; verify the logical-composition and all-or-nothing spec scenarios.
- [x] 4.4 Implement comparisons (term, must_not term, range with operand reversal, null equality/inequality via exists) and standalone/negated Boolean members; verify the comparison and Boolean spec scenarios plus nullable and enum operands.
- [x] 4.5 Implement membership (`Enumerable.Contains` over constant/captured/variable lists, `Not` for `not in`, de-duplication in first-seen order, null element disjunct, empty-list constants, case-insensitive `should` of terms); verify the membership spec scenarios.
- [x] 4.6 Implement string functions (prefix, escaped wildcard, empty operand as exists, null operand failure, `case_insensitive` flag) and keyword/text-only enforcement; verify the string-function and keyword/text spec scenarios.
- [x] 4.7 Implement value formatting (numbers, bool, string/char, Guid, DateTime/DateTimeOffset round-trip, enum number/name, converters by type and field, non-finite and unformattable failures) culture-independently; verify the value-formatting scenarios, including a run under a comma-decimal culture.
- [x] 4.8 Implement explicit failures for arithmetic, concatenation, field-to-field comparisons, unsupported methods, conditionals, type tests, foreign parameters, and computed public expression fields; verify each produces the documented diagnostic code with member path and expression text and no output.
- [x] 4.9 Add entry-point parity tests translating equivalent predicates from `Interpreter`, structured `Filter`/`FilterGroup`, `FilterNode` trees, and `ExpressionQueryContext` (with a query policy); verify identical JSON and that translation does not alter policy outcomes.

## 5. Nested documents and collections

- [x] 5.1 Implement leaf-level `nested` wrapping for single-valued nested objects (negation outside, nested-in-nested chains) and nested-object null checks; verify the nested-object spec scenarios.
- [x] 5.1a Implement ordinary-object null checks through registered Boolean presence markers, including nested enclosing scopes, and fail unconfigured ordinary-object null checks; verify true/false marker queries distinguish null, empty, all-null, and populated objects without child-field existence inference.
- [x] 5.2 Implement `Any(predicate)` and `Any()` over nested collections with element-parameter binding and suppression of already-open nested paths; verify the same-element and Any-without-predicate scenarios and a nested-in-nested `Any` case.
- [x] 5.3 Implement non-nested collection support (`Contains` with non-null operands on scalar arrays, single positive-leaf `Any` with non-null membership candidates) and the `opensearch-flattened-collection` diagnostic for all other shapes; verify the non-nested collection spec scenarios including negated, inequality, `Contains(null)`, null-bearing `Any` membership, and multi-condition rejections, while retaining scalar nullable-field membership and nested-element scalar null checks.

## 6. Sort, pagination, and search body

- [x] 6.1 Implement `TranslateSort` from `OrderByClause.GetKeys` (exact paths for strings, `missing` `_first`/`_last`, `nested` sort option, computed/text-only key failures); verify the sort spec scenarios.
- [x] 6.2 Implement `TranslatePagination` (`Offset`/`Limit`, `long` result-window check with limit and observed value in the diagnostic); verify the pagination spec scenarios.
- [x] 6.3 Implement `TranslateSearch` for predicate(s) + ordering + pagination and for `Condition<T>` (invalid conditions fail), plus the non-generic `LambdaExpression`/`Condition` overloads; verify the search-body spec scenarios and combined diagnostics from multiple parts.
- [x] 6.4 Write `docs/opensearch.md` (installation, mapping configuration, supported constructs table, null/case/nested semantics, conversion-aware numeric rewrites and failure cases, flattened null-element restrictions, presence-marker registration and indexing/backfill responsibility, diagnostics catalog, risks: `ignore_above`, date precision, unspecified `DateTime`, expensive wildcards, `Pagination.Page` ordering) and add an OpenSearch section and contents entry to `README.md`; verify every documented example compiles and produces the documented JSON in a documentation-example test.

## 7. OpenSearch.Client adapter

- [x] 7.1 Implement `ToQueryContainer()` (via `RawQuery`), `ToSorts()` (`FieldSort` with order, missing, nested path), and `SearchDescriptor<T>`/`ISearchRequest` extensions applying query, sort, from, and size, throwing `OpenSearchTranslationException` for failed results; verify unit tests that serialize the client request with the client's serializer and compare `query`/`sort`/`from`/`size` with the raw body via `JsonNode.DeepEquals`.
- [x] 7.2 Document the adapter in `docs/opensearch.md` and its package README; verify the documented example compiles in a documentation-example test.

## 8. OpenSearch equivalence verification

- [x] 8.1 Add a Testcontainers OpenSearch 2.x collection fixture (pinned image) with an explicit index mapping (keyword, text with `keyword`/`raw` subfields, case-insensitive candidate field, nested objects and nested-in-nested collections, scalar arrays, date, enum, nullable numerics, integral rounding-boundary fields, and Boolean ordinary-object presence markers) and seed data mirrored in memory, including null/empty/all-null/populated objects with correct markers; verify the fixture starts, indexes, and refreshes in a smoke test tagged `[Trait("Provider", "OpenSearch")]`.
- [x] 8.2 Add equivalence tests executing translated queries (raw body and via the adapter) and comparing hit ids and order with LINQ-to-Objects (null-safe reference predicates for string functions) for every supported operator, null case, membership edge case, case-insensitive field, nested and flattened collection case, multi-key sort with missing values, and page window; verify `dotnet test src/Kkts.Expressions.OpenSearch.Tests --filter "Provider=OpenSearch"` passes with Docker running.
- [x] 8.2a Add real-OpenSearch regression coverage for converted numeric equality/range/membership around rounding boundaries, ordinary-object presence markers across null/empty/all-null/populated objects, and diagnostic rejection of flattened null-element membership; verify hit identifiers agree with CLR evaluation for successful rewrites and no query is emitted for rejected shapes.
- [x] 8.3 Document running the OpenSearch suite (Docker requirement, trait filter, pinned image) in the README development notes; verify `dotnet test src/Kkts.sln --filter "Provider!=OpenSearch&Provider!=SqlServer&Provider!=MySql"` passes without Docker and the full `dotnet build src/Kkts.sln` succeeds.
