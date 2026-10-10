# Proposal

## Why

String-to-string mappings expose CLR paths but cannot express business projections such as an order total, and the existing reflected schema is not an explicit public-field boundary. P1 adds compiler-checked expression registrations and opt-in public-schema resolution while preserving the existing schema, policies, and legacy APIs.

## What Changes

- Extend `ExpressionSchema` with registered expression fields and a strongly typed fluent `QuerySchema<T>` builder that produces the existing immutable schema type, not a competing schema/context model.
- Support direct, nested, and computed scalar selectors with inferred result types, preserved expression trees, parameter rebinding, and explicit registration errors.
- Make public-schema mode restrictive even when no fields are registered: only exact registered names resolve, case-insensitively; internal members and appended paths do not. Variables require explicit `$` references in this mode.
- Add independent filtering/sorting permissions, optional allowed operators, display name, description, nullability, and an immutable public metadata view that omits mapping bodies and internal paths.
- Share resolution and types across semantic analysis, string/direct/structured/tree predicates, conditions, and ordering. Reuse existing diagnostics and fail without partial results.
- Treat application expressions as opaque trusted configuration for navigation/collection policies. Intersect field operator permissions with query policies keyed by public field identity; retain legacy canonical-path rules outside public mode.
- Reject public-mode legacy mapping/canonical-member-override combinations; allow additional registered-name restrictions only to narrow permissions.
- Preserve selector null/conversion semantics rather than inventing null propagation. Keep construction and analysis compile-free; retain execution-only compilation of final keys for `IEnumerable` ordering.
- Add unit/compatibility coverage, SQL Server/MySQL execution comparisons, a dedicated guide, README quick start, and compilable examples.

## Capabilities

### New Capabilities

- `expression-field-mapping`: Typed expression registrations extending existing schema infrastructure, explicit public-field visibility, independent permissions, safe metadata, shared composition/diagnostics, relational verification, and documentation.

### Modified Capabilities

- `query-policies`: Distinguish trusted public expression fields from legacy canonical paths, bind permissions to public identities, enforce separate filter/sort permissions, and replace the existing prohibition on expression mapping registration with the opt-in contract.

## Impact

- Core remains `netstandard2.0` and EF Core-independent. Relevant existing surfaces include `ExpressionSchema`, `ExpressionQueryContext`, `BuildArgument`, property AST binding, semantic/tree validators, filter builders, `OrderByParser`, and `OrderByClause`.
- Existing overloads, reflected schemas, string mappings, canonical denials, bare-variable behavior, conversions, and legacy ordering remain unchanged outside explicit public mode.
- The implemented semantic-analysis and nested-filter-tree infrastructure belongs to still-active OpenSpec changes. This change builds on that code without editing those changes; their future spec synchronization must preserve this change's explicit public-mode exceptions.
- Reuse the xUnit unit project and existing Testcontainers SQL Server/MySQL fixtures. Strong C# typing guarantees member/type checking, not provider translation; no EF dependency or SQL rewriting is added to core.
- Out of scope: autocomplete UI, new query operators, automatic cost/tenant controls, provider-specific translation, and legacy mapping removal.
