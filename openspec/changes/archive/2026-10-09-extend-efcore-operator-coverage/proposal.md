# Proposal

## Why

The shared SQL Server and MySQL integration suite currently verifies comparisons, logical composition, nullable comparisons, strings, enums, dates, and navigation, but it does not exercise `in`, `not in`, addition, or subtraction against either relational provider. The library and README already define these operators' behavior, so provider-backed coverage can close the gap without changing parser or API behavior.

## What Changes

- Extend the existing provider-shared relational predicate cases for `in`/`not in`, binary addition and subtraction, composition/grouping, nullable operands, and negative numeric literals.
- Reuse the current SQL Server and MySQL Testcontainers fixtures, five-row deterministic seed convention, provider collation setup, test entry points, and CI provider matrix. Add only test-model fields or seed values needed to express the operator matrix.
- Execute each parsed `Expression<Func<RelationalRecord, bool>>` directly with relational `IQueryable.Where`; assert explicit record IDs and compare against equivalent handwritten predicates executed on the same provider.
- Derive membership expectations from the documented complement contract: `in` is false and `not in` true for an empty list; null matches a list only when it contains null; `not in` is the Boolean complement of `in`, without SQL-style unknown results or implicit null filtering. Use the existing binary collations for deterministic exact string membership, including punctuation.
- Cover the numeric operand types and nullable/mixed combinations already supported by the library where both providers can map and translate them. Treat provider translation gaps as findings to document and propose separately, not as parser/API changes or reasons to alter library semantics in this coverage change.
- Keep malformed syntax and unsupported-operand validation in the existing unit-test project. General unary negation of a property, variable, or group is not supported; the relational suite may exercise supported negative numeric literals, not introduce or imply general unary syntax.
- Do not change production code, public APIs, dependencies, database infrastructure, or exact-SQL assertions.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This is test-only work and does not change library behavior; the change opts out of a delta spec with `skip_specs: true`.

## Impact

- Extends `src/Kkts.Expressions.EntityFrameworkCore.Tests/RelationalPredicateIntegrationTests.cs` and, if needed, its existing relational test model and deterministic seed in `RelationalTestModel.cs` and `RelationalDatabaseFixtures.cs`.
- Reuses the existing SQL Server 2022 and MySQL 8.4.4 containers, EF Core 10.0.9 provider packages, and `.github/workflows/efcore-relational-tests.yml`; no new provider or dependency is introduced.
- README operator documentation is the source for membership null/empty-list and arithmetic/unary-negation expectations. Any newly demonstrated provider limitation is recorded for a separate follow-up rather than silently changing the contract.
