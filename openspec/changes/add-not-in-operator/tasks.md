# Tasks

## 1. Parsed exclusion predicates

- [x] 1.1 Register canonical `not in` and implement whitespace-aware two-keyword recognition in `ComparisonOparatorParser`, routing RHS parsing like `in`; add parser tests for case, spaces/tabs/newlines, token boundaries, malformed sequences, quoted text, keyword-containing identifiers, and unchanged prefix negation, and verify them with the focused xUnit test selector.
- [x] 1.2 Extend parsed membership expression construction to negate typed `Contains` while sharing membership classification; handle arithmetic element typing and skip comparison normalization for both membership operators, and verify matching/non-matching values, grouped addition/subtraction, computed strings, collection-variable RHS, logical AND/OR, and nested negation in focused interpreter tests.
- [x] 1.3 Add a complement-equivalence type matrix for numeric, string, Boolean, Guid, enum, DateTime, DateTimeOffset, TimeSpan, and nullable values, including null elements, empty lists, duplicates, and invalid conversions; verify compiled results equal explicitly negated `in` and simple trees are `Not(Contains(...))` without invocation/compiled delegates.
- [x] 1.4 Cover generic/runtime-type sync/async parsing, literal-list variables, collection variables, async-only resolution, mappings/allowlists, unresolved-variable diagnostics, and cancellation during resolution; verify overload parity and existing failure contracts in focused interpreter tests.
- [x] 1.5 Add the predicate syntax and supported types to the README operator table and document whitespace, complement/null semantics, empty-list behavior, and provider-dependent translation; verify the documented examples against the new predicate tests.

## 2. Direct builders and structured filters

- [x] 2.1 Append `ComparisonOperator.NotIn = 11`, map the structured spelling with narrowly scoped whitespace normalization, and update `CorrectOperator` plus sync/async scalar-conversion exemptions wherever `In` is special; verify existing enum numeric values remain unchanged and supported string/Boolean/Guid/enum/duration builders no longer fall back to equality or scalar list conversion.
- [x] 2.2 Wire direct/structured construction to shared typed membership negation, preserving list conversion and awaiting list-element resolution in async membership building with cancellation; add generic/runtime-type sync/async enum-builder and individual-filter tests with matching/non-matching values, nullable values, an async-only resolver, and cancellation, and run the focused selector alongside corresponding existing `In` cases.
- [x] 2.3 Add structured spelling tests for case, leading/trailing whitespace, repeated spaces/tabs/newlines, and invalid aliases, plus throwing/try-builder error and diagnostic tests for bad values, missing variables, and restricted properties; verify accepted spellings agree and failures remain explicit.
- [x] 2.4 Add filter collection and group tests combining exclusion with existing criteria across generic/runtime-type sync/async entry points; verify collection-AND and group-OR record sets rather than only non-null expression results.
- [x] 2.5 Add a README structured `Filter { Property = "Id", Operator = "not in", Value = "1, 2" }` example and describe `ComparisonOperator.NotIn`; verify each documented representation is exercised by the builder/filter tests.

## 3. Condition integration

- [x] 3.1 Add dedicated condition exclusion tests following the existing `ConditionOptionsPlusTest` pattern for `Where` and structured filters through generic/runtime-type sync/async APIs; verify exact selected records with EF Core InMemory, composition with sorting, and equivalence with explicitly negated membership.
- [x] 3.2 Add invalid-condition tests for malformed `not in`, invalid list values, and unresolved variables; verify `IsValid`, errors, null predicates on failure, and cancellation behavior match existing condition contracts.
- [x] 3.3 Add a condition `Where = "Id not in [1, 2]"` documentation example with the existing validation pattern; verify the example's valid/invalid paths are represented by condition integration tests.

## 4. Integration validation

- [x] 4.1 Run one combined focused `dotnet test src/Kkts.Expressions.UnitTest/Kkts.Expressions.UnitTest.csproj --filter "<relevant fully-qualified-name selectors>"` covering exclusion, existing membership/negation, culture, durations, arithmetic, filters/groups, and conditions; verify all selected tests pass, escalating only if failures expose wider coupling.
- [x] 4.2 Build `src/Kkts.Expressions/Kkts.Expressions.csproj` with the existing toolchain to verify `netstandard2.0` compatibility, and validate this OpenSpec change with `openspec validate add-not-in-operator --strict`; verify both commands succeed without new dependencies or unrelated source changes.
