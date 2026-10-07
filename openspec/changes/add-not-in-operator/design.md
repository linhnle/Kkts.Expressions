# Design

## Context

See [proposal.md](./proposal.md) for motivation and scope.

The library targets `netstandard2.0`; its xUnit test project targets `net10.0` and uses EF Core InMemory. Membership has two construction paths: parsed comparisons in `Internal/Nodes/Comparison`, and direct/structured builders in `Interpreter`. Both emit `Enumerable.Contains<T>`.

`ComparisonOparatorParser` currently delegates to `Parser.AcceptOperator`, which ends a token when whitespace follows any operator text. Its `in` RHS path selects an array parser or a collection-variable parser. The shared lexer also serves logical operators, so allowing arbitrary whitespace within all operators would be a compatibility risk.

Other membership-specific branches are spread across enum mapping, `CorrectOperator`, scalar conversion in `BuildBody`/`BuildBodyAsync`, and arithmetic operand preparation. In particular, arithmetic membership skips ordinary comparison normalization and uses the computed left type as the element type.

## Goals / Non-Goals

**Goals:**
- Extend existing membership construction rather than implement a separate exclusion engine.
- Keep two-word recognition local and preserve parser indices, cached token text, keyword boundaries, and negation behavior.
- Make all public filter/predicate/condition entry points agree on accepted inputs and output semantics.

**Non-Goals:**
- SQL-specific null semantics or guaranteed translation for every relational provider.
- New operator aliases, collection-property operators, arithmetic inside list literals, or changes to sorting.
- A wholesale lexer rewrite, new dependencies, framework changes, or unrelated variable-resolution fixes.

## Decisions

### 1. Treat `not in` as one comparison token with explicit keyword separation

Extend `ComparisonOparatorParser` to recognize the `not` prefix, require a query-whitespace separator, then consume exactly `in` and finish at the normal operator boundary. Retain canonical internal text `not in` regardless of separator length/type. Preserve source indices from the original input and invalidate token caches through existing append mechanisms.

Do not globally relax `Parser.AcceptOperator`: that would make unrelated operators accept embedded whitespace and could conflate `notin`, identifiers, and partial keywords. Do not rewrite the predicate string into `not(...)`: string rewriting risks corrupting quoted text, grouping, diagnostics, and source positions.

Use the existing `in` RHS parser choices for the completed new operator. Keep `not(...)` and prefix negation in their existing parser paths; only an infix comparison position recognizes `not in`.

### 2. Use additive public registration and narrow spelling normalization

Append `ComparisonOperator.NotIn = 11`; retain all existing numeric enum values. Register a canonical `ComparisonNotIn` spelling in `Interpreter` and its comparison-operator collection.

Map structured operators through the existing `GetComparisonOperator` path. Normalize only the supported multiword spelling using the same accepted whitespace rules as query parsing; do not make arbitrary malformed spellings valid or broadly relax existing operator spellings. Existing `Filter.Operator` trim/lowercase behavior remains compatible.

Include `NotIn` wherever `CorrectOperator` permits `In`, including string, Boolean, Guid, enum, numeric, date/time, and duration branches. Membership-specific scalar-conversion exclusions in both synchronous and asynchronous builders must treat both operators alike, especially the TimeSpan exemption.

### 3. Negate shared membership construction

Build the same typed `Enumerable.Contains<T>(collection, value)` expression as `in`, then apply `Expression.Not` for exclusion. Factor a small internal membership predicate/helper where needed to keep operator classification and construction consistent across both paths; prefer existing reusable code over introducing a general operator framework.

In `Comparison`, both membership operators must select the same RHS element type and bypass arithmetic comparison normalization. This includes numeric addition/subtraction and computed string operands where already accepted by `in`.

Direct/structured construction must keep list parsing and conversion equivalent to `In`. Async construction must await supported list-element resolution with the caller's cancellation token rather than accidentally routing the new operator through synchronous list building. If a shared membership routine needs adjustment, keep it narrowly scoped and verify existing `In` regressions.

Alternative: expand to a series of inequalities. Rejected because it duplicates conversions, changes tree shape/size, and diverges for nullable values and variable collections.

### 4. Preserve .NET complement semantics

`not in` is exactly `!(value in collection)`. For nullable properties, null is excluded only when the collection contains null; an empty collection always yields true for exclusion. Do not add null guards or infer SQL three-valued logic.

The tree contains native membership and negation, not compiled delegates or `Expression.Invoke`. Relational translation remains provider-dependent, as with existing membership and arithmetic. EF Core InMemory tests establish integration, not relational SQL guarantees.

Implementation discovery: the existing list parser rejects unquoted `null`.
The user approved extending the shared list parser to accept this literal for
both membership operators, as required by the nullable scenarios. Quoted
`'null'` remains a string, and null cannot be converted to a non-nullable value
type. Existing quoted Boolean and nullable empty-string representations remain
supported.

### 5. Test behavior at each integration boundary

Add focused operator tests using the existing interpreter and filter test conventions. Assert compiled true/false outcomes and equivalence with explicitly negated `in`, rather than only checking that expressions are non-null.

Cover:
- Keyword case, spaces/tabs/newlines, incomplete tokens, rejected `notin`/`!in`, quoted text, and valid property identifiers containing keyword substrings.
- Numeric, string, Boolean, Guid, enum, date/time, duration, and nullable types; null elements, empty lists, duplicates, and conversion errors.
- Literal lists, collection variables, list-element variables, mappings/allowlists, and async cancellation.
- Arithmetic left operands with both literal and collection-variable RHS, mixed logical expressions, grouping, and nested negation.
- Generic/runtime-type sync/async parsing; enum builders; individual filters, collections/groups, and conditions.
- Native `Not(Contains(...))` output and absence of invocation/delegate compilation.

Follow the supplied `ConditionOptionsPlusTest` pattern for condition `Where` integration and invalid-condition errors, adding dedicated exclusion tests rather than editing unrelated arithmetic assertions. Extend the existing culture and TimeSpan patterns where useful.

## Risks / Trade-offs

- [Multiword parsing can consume operand text or collide with prefix negation] -> Require exact boundaries and test malformed sequences, identifiers, original negation, and quoted strings.
- [An `In`-only branch can remain and change type conversion or arithmetic behavior] -> Audit every membership-specific branch and pair representative `In`/`NotIn` tests across both construction paths.
- [Async structured membership can fall through a synchronous resolution path] -> Test with an async-only resolver and cancellation during list-element resolution, not merely a pre-cancelled token.
- [Nullable SQL translation differs from compiled LINQ behavior] -> Specify complement semantics and document provider dependence without adding a relational-provider dependency.
- [New enum handling silently falls back to equality] -> Add explicit handling in validation and construction; test generated tree shape and non-matching values.

## Migration Plan

This is an additive API/grammar change requiring no data migration. Implement parser and builder wiring together, run focused compatibility tests, and update the operator documentation before release. Existing explicit negation remains valid. Reverting the new parser registration, enum wiring, and documentation removes the feature without changing existing enum values or persisted data.
