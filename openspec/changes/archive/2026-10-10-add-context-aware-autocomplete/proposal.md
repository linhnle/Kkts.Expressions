# Proposal

## Why

Expression editors currently receive source classifications and semantic diagnostics but no cursor-aware completion API. P1 autocomplete should make declared, permitted query constructs discoverable while reusing the existing language and preserving the public-schema boundary.

## What Changes

- Add synchronous, UI-independent completion over exact expression text, a UTF-16 cursor offset, `ExpressionSchema`, `ExpressionVariableSchema`, and the applicable `ExpressionQueryContext` policy/restrictions.
- Return immutable completion items with stable kinds, labels, insertion text, precise replacement spans, descriptions, and applicable `ExpressionTypeInfo`; define deterministic ordering, deduplication, result/work limits, and explicit failure/suppression status.
- Recognize recoverable cursor-local operand, operator, value, variable, path, group, NOT, and membership contexts, including partial tokens and text after the cursor. Suppress unsafe guesses rather than require a completely valid predicate.
- Enumerate only queryable public names and compatible operators/values/variables. Preserve exact typed public-field registrations, legacy full-path aliases, navigation/collection restrictions, and determinable policy budgets.
- Add optional immutable, application-declared field value hints, distinct from allowed-value constraints. Offer existing enum/Boolean/null literals when applicable; never discover runtime values or assume built-in variable declarations.
- Add replacement, permission/non-disclosure, purity, compatibility, and measured typing-workload tests.
- Add a dedicated autocomplete guide, a concise README example with relative links, and one small runnable CodeMirror example outside the core package.

## Capabilities

### New Capabilities

- `expression-autocomplete`: Metadata-only, policy-aware, cursor-local completion contracts, replacement behavior, bounded processing, and editor integration.

### Modified Capabilities

None. Syntax highlighting, semantic analysis, public-field binding, policy enforcement, and runtime predicate semantics retain their existing requirements. Shared internal helper extraction is implementation reuse, not a change to those contracts.

## Impact

- Core `netstandard2.0` library: additive completion contracts and entry points; reuse `ExpressionLexer`, `ExpressionSyntaxParser`, `ExpressionAnalyzer`, `ExpressionSemanticAnalyzer`, `ExpressionGrammar`, `ExpressionOperatorRules`, `ExpressionConversionRules`, `NumericOperands`, schema/variable descriptors, and policy scanners/checks.
- Existing immutable schemas and query contexts remain reusable. Expression selectors, runtime resolvers, getters, conversions implemented by application code, and query execution are not completion inputs.
- Tests extend `src/Kkts.Expressions.UnitTest`; existing predicate and relational suites remain compatibility gates.
- Documentation: planned `docs/autocomplete.md`, README links, and an isolated sample under `examples/`. No editor, EF Core, or HTTP dependency is added to the library.
- Related in-flight plans: `add-expression-semantic-analysis` supplies the implemented semantic infrastructure; `add-expression-field-mapping` supplies the exact public-name contract and its policy amendments. They are not yet archived into main specs. This change explicitly follows the implemented public-mode exception rather than the older generic reflected-path wording.
- No new language syntax/operators/variables, runtime discovery, external suggestion service, language server, SQL changes, authorization, or tenant filtering.
