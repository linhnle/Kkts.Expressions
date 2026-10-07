# Proposal

## Why

Exclusion filters currently require callers to negate an `in` predicate explicitly, and structured filters have no corresponding exclusion operator. Supporting `not in` makes common UI exclusion queries readable and consistent across predicate strings and structured filtering.

## What Changes

- Add the case-insensitive, two-keyword infix operator `not in`, accepting ordinary query whitespace between the keywords.
- Add `ComparisonOperator.NotIn` and structured `Filter.Operator = "not in"` support without renumbering existing enum values.
- Define exclusion as the Boolean complement of existing `in` membership, reusing its operand types, list conversion, variables, and null behavior rather than introducing SQL three-valued semantics.
- Support generic and runtime-type synchronous/asynchronous parsing, direct predicate builders, filter collections/groups, and condition `Where`/filters.
- Preserve arithmetic left operands, existing negation syntax, diagnostics, property mappings/allowlists, and cancellation behavior.
- Document syntax, structured-filter examples, and provider-dependent translation; add behavior and expression-tree regression tests.

## Capabilities

### New Capabilities

- `not-in-operator`: Negated membership in string predicates and structured filters, including compatibility and validation requirements.

### Modified Capabilities

None. The main specification inventory is currently empty.

## Impact

- Parser: `ComparisonOparatorParser` and operator registration in `Interpreter`; membership RHS routing must recognize the new multiword operator.
- Expression construction: `Internal/Nodes/Comparison`, enum/string operator mapping, `BuildBody`/`BuildBodyAsync`, `BuildBodyCore`, and `CorrectOperator`.
- Public APIs: additive enum member and filter operator spelling; existing signatures remain unchanged.
- Tests: existing xUnit interpreter, filter, group, condition, arithmetic, duration, culture, variable, and cancellation test patterns.
- Documentation: supported-operator table and examples in `README.md`.
- No new dependencies, target-framework changes, database-specific APIs, arithmetic-in-list syntax, or alternative spellings such as `notin`/`!in`.
