# Design

## Context

See [proposal.md](./proposal.md) for the coverage gap and scope. The relational project has one `PredicateCase` shape (`Text`, expected record IDs, handwritten expression) and a shared runner that parses each case and executes both expressions via `Records.Where(...)` for each provider. SQL Server and MySQL test classes already call that runner; their Testcontainers fixtures seed five deterministic records. The suite currently has no membership or arithmetic cases.

Existing unit-test contracts and the README define the behaviors relevant to this suite:

- `not in` is exactly the Boolean complement of `in`. A null value is a member only when the list contains null; an empty list yields false for `in` and true for `not in`; duplicates do not change membership.
- Numeric addition and subtraction share precedence, associate left-to-right, and propagate nullable operands as null. Negative numeric literals are accepted in value positions.
- General unary negation of a property, variable, or group is explicitly unsupported.

The current relational model already provides an integer, nullable integer, enum, binary-collated string, nullable date, and `ParentId` integer suitable for property-to-property arithmetic. Add only the further numeric test properties needed to cover library-supported numeric types and mixed/nullable pairs.

## Goals / Non-Goals

**Goals:**
- Keep one provider-independent behavior matrix and run it unchanged against SQL Server and MySQL.
- Verify parser success separately from provider query translation/execution, and assert both an explicit expected ID set and equivalent handwritten-query results.
- Cover requested membership and arithmetic forms using the existing deterministic records, adding data only when the current seed cannot distinguish a case.
- Let provider-specific translation or semantics differences be visible and diagnosable.

**Non-Goals:**
- Add or alter expression syntax, library behavior, public APIs, providers, dependencies, fixtures, or database lifecycle.
- Test unsupported unary negation as relational syntax or move parser/operand failure assertions out of unit tests.
- Assert provider SQL text or claim support beyond the exact tested expressions, types, versions, and database configuration.

## Decisions

### 1. Extend the existing shared predicate-case runner

Add membership and arithmetic cases to the current shared case table and retain its existing assertion flow: parse and assert parse success; pass the generated lambda directly to `IQueryable.Where`; execute a handwritten equivalent against the same database; compare each result to the explicit sorted ID list and to each other. Keep provider-specific setup and metadata checks in their existing test classes.

Do not create a second operator-specific harness or copy the case list into provider tests. The existing runner already ensures that each predicate is exercised on both providers and that SQL translation failures occur at query execution rather than being confused with parser validation.

### 2. Use the deterministic seed to form a contract-driven matrix

Use current `Integer`, `NullableInteger`, `Name`, `Status`, `Enabled`, and `ParentId` values for single/multiple numeric membership, string membership containing `%` and `_`, enum membership, nullable/null-list cases, and property-to-property arithmetic. Add test-only numeric properties and fixed values only where necessary for nullable arithmetic and mixed numeric promotion; keep IDs and existing seed expectations stable.

Include explicit positive and negative membership cases, including single- and multi-value lists, null and non-null nullable values, null-containing lists, and empty lists. Include membership under AND, OR, NOT, and parentheses. The expected results follow the documented complement contract rather than SQL `NOT IN` intuition.

Exercise addition and subtraction against constants and a second property; include mixed and chained operations, precedence and explicit grouping, logical composition, nullable results, and signed negative literals. General unary-negation syntax is omitted because the README and subtraction contract explicitly reject it; existing unit tests remain responsible for rejection cases.

Build the numeric integration matrix from the numeric CLR types accepted by the library. Use provider-mappable model properties, nullable counterparts where supported, and representative compatible mixed pairs. Keep each provider's results explicit; if a provider cannot map or translate a library-supported expression, preserve it as a reported compatibility finding instead of silently excluding or rewriting the case. Do not infer that a passing subset guarantees translation for all types or expressions.

### 3. Reuse existing provider semantics and lifecycle

Do not modify the Testcontainers fixtures, configured SQL Server/MySQL versions, collation settings, date precision, cleanup, or CI matrix. Exact string membership uses the existing binary collations, avoiding implicit assumptions about case/accent behavior. Null-list cases validate the configured EF Core/provider result against the same handwritten predicate and explicit expected IDs. No date arithmetic is introduced, so date precision configuration remains unchanged.

### 4. Keep failures categorized by boundary

The shared relational runner first reports an unsuccessful parse with the query text and parser exception, then executes the resulting expression and lets provider translation or database execution errors fail at that stage. Do not catch translation exceptions and reclassify them as parser errors. Keep malformed operators, missing operands, and unsupported operand-pair tests in the existing unit-test project; its `NotInTest`, `InterpreterPlusTest`, and `InterpreterSubtractionTest` already cover those parser contracts.

If integration execution demonstrates a compatibility defect, record the provider, expression, observed failure/result, and expected contract, then propose a narrowly scoped follow-up. Do not repair production translation or alter the contract within this test-coverage change.

## Risks / Trade-offs

- [Provider-supported CLR numeric mappings may differ] → Exercise the library's numeric type set against both providers, retain provider identity in failures, and report confirmed gaps separately rather than disguising them as parser failures.
- [Relational null compensation can differ from compiled LINQ intuitions] → Assert the existing explicit complement contract and same-provider handwritten results under the current configuration; document any discrepancy as a finding.
- [Adding test columns or seed values can disturb existing expectations] → Preserve current IDs and values, use additional fixed values only for missing numeric shapes, and run the entire shared case table on both providers.
- [String collation can make membership results provider-dependent] → Rely on the already configured binary collations and exact spellings; do not add cross-provider case-folding expectations.

## Migration Plan

No production migration or rollback is needed. Extend only the integration test project and, if required, its test model and seed. Run the provider-filtered integration tests for SQL Server and MySQL, then run the full integration project and existing unit-test project. If any case exposes an existing provider incompatibility, report it independently and keep code fixes outside this change.
