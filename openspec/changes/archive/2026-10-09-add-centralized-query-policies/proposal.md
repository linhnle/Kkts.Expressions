# Proposal

## Why

User expressions currently have property allowlists and metadata-aware editor checks, but no reusable complexity or per-field operator policy enforced across runtime entry points. Applications need one opt-in configuration for editors, structured UI filters, and server predicate construction, with server enforcement independent of editor validation.

## What Changes

- Add immutable `QueryPolicy` configuration and an `ExpressionQueryContext` binding a policy to the existing `ExpressionSchema`. The context offers policy-aware versions of existing analysis, predicate, structured-filter, condition, and ordering operations; existing signatures and no-policy behavior remain unchanged.
- Enforce expression length, parenthesis depth, atomic condition count, membership item count, entity navigation depth, collection access permission, and canonical per-field comparison-operator permissions.
- Share counting, field binding, alias normalization, permission intersection, and diagnostics between metadata analysis and runtime construction. Check text length before lexing and structural limits before recursive work or large allocations.
- Enforce runtime variable-backed membership sizes using bounded enumeration before expression construction; metadata analysis reports no invented runtime cardinality and never executes application code.
- Reuse `ExpressionDiagnostic` with additive limit/observed-value and structured-location metadata. Policy failures return no usable predicate, condition predicates, or ordering clause; throwing APIs retain explicit failure channels.
- Add a dedicated query-policy guide and README quick start, including application-owned tenant filtering, tested examples, compatibility decisions, recommended opt-in limits, and security boundaries.
- Preserve supported syntax, membership semantics, Filter collection AND/group collection OR behavior, conversion defaults, async cancellation, and the `netstandard2.0` target.

## Capabilities

### New Capabilities

- `query-policies`: Centralized policy configuration, deterministic counting, staged enforcement, permission-safe diagnostics, cross-entry-point consistency, compatibility, and documentation.

### Modified Capabilities

None. Existing `expression-editor-analysis` and `not-in-operator` requirements remain unchanged for legacy entry points. This change extends them through explicitly policy-aware operations rather than changing their default contracts.

## Impact

- Core library: `Interpreter`, `BuildArgument`, syntax lexer/analyzer, semantic analyzer, schema/path binding, runtime parser/nodes, membership conversion, sync/async filter/group builders, `ConditionOptions`, ordering parsers/builders, and evaluation-result conversion.
- Tests: existing xUnit unit tests for analysis, schema, membership, filters/groups, conditions, ordering, parser optimization, and documentation; existing relational regression suite for expression-tree compatibility.
- Documentation: new `docs/query-policies.md`, README relative link/quick start, and targeted semantic-analysis guide cross-reference.
- No new runtime dependencies or provider-specific SQL changes.
- The in-progress `add-expression-semantic-analysis` change already supplies the on-disk schema and diagnostic infrastructure. Implementation must coordinate with it, not duplicate its capability or silently alter its unarchived requirements.
- Computed lambda/expression mappings are not an existing mapping API: current mappings are exact external-name-to-CLR-path strings. This proposal does not add computed mapping registration; readable computed CLR properties remain application-owned opaque members.
- Out of scope: new operators/collection syntax, recursive structured-filter DTOs, UI/autocomplete, pagination additions, SQL translation changes, automatic tenant filtering, and database execution-cost guarantees.
