# Proposal

## Why

The relational operator coverage exposed provider-dependent results for numeric `char` arithmetic: with `CharValue` seeded as `'2'`, the predicate `CharValue + 1 = 51` selected no SQL Server rows and selected MySQL record 3, while CLR character-code arithmetic expects record 2. Since `char` participates in the library's numeric promotion rules, this follow-up should establish a provider-compatible mapping or expression representation, or explicitly document the relational limitation without changing unrelated arithmetic behavior.

## What Changes

- Add a focused relational reproduction for `char` addition/subtraction and compare the generated predicate with a handwritten predicate on SQL Server and MySQL.
- Determine whether an explicit numeric relational mapping/value converter preserves the CLR character-code semantics through each provider; do not treat string-to-number coercion as equivalent to CLR `char` arithmetic.
- If a provider-neutral mapped solution is demonstrated, add the narrowest model/test guidance needed to verify it. Otherwise document the provider limitation and define whether character arithmetic is outside the relational-translation guarantee.
- Keep this investigation separate from the broader membership and arithmetic test coverage change; do not add provider-specific SQL or change public APIs as a shortcut.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This is a focused provider-compatibility investigation and adds no library behavior guarantee; the change opts out of a behavior delta spec with `skip_specs: true`.

## Impact

- Follow-up scope is limited to numeric `char` property mapping/translation and focused SQL Server/MySQL relational tests.
- Evidence is from EF Core 10.0.9, SQL Server 2022, and MySQL 8.4.4 using the existing pinned test fixtures: the temporary shared case expected record ID 2 for `CharValue + 1 = 51`; SQL Server returned no IDs and MySQL returned ID 3. The case was removed from the passing shared matrix pending follow-up, rather than weakening its CLR-derived expected result.
- No production implementation, public API, provider set, or unrelated test infrastructure is authorized by this proposal.
