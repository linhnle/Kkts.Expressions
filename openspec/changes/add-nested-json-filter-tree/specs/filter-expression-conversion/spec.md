# Spec Delta

## Purpose

Allow query builders and expression editors to exchange representable filter trees and canonical expressions without executing application code or silently changing predicate meaning.

## ADDED Requirements

### Requirement: Explicit representable subset
Bidirectional conversion SHALL support ordered AND/OR/NOT composition, grouping, one entity field on the left of each existing comparison, a literal or explicit variable on the right, literal/reference membership lists, a whole collection variable for membership, and existing field string comparison functions with representable value arguments. Aliases, allowed operator aliases, case-insensitive language operators, existing list delimiters, and literal escaping SHALL follow the existing language. A bare Boolean entity field SHALL be representable as equality with true when existing predicate semantics agree.

Arithmetic, concatenation/computed operands, field-to-field comparisons, reversed operand comparisons, entity-member membership sources, standalone Boolean variables/constants, or function/operand forms outside this subset SHALL return `filter-conversion-unsupported` at the unsupported construct, not be folded, reordered, dropped, or approximated. Values whose spelling cannot preserve literal/reference identity, precision, or conversion semantics in the existing grammar SHALL likewise fail explicitly. No public general expression AST or new operators SHALL be introduced.

#### Scenario: Supported expressions
- **WHEN** `(Status = 'Active' or Priority >= 3) and not (Department in ['Archived', 'External'])` or `(A = 1 or B = 2) and not (C = 3 or (D = 4 and E = 5))` is converted
- **THEN** the tree preserves each logical branch and the same predicate meaning

#### Scenario: Unsupported expressions remain intact
- **WHEN** `Price + Discount > 10`, `Price = Discount`, `1 < Price`, or membership sourced from an entity member is converted
- **THEN** conversion returns an explicit unsupported diagnostic covering the construct and no partial tree
- **AND** ordinary expression parsing remains unchanged

#### Scenario: Supported function and Boolean field
- **WHEN** `Name.contains('x') and IsEnabled` is converted for permitted String/Boolean fields
- **THEN** it maps to Contains and Equal(true) conditions without executing field getters

### Requirement: Pure schema-aware conversion
Parse-to-tree and tree-to-text operations SHALL accept entity schema, optional declared variable metadata, and immutable conversion context, with policy-aware variants. They SHALL use non-executable syntax and shared metadata semantic rules, not runtime predicate construction. Conversion SHALL NOT execute variable resolution, initialization, getters, runtime collection enumeration, application conversion/operators/ToString, or compiled expressions. It SHALL preserve explicit variable paths rather than substituting values; literal strings SHALL never become references. Metadata absence, syntax/semantic errors, context-dependent conversion, or unrepresentable values SHALL yield explicit diagnostics and no usable conversion result. Supported runtime variable conversions SHALL remain deferred, not become fabricated invalid values.

#### Scenario: Instrumented purity
- **WHEN** valid, malformed, denied, and unsupported inputs reference declared variables and throwing application members
- **THEN** parsing/formatting invokes application code zero times, including error paths

#### Scenario: Quoted prefix-sensitive membership value
- **WHEN** formatting a literal membership string would make the existing runtime parser treat it as a variable
- **THEN** formatting reports `filter-conversion-unsupported` unless an existing supported escaping form demonstrably preserves literal semantics
- **AND** direct tree construction still treats the string as literal

### Requirement: Deterministic semantic round trips
Formatting SHALL produce deterministic canonical operators, lowercase logical keywords, explicit `not (...)`, stable precedence-preserving parentheses, invariant numeric literals, canonical supported typed strings, standard list delimiters, and grammar-correct quoting/escaping. Child order SHALL be preserved; formatting SHALL NOT apply De Morgan transformations, reorder leaves, deduplicate values, or normalize NOT into a different comparison. Original whitespace and operator aliases need not survive. Under the same schema, conversion context, policy, and runtime variable values, supported tree-to-text-to-tree and text-to-tree-to-text conversions SHALL preserve predicate semantics, target-typed literal values, nulls, and literal/reference identity, even when redundant groups or spellings differ. If this cannot be guaranteed, conversion SHALL fail rather than emit plausible but inequivalent output.

#### Scenario: Both semantic directions
- **WHEN** nested predicates, escaped strings, nullable fields, membership duplicates, enum/Guid/temporal/duration values, numeric extremes, aliases, and references undergo both round-trip directions
- **THEN** resulting predicates agree with the originals and handwritten baselines over representative records
- **AND** formatting the normalized result again is stable

#### Scenario: Precedence and order
- **WHEN** an OR appears under AND or nested NOT and comparison-function calls are formatted
- **THEN** required parentheses preserve semantics and the original ordered child traversal is retained

### Requirement: Shared editor and query-builder diagnostics
Conversion SHALL reuse existing syntax spans and applicable semantic/policy diagnostic codes. Unsupported text conversion SHALL identify zero-based UTF-16 source spans; unsupported tree formatting SHALL identify JSON Pointer paths. Permission checks SHALL precede metadata disclosure, and suggestions SHALL retain existing permitted-name protections. Policy-aware conversion SHALL enforce input and output budgets independently; text parsing SHALL obey source parenthesis/length limits and the resulting tree's tree-depth budget, while formatting SHALL enforce tree limits and generated-text limits without altering the tree to evade them. The existing syntax-only analyzer's completeness, recovery, tokens, and whole-input contract SHALL remain unchanged.

#### Scenario: Policy-constrained exchange
- **WHEN** a valid tree fits tree-depth limits but its required canonical text exceeds a configured text/parenthesis limit
- **THEN** formatting fails with the applicable policy code and no output rather than reducing grouping unsafely

#### Scenario: Restricted fields in either editor
- **WHEN** text conversion or tree validation references a restricted field or alias
- **THEN** both report the applicable denial without restricted-name/type disclosure or denied-name suggestions
