# Proposal

## Why

`Filter` collections AND their leaves and `FilterGroup` collections OR their groups, so structured clients cannot express arbitrary logical composition. A stable nested JSON contract and lossless, explicitly bounded expression conversion will let advanced query builders and expression editors exchange predicates without string generation becoming the runtime construction path.

## What Changes

- Add an EF/UI-independent filter tree with AND, OR, unary NOT, and field/operator/value conditions using the requested lowercase JSON shape.
- Add strict System.Text.Json serialization, lossless numeric handling, schema-directed scalar conversion, nullable values, membership arrays, and explicit `{"variable":"user.id"}` value references. Reject malformed, ambiguous, duplicate-key, and unknown-member payloads.
- Reject empty logical groups, accept single-child groups, and preserve child order. Build predicates directly using shared member, operator, conversion, permission, and variable rules.
- Add metadata-only validation and bidirectional expression conversion with deterministic output, JSON Pointer diagnostics, and explicit unsupported-conversion failures.
- Add independent filter-tree depth policy configuration, enforce all applicable existing budgets, and share aggregate budgets across condition inputs. Do not reinterpret parenthesis limits.
- Keep legacy DTOs, signatures, serialized payloads, exceptions, evaluation diagnostics, null behavior, and entry-point-specific empty-group behavior. Adapt legacy inputs internally without imposing new-tree validation on them.
- Add optional `ConditionOptions.FilterTree`; combine it with existing supplied filter inputs using AND, without replacing any input.
- Add a dedicated guide and compile-checked README example with a relative link.

No public breaking change is intended. New tree contracts are additive; the existing specifications' exclusions of recursive structured filters are deliberately superseded for these new entry points only.

## Capabilities

### New Capabilities

- `nested-filter-tree`: Public logical/value model, strict JSON contract, direct construction, metadata-only validation, legacy adaptation, condition integration, and documentation.
- `filter-expression-conversion`: Explicit representable subset, pure parsing/formatting, deterministic canonical text, editor interoperability, semantic round trips, and unsupported diagnostics.

### Modified Capabilities

- `query-policies`: Admit the new structured surface, define an independent tree-depth budget and tree accounting/locations, and retain all legacy parenthesis/group semantics.

## Impact

- Core `netstandard2.0` library: additive model/result/codec APIs, System.Text.Json package reference compatible with the existing target, `ConditionOptions` and async counterpart, `ExpressionQueryContext`, and all sync/async generic/runtime-type tree builders.
- Reuse/extract pure rules from `ExpressionSemanticAnalyzer`, `ExpressionGrammar`, `ExpressionOperatorRules`, `NumericOperands`, `ExpressionSchema`, `BuildArgument`, and `QueryPolicyExecution`; reuse runtime comparison/membership primitives in `Interpreter`. Keep the internal positioned syntax structure distinct from the public tree.
- Coordinate narrowly with the in-progress `add-expression-semantic-analysis` change: its private semantic reducer currently loses operand structure, and several shared-rule/recovery tasks are unfinished. This change supplies only the shared extraction needed by tree/conversion parity; it does not mark that change complete or redesign editor recovery.
- Extend existing xUnit unit tests and SQL Server/MySQL Testcontainers infrastructure with handwritten-predicate comparisons, adversarial policy tests, serialization tests, purity instrumentation, and round-trip tests.
- Documentation: new `docs/nested-filters.md`, README quick start, and directly related policy/semantic guide cross-references.
- Out of scope: query-builder UI, autocomplete, additional operators, SQL translation changes, tenant filtering, removal of legacy APIs, and a public general-purpose expression AST.
