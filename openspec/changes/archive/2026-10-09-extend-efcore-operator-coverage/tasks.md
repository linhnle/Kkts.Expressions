# Tasks

## 1. Relational membership cases

- [x] 1.1 Extend the existing shared predicate matrix with `in` and `not in` cases for single- and multi-value lists, matches and non-matches, numeric values, binary-collated strings containing special punctuation, enums, duplicates, and AND/OR/NOT/parenthesized composition. For every case assert explicit IDs and execute both the parsed expression and equivalent handwritten predicate on SQL Server and MySQL.
- [x] 1.2 Add nullable and empty-list cases from the documented complement contract: null and non-null field values with and without null list elements; `in []` selects no rows and `not in []` selects all rows, including rows whose nullable field is null. Assert explicit IDs and generated/handwritten parity on both providers; do not infer or encode SQL three-valued semantics.

## 2. Relational arithmetic cases

- [x] 2.1 Extend the shared cases for property-to-constant and property-to-property addition/subtraction, negative numeric literals, left-associative chains, shared `+`/`-` precedence, explicit parentheses, logical composition, and nullable result comparisons. Assert explicit IDs plus same-provider handwritten predicate parity; do not add general unary-negation expressions.
- [x] 2.2 Audit the library-supported numeric CLR types and extend only the test model and deterministic seed needed for provider-mappable nullable, mixed-type, and chained arithmetic cases. Add table-driven relational cases for compatible promoted operand pairs on both providers; assert explicit results and record any mapping/translation incompatibility as a separate finding rather than changing parser behavior or silently dropping the case.

## 3. Verification and compatibility findings

- [x] 3.1 Verify malformed membership/arithmetic syntax and unsupported operand validation remain covered by the existing `NotInTest`, `InterpreterPlusTest`, and `InterpreterSubtractionTest` unit tests; add validation tests only for a demonstrated gap, keep them in the existing unit-test project, and run the focused unit selectors.
- [x] 3.2 Run the integration suite using the existing SQL Server and MySQL provider filters, then run the full `Kkts.Expressions.EntityFrameworkCore.Tests` project. Confirm generated expression trees go directly to relational `IQueryable.Where`, parser failures remain distinct from translation/execution failures, and no exact SQL snapshots are introduced.
- [x] 3.3 For any provider-specific behavior or compatibility defect found, record the provider, expression, observed result/failure, and expected contract in a separate narrowly scoped follow-up proposal; do not modify production code or weaken the shared contract as part of this test-only change. Confirm existing README collation, null, arithmetic, and provider-guarantee notes remain accurate.
