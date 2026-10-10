# Spec Delta

## Purpose

Enable clients to exchange schema-aware nested filters as strict JSON and build equivalent predicates directly, while preserving legacy structured filtering behavior.

## ADDED Requirements

### Requirement: Exclusive logical and condition shapes
A filter node SHALL have exactly one of these shapes: `{"and":[nodes...]}`, `{"or":[nodes...]}`, `{"not":node}`, or `{"field":string,"op":string,"value":value}`. Keys SHALL be case-sensitive. AND/OR SHALL have nonempty child lists; one-child groups SHALL be valid and retain their child semantics. NOT SHALL have exactly one nonnull child, not an array. Conditions SHALL have nonblank field/operator strings and a present value, including explicit null. Null nodes/children, unknown members, duplicate keys, mixed shapes, missing values, wrong member kinds, nested membership arrays, and unknown operators SHALL fail explicitly without a usable tree or predicate. In-memory models SHALL prevent ambiguous shapes and reject cycles. Child ordering and duplicates SHALL be preserved. Arbitrary logical nesting SHALL be supported subject to configured limits, without recursive call-stack failure.

#### Scenario: Representative mixed tree
- **WHEN** the requested AND containing an OR of Status/Priority conditions and NOT of Department membership is supplied
- **THEN** it represents `(Status = 'Active' or Priority >= 3) and not (Department in ['Archived', 'External'])`
- **AND** `(A OR B) AND NOT (C OR (D AND E))` can independently be represented with arbitrary condition leaves and nested groups

#### Scenario: Empty and single-child groups
- **WHEN** `{"and":[]}`, `{"or":[]}`, and `{"and":[{"field":"Id","op":"=","value":1}]}` are validated
- **THEN** the empty groups fail with `filter-tree-empty-group` and the one-child group is equivalent to Id equality

#### Scenario: Ambiguous and malformed nodes
- **WHEN** a node has both `and` and `or`, two `field` keys, an unknown member, a missing `value`, a null child, or an unknown operator
- **THEN** validation identifies the offending node/member and returns no usable result
- **AND** explicit `"value":null` is not confused with a missing value

### Requirement: Typed literals and explicit references
Values SHALL distinguish null, Boolean, exact JSON number, string, membership array, and `{"variable":"name.path"}` references. Variable names SHALL be nonblank, unprefixed paths; literal strings beginning with a variable prefix SHALL remain literal strings. No arbitrary JSON object SHALL be accepted as a value. Arrays SHALL be supported only for IN/NOT IN and SHALL contain scalar literals or scalar references, never arrays or collection expansion slots. A collection reference SHALL be supported as the entire value of a membership condition.

Schema-directed conversion SHALL support the library's existing Boolean, character, signed/unsigned numeric, decimal/floating, string, enum, Guid, DateTime, DateTimeOffset, TimeSpan, and nullable scalar targets. Numeric JSON SHALL not first round through Double; range/precision failures SHALL be explicit. Character, enum names, Guid, temporal and duration values SHALL use documented string encodings; enum underlying integral values SHALL be accepted where supported. Temporal conversion for the new tree SHALL use an explicit immutable conversion context and reject current-date/local-zone-dependent conversion rather than guessing. Existing legacy temporal defaults SHALL be unchanged. Nonfinite numbers and unsupported CLR value types SHALL be rejected, not stringified. Nonmembership nulls and membership null items SHALL obey existing target nullability/operator rules.

#### Scenario: String and variable are distinct
- **WHEN** a String field is compared with `"$user.name"` and with `{"variable":"user.name"}`
- **THEN** only the latter resolves a variable during construction and both retain their distinct JSON representation

#### Scenario: Numeric and CLR boundaries
- **WHEN** UInt64 maximum, high-precision Decimal, a character, enum, Guid, full timestamp with explicit offset, or negative TimeSpan is exchanged as JSON
- **THEN** schema-directed conversion preserves supported values and rejects overflow or unsupported representations explicitly

#### Scenario: Collections and null
- **WHEN** nullable Id uses IN with `[null,1,1,{"variable":"user.id"}]` or a whole collection reference
- **THEN** membership retains order, duplicates, nulls, and references through serialization and applies the existing runtime membership meaning
- **AND** a nested array or collection reference inside an array fails instead of being implicitly flattened

### Requirement: Direct construction and metadata validation
The library SHALL expose synchronous/asynchronous, generic/runtime-type throwing and evaluation-based tree predicate construction. Tree construction SHALL emit short-circuit AND/OR and logical NOT directly without serializing conditions, membership elements, or trees to expression text and reparsing. It SHALL reuse existing field binding, exact full-path aliases, conversion/operator applicability, nullable comparison, and variable-resolution semantics, except the explicit literal/reference distinction and documented immutable temporal context of the new contract.

Metadata-only tree validation SHALL use entity/variable declarations and applicable policy metadata, SHALL NOT execute resolvers, initialization hooks, getters, application conversions/operators/ToString, compiled expressions, or runtime collection enumeration, and SHALL distinguish deferred runtime value availability/cardinality from invalid metadata. Runtime construction SHALL independently enforce all applicable restrictions before use; static failures SHALL occur before application resolution. Async cancellation SHALL remain cancellation. Evaluation failures SHALL have null Result; throwing operations SHALL carry structured diagnostics rather than silently accepting or dropping invalid leaves.

#### Scenario: Equivalent handwritten predicate
- **WHEN** nested AND/OR/NOT conditions are built for representative entity data
- **THEN** generated predicates and equivalent handwritten predicates select identical records in memory and in existing SQL Server/MySQL integration infrastructure

#### Scenario: Metadata purity and deferred resolution
- **WHEN** tree validation references a declared variable whose resolver/getter would throw and whose collection cardinality is unknown
- **THEN** application code is invoked zero times and unknown cardinality alone is not rejected
- **AND** construction subsequently enforces resolved cardinality before emitting membership

### Requirement: Structured tree diagnostics
Tree diagnostics SHALL reuse applicable semantic and policy codes and type/limit details. Nontext locations SHALL have Start/Length zero and RFC 6901 JSON Pointer InputPath relative to the tree root, with the root represented by the empty string. Condition embedding SHALL prefix the pointer with `/FilterTree`. Invalid source JSON SHALL produce explicit decoding diagnostics; no partial tree SHALL be returned. Stable shape/value codes SHALL include `filter-tree-invalid-shape`, `filter-tree-empty-group`, `filter-tree-null-child`, `filter-tree-unknown-member`, `filter-tree-duplicate-member`, `filter-tree-invalid-value`, and `filter-tree-invalid-json`. Unknown operators SHALL use `filter-tree-unknown-operator`. Restricted-field errors SHALL not disclose internal canonical paths/types, variable values, or denied-name suggestions.

#### Scenario: Positioned membership failure
- **WHEN** the third item of the value at the second child of an AND fails conversion
- **THEN** its diagnostic InputPath is `/and/1/value/2` with Start/Length zero
- **AND** the equivalent condition operation uses `/FilterTree/and/1/value/2`

#### Scenario: Restricted alias
- **WHEN** an alias maps to a denied member
- **THEN** `property-not-queryable` identifies the external `/field` location without suggesting denied names or exposing internal type details

### Requirement: Legacy adaptation without behavior changes
Existing Filter, FilterGroup, related extension/interpreter APIs, and old serialized payloads SHALL remain operational with unchanged signatures, ordering, AND-within-group/OR-between-group semantics, variable heuristics, trimming, null handling, exceptions, legacy diagnostic collections, and entry-point-specific empty-input behavior. Legacy adaptation SHALL NOT impose new-tree empty-group/reference/shape rules or policy accounting on old inputs. Throwing, evaluation, generic/runtime-type, synchronous/asynchronous, and policy/no-policy surfaces SHALL each preserve their own established behavior, including failure behavior that differs between entry points.

#### Scenario: Existing populated groups
- **WHEN** legacy groups are built through existing APIs after adaptation
- **THEN** their results agree with existing tests and equivalent ordered OR-of-AND composition

#### Scenario: Empty and invalid legacy inputs
- **WHEN** empty groups, empty sequences, null groups/children/lists, or invalid leaves are supplied to each existing entry point
- **THEN** its characterized success/failure, exception type, diagnostics, and resolver ordering remain unchanged
- **AND** new-tree rejection rules do not silently repair or reject previously accepted legacy inputs

### Requirement: Additive condition input composition
ConditionOptions SHALL accept an optional FilterTree. When present together with Filters, FilterGroups, and/or Where, all supplied inputs SHALL be AND-combined, without precedence, replacement, or flattening of their internal logic. Existing input execution order SHALL remain unchanged, with the tree processed after existing predicate inputs. Policy-aware condition operations SHALL validate the entire operation before resolution and enforce shared aggregate budgets. A failed new-tree condition operation SHALL expose no usable partial predicates or ordering result. When the tree is absent, legacy condition validity/failure behavior and serialized payloads SHALL remain unchanged; the new property SHALL be omitted on default serialization when null.

#### Scenario: All input forms present
- **WHEN** Where, flat Filters, OR-composed FilterGroups, and a FilterTree are supplied
- **THEN** the effective predicate requires all four inputs to match and policy budgets include all four

#### Scenario: New input fails
- **WHEN** other condition predicates are valid but FilterTree is malformed or denied
- **THEN** the condition fails explicitly with diagnostics and no usable partial condition or ordering clause

### Requirement: Public guide and acceptance evidence
Documentation SHALL include a dedicated nested-filter Markdown guide covering exact JSON/public API contracts, nested examples, typed encodings, references, validation/policy locations, conversion boundaries, and migration from FilterGroup. README SHALL contain a concise example and relative guide link. Examples SHALL compile against implemented APIs and documentation links SHALL resolve. Acceptance SHALL cover JSON round trips and malformed/ambiguous rejection, all supported type families, both representative nested forms, legacy compatibility, handwritten-predicate parity, and representative SQL Server/MySQL cases. Existing suites SHALL continue to pass.

#### Scenario: Verified documentation
- **WHEN** implementation examples and local links are checked
- **THEN** they use real APIs, fail explicitly before consuming invalid results, and link to the complete nested-filter guide
