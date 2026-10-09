# Design

## Context

See [proposal.md](./proposal.md) for the cross-provider reproduction. The parser considers `char` numeric and emits CLR-style promotion to `int`; relational providers may apply database coercion rules to a character column differently. The same case produced different unexpected results on the existing SQL Server and MySQL fixtures.

## Goals / Non-Goals

**Goals:**
- Establish generated and handwritten query results for `char` arithmetic on both existing relational providers.
- Evaluate an explicit integral store representation as the narrowest candidate for retaining CLR character-code arithmetic without custom provider SQL.
- Record a clear outcome: a mapped configuration that preserves expected semantics, or a scoped provider limitation.

**Non-Goals:**
- Change the predicate parser's supported operand set or public API.
- Add provider-specific SQL translation, change package/provider versions, or broaden the test infrastructure.
- Assert that default string-backed `char` columns support numeric arithmetic.

## Decisions

Use the existing relational fixtures and a focused case with digit and non-digit characters. First reproduce default provider mapping behavior with the exact expected CLR code-point result. Then test an explicit EF Core value conversion from `char` to an integral store value on each provider, comparing parsed and handwritten predicates via relational `Where`. The conversion is a candidate test-model mapping, not an assumed solution; preserve it only if both providers return the same explicit expected IDs.

If the conversion does not preserve semantics or requires provider-specific behavior, do not introduce a SQL translation workaround. Record which provider failed and document the boundary for relational tests. This is narrower and safer than silently weakening the library's CLR numeric contract or promising provider-wide compatibility.

## Risks / Trade-offs

- [A converter may not be applied usefully to arithmetic expression trees] → Verify actual relational execution on both providers before accepting it; otherwise recommend a documented limitation.
- [Digit-only characters may hide string coercion] → Include a non-digit character whose numeric code-point result is distinct from parsed-digit coercion.
- [A result may depend on provider version/configuration] → Reuse the pinned EF Core/provider/database matrix and state that scope in the outcome.

## Migration Plan

No production or schema migration is planned. The follow-up adds only focused integration test mapping/configuration if proven effective, otherwise documents the observed translation limitation. Roll back any candidate mapping if it fails to preserve expected results on either provider.
