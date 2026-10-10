# Spec Delta

## Purpose

Allow applications to publish compiler-checked scalar query fields, including business computations, through the existing schema contract with explicit visibility, independent permissions, and consistent analysis and construction.

## ADDED Requirements

### Requirement: Typed registration extends the existing schema
The library SHALL provide fluent generic field registration accepting an expression from the entity type to an inferred scalar result type and producing the existing immutable entity schema type in explicit public-schema mode. Existing reflected-schema creation SHALL remain unchanged. Fields SHALL support separate filter and sort permissions, each defaulting to enabled, optional allowed comparison operators, display name, description, and nullability metadata. An omitted operator set SHALL allow existing applicable operators; an explicitly empty set SHALL deny filtering comparisons without denying sorting.

Registration SHALL preserve the selector expression rather than compile, invoke, partially evaluate, or stringify it. The inferred result type SHALL be the actual generic selector result type, not Object boxing or a member-path approximation. References to removed or renamed entity members and invalid C# operand types SHALL fail application compilation.

#### Scenario: Direct nested and computed registration
- **WHEN** an application registers customerName from an entity's nested customer name, total from Decimal unit price multiplied by Int32 quantity, and createdAt from DateTime
- **THEN** the schema exposes those three names with String, Decimal, and DateTime result metadata
- **AND** selector trees retain their nested access and multiplication semantics without application execution

#### Scenario: Compilation catches member changes
- **WHEN** an entity member used by a registered selector is removed or renamed without updating that selector
- **THEN** application compilation fails at the C# member reference
- **AND** no runtime string mapping silently substitutes another member

### Requirement: Registration validation and supported expression subset
Field names SHALL be single unprefixed identifiers compatible with the existing query grammar, without dotted paths, surrounding/internal whitespace, variable prefixes, reserved literal/operator names, or comparison-function collisions. Lookup and duplicate detection SHALL use ordinal case-insensitive comparison. Blank/null names, null selectors, duplicate case-insensitive names, invalid permission/operator/nullability metadata, mismatched entity/result declarations, and unsupported expression shapes SHALL produce explicit configuration argument errors identifying the field and reason before processing user input.

P1 SHALL accept scalar results supported by existing conversion/type metadata, including nullable forms. Selector bodies SHALL support readable member accesses, constants, the entity parameter, scalar-returning method calls, nonassignment arithmetic/comparison/logical binary expressions, coalesce, scalar conversions, unary arithmetic/logical operations, and conditional/default expressions. Nodes SHALL retain their declared methods, checked behavior, and types. Invocation nodes, nested lambdas/quotes, blocks, assignments, dynamic/extension nodes, construction/initialization nodes, index access, free parameters, and nonscalar results SHALL be rejected explicitly. Application-defined method calls returning supported scalar types SHALL be valid configuration without being promised provider translation.

#### Scenario: Case and invalid configuration
- **WHEN** total and TOTAL are both registered, a selector is null, a name contains a dotted suffix, or an undefined comparison operator is supplied
- **THEN** configuration fails explicitly and does not create a partially registered field
- **AND** a single total registration resolves TOTAL successfully

#### Scenario: Valid method call differs from unsupported invocation
- **WHEN** a typed selector calls an application static method returning Decimal, and another selector contains a delegate invocation
- **THEN** the method-call selector is accepted without calling the method
- **AND** the invocation selector is rejected with a clear unsupported-shape error

### Requirement: Explicit public-field boundary
Public-schema mode SHALL resolve only registered field identifiers, including when the registration set is empty. It SHALL NOT fall back to reflected entity names, internal members referenced by mappings, legacy aliases, mapping prefixes, or appended member paths. Fields with object or collection results SHALL not implicitly expose their members. Scalar comparison-function syntax using a registered receiver SHALL remain supported where already applicable, but member suffixes such as createdAt.Year SHALL require their own explicitly registered projection.

In this mode, variable references SHALL require the existing explicit `$` syntax in string expressions and explicit reference values in structured trees. Unknown or denied bare names SHALL not initialize or call a variable resolver. Existing bare-variable fallback SHALL remain unchanged outside public mode.
This public-mode exception SHALL take precedence over legacy reflected-member
and bare-variable-binding requirements in the semantic-analysis and
nested-filter capabilities; those requirements remain unchanged for legacy
schemas. Tree validation and expression/tree conversion SHALL bind through the
same exact registered public-field metadata rather than discovering internal
entity members.

#### Scenario: Private inputs to a public computation
- **WHEN** total maps to UnitPrice multiplied by Quantity
- **THEN** total can be used when permitted, but UnitPrice and Quantity independently report unknown-property unless separately registered
- **AND** diagnostics do not disclose the computation or internal member names

#### Scenario: Exact names and existing functions
- **WHEN** customerName.contains('A'), customerName.Length = 1, and createdAt.Year = 2026 are queried
- **THEN** the existing contains comparison is valid when permitted and type-compatible
- **AND** both appended member paths report unknown-property unless represented by separately registered field names

#### Scenario: Empty schema and variable fallback
- **WHEN** a public schema has no registrations and an input uses Id = 1, even with an Id variable available
- **THEN** Id reports unknown-property, construction fails without a predicate, and the resolver is not called
- **AND** an explicitly declared $Id remains subject to ordinary variable metadata and runtime rules

### Requirement: Selector nullability and conversions preserve C# semantics
Registered field type and nullable-value metadata SHALL follow the selector result type. Nonnullable value results SHALL remain nonnullable, nullable value results SHALL remain nullable, and reference results SHALL default to unknown nullability unless explicitly overridden. Contradictory nullable-value overrides SHALL be rejected. Reference nullability declarations SHALL be metadata claims, not generated runtime guards.

Composition SHALL preserve C# numeric promotions, lifted/checked operators, explicit conversions, coalesce, and conditional branches already present in selectors. It SHALL NOT infer a different result type from the last member, add null propagation, remove meaningful conversions, or insert boxing to unify keys. A navigation through a nullable reference SHALL retain its ordinary C# behavior; applications SHALL supply explicit guards where null-safe in-memory behavior is needed. User values SHALL use existing conversion/operator rules against the registered result type, not types of internal computation operands.

#### Scenario: Explicit nullable navigation
- **WHEN** a selector is x => x.Customer == null ? (int?)null : x.Customer.Rank and a record has no Customer
- **THEN** the registered field is Nullable<Int32> and the composed expression returns null for that record
- **AND** comparisons and membership use the existing nullable field rules

#### Scenario: Unguarded path is not rewritten
- **WHEN** x => x.Customer.Name is registered and evaluated in memory for a record with null Customer
- **THEN** the ordinary C# null-reference behavior is preserved
- **AND** registration and semantic analysis do not execute the selector or fabricate a null-safe result

#### Scenario: Computed operand semantics
- **WHEN** Decimal price multiplied by Int32 quantity, explicit Int64 conversion, nullable multiplication, or checked arithmetic is registered
- **THEN** generated field operands retain the selector's resulting CLR type, conversions, lifting, and checked behavior
- **AND** literals are converted against that resulting type using existing query semantics

### Requirement: Independent permissions and configuration conflicts
Filtering SHALL require the registered field's filter permission, applicable public-name restrictions, and the intersection of field and query-policy operator permissions. Sorting SHALL independently require sort permission and applicable public-name restrictions, but SHALL not depend on filter permission or comparison operator sets. Fields defaulting to both permissions denied SHALL remain known metadata fields but unusable for those operations.

Combining public-schema mode with nonempty legacy string mappings or canonical-member metadata overrides SHALL fail at configuration time rather than establish ambiguous precedence. Additional nonempty valid-property allowlists SHALL only narrow access by registered public names; unknown names SHALL be configuration errors. Omitted/empty additional legacy-style allowlists SHALL mean no additional narrowing, not unrestricted entity exposure. Policy rules SHALL bind to public field identity; differently named registrations using the same internal expression SHALL remain independently governed fields.

#### Scenario: Filter-only sort-only and deny-all fields
- **WHEN** total permits filtering only, label permits sorting only, and secret permits neither
- **THEN** total filtering and label sorting can succeed, total sorting and label filtering are denied, and secret is denied in both operations
- **AND** these decisions agree across applicable string, direct, structured, tree, condition, and ordering entry points

#### Scenario: Effective operators and ordering
- **WHEN** a field allows Equal/In while its policy allows Equal/GreaterThan
- **THEN** only Equal is available for filtering
- **AND** sorting remains permitted if the field's sort permission is enabled, including when the effective filtering operator set is empty

#### Scenario: Fail-fast conflicts and narrowing
- **WHEN** a public schema is combined with a legacy alias map, canonical property overrides, or an additional allowlist naming an internal member
- **THEN** configuration fails before query processing or resolver initialization
- **AND** an allowlist of registered names only can further deny either operation without exposing more fields

### Requirement: Shared resolution composition and analysis
The same schema field types, identities, nullability, and operation permissions SHALL govern semantic analysis, synchronous/asynchronous generic/runtime-type string predicates, direct field/operator/value predicates, flat/grouped structured filters, nested filter trees, condition construction, and string/structured ordering. Tree validation and metadata-only expression/tree exchange SHALL recognize registered fields without rebuilding them as internal member paths or executing selectors.

Generated predicates and queryable ordering keys SHALL compose selector bodies by binding their entity parameter to the operation's entity parameter, without introducing invocation nodes, compiling selectors, or calling application code during construction. Multiple fields and combined conditions SHALL have no unbound or accidentally captured entity parameters. Sorting SHALL use the actual selector result type for all keys, including ThenBy and descending variants. In-memory enumerable ordering SHALL preserve existing behavior by compiling only the final composed ordering lambda at ordering execution, not during registration, metadata analysis, or ordering-clause construction.

#### Scenario: Rebinding and combined fields
- **WHEN** selectors with different parameter names are used in a conjunction and multi-key ordering
- **THEN** generated trees refer to each operation's entity parameter, contain no invocation nodes, and execute equivalently to handwritten LINQ
- **AND** async and runtime-type variants retain the same field decisions and key types

#### Scenario: Semantic and construction parity
- **WHEN** a Decimal total field is analyzed or constructed with total > 'abc', total.contains('x'), or an unregistered entity name
- **THEN** metadata analysis rejects the same statically incompatible values, operators, and fields as runtime construction
- **AND** values and operator checks use Decimal field metadata rather than inspecting or executing the multiplication

#### Scenario: Tree exchange uses public identity
- **WHEN** a supported expression using total is converted to a filter tree and back using a public schema
- **THEN** the exchanged field remains total and uses the registered result type and permissions
- **AND** no internal selector or canonical member path is exported

### Requirement: Diagnostics and metadata-only purity
Unknown public names SHALL produce unknown-property even when a matching internal entity member exists. Known filter-denied or sort-denied registrations SHALL reuse property-not-queryable with an actionable operation-specific message. Policy/field operator denial SHALL use query-policy-operator-denied, incompatible operators SHALL use operator-not-applicable, and incompatible values SHALL reuse incompatible-operand or the applicable existing structured conversion diagnostic.

Diagnostics SHALL retain existing source spans, structured input paths/JSON Pointers, diagnostic bounds, truncation, and failure-output conventions. Denials and unknown-field suggestions SHALL not disclose internal paths, expression text, captured values, or denied-field types. Try/evaluation APIs SHALL return no usable result on failure; conditions SHALL not retain partial predicates or keys; throwing APIs SHALL carry the repository-standard structured failure information. Static invalid inputs SHALL fail before resolver initialization/resolution, and cancellation SHALL remain cancellation.

Registration validation, schema/context binding, metadata inspection, semantic analysis, tree validation, expression/tree exchange, and recovery SHALL not call selectors, getters, variable resolvers, user conversion/operator methods, database queries, external services, or enumerate runtime collections. Application-provided trees SHALL be trusted configuration, not executable metadata.

#### Scenario: Locations and nondisclosure
- **WHEN** an unknown name, a filter-denied field, and a sort-denied field are supplied through text, flat filters, tree conditions, or structured ordering
- **THEN** each receives the existing appropriate location form and code with its external spelling and operation
- **AND** failed results are null and internal mappings are absent from messages and suggestions

#### Scenario: Instrumented metadata purity
- **WHEN** selectors contain throwing getters/methods or captured runtime objects and analysis processes valid, denied, incompatible, and incomplete inputs
- **THEN** getter, method, resolver, conversion, enumeration, database, and external-service counters remain zero
- **AND** execution controls confirm the probes can observe actual application execution

### Requirement: Stable public metadata
Public schemas SHALL expose a stable read-only field view in registration order, with registered name, CLR result type, nullability, filter/sort permission, declared allowed operators, optional display name, and description. Schema-bound context metadata SHALL expose effective permissions/operators after policy and public-name restrictions. Field/operator collections SHALL be copied into immutable snapshots; later builder/options/collection mutation SHALL not change an existing schema or context.

The default public field view SHALL omit selectors, canonical/internal member paths, captured objects, entity-member enumeration, and provider-translation claims. It SHALL contain enough information to drive semantic validation and application-owned documentation or future autocomplete without interpreting internal mappings.

#### Scenario: Immutable metadata and snapshot reuse
- **WHEN** a schema/context is built and the builder, operator collection, or input metadata is subsequently changed
- **THEN** the existing schema/context retains its original ordered names, permissions, and operator sets
- **AND** metadata inspection reveals no internal selector details or entity-only members

### Requirement: Relational compatibility documentation and legacy preservation
The library SHALL keep legacy APIs and reflected/string-mapped behavior unchanged unless callers explicitly select public-schema mode. Core SHALL remain independent of EF Core and SHALL not implement provider-specific SQL rewriting or claim arbitrary selectors translate.

Tests SHALL execute representative nested, computed numeric, nullable, filter, and sort mappings through the existing SQL Server and MySQL infrastructure and compare results with equivalent handwritten LINQ plus explicit expected record IDs/order. Registration validity, semantic validity, and provider translation/execution SHALL be reported as distinct stages. A valid C# scalar selector calling an unmapped application method SHALL be documented and verified as a translation limitation, without client-evaluation fallback.

Implementation SHALL add a dedicated Markdown guide with complete C# registration/computed/nullable examples, metadata, independent permissions, query-policy interaction, conflict rules, legacy compatibility, migration from string mappings, and SQL limitations. README SHALL contain a concise quick start and resolving relative guide link. Documentation examples SHALL compile against implemented APIs and links SHALL be checked. Existing unit and relational suites SHALL continue to pass.

#### Scenario: Relational results are not a translation proxy
- **WHEN** nested customerName, computed total, and supported nullable mappings are filtered/sorted on SQL Server and MySQL
- **THEN** generated and handwritten queries execute on each database and match explicitly asserted expected IDs and ordering
- **AND** IQueryable expression construction, registration success, or semantic success alone is not accepted as relational verification

#### Scenario: Nontranslatable but valid selector
- **WHEN** a registered scalar expression calls an application method without provider translation support
- **THEN** registration and applicable semantic checks succeed without calling it
- **AND** provider execution fails explicitly at translation rather than silently compiling/evaluating the mapping locally

#### Scenario: Legacy regression and guide verification
- **WHEN** existing callers run without public-schema mode and the new guide examples and links are checked
- **THEN** legacy behavior and overload resolution remain unchanged, examples compile, and README links to the dedicated guide
- **AND** the guide distinguishes C# compile-time member checking from registration, semantic, and provider compatibility
