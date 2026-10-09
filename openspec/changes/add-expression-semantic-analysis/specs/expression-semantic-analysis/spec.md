# Spec Delta

## Purpose

Enable editors to validate predicate expressions against entity and variable metadata without evaluating application code, while providing precise, deterministic diagnostics during incremental editing.

## ADDED Requirements

### Requirement: Additive metadata-aware analysis
The library SHALL expose synchronous generic and runtime-entity-type semantic analysis accepting expression text, an entity schema, and optional declared variable metadata. It SHALL return immutable tokens, unchanged syntax diagnostics, semantic diagnostics, and a source-ordered combined diagnostic view. `IsComplete` SHALL retain syntax-only meaning; `IsSemanticallyValid` SHALL require complete syntax, no semantic errors, and a Boolean predicate result. The existing `AnalyzeExpression(string)` signature, result types, classifications, diagnostics, and completeness SHALL remain unchanged. Null expression/schema/type arguments and inconsistent metadata SHALL produce explicit argument errors; incomplete user expression text SHALL return diagnostics rather than throw.

#### Scenario: Syntax-only compatibility
- **WHEN** `MissingProperty = $missing` is passed to the existing syntax-only operation
- **THEN** syntax is complete and no diagnostic is produced, regardless of semantic-analysis availability

#### Scenario: Typed analysis rejects a nonpredicate
- **WHEN** `1 + 2` is analyzed against an entity schema
- **THEN** syntax completeness is true, semantic validity is false, and `predicate-result-not-boolean` covers `(0, 5)` with expected Boolean and actual Int32 type information

#### Scenario: Equivalent entry points
- **WHEN** the same expression, schema, and variable declarations are passed through generic and runtime-type semantic overloads
- **THEN** both produce identical tokens, validity flags, codes, spans, type information, and suggestions

### Requirement: Entity schema and query permissions
Schema metadata SHALL describe canonical property/field paths, CLR types, nullability, query permission, and external-name mappings. Schema construction SHALL reuse readable public instance CLR member metadata without requiring callers to repeat every property. Property path and mapping lookup SHALL follow existing case-insensitive, exact full-path mapping conventions; aliases SHALL NOT implicitly rewrite path prefixes. Nullability of reference members not explicitly described SHALL be unknown rather than inferred as non-null. Empty or omitted legacy-style allowlists SHALL retain unrestricted-member semantics; an explicit member permission denial SHALL remain effective through every alias. Unknown paths SHALL produce `unknown-property`; existing but disallowed paths SHALL produce `property-not-queryable`. Neither diagnostic SHALL be conflated with runtime variable availability.

#### Scenario: Unknown and restricted are distinct
- **WHEN** `UnknownField = 1 and InternalCost = 2` is analyzed for an entity without `UnknownField` but with denied `InternalCost`
- **THEN** diagnostics contain `unknown-property` and `property-not-queryable` at their respective source paths
- **AND** the restricted diagnostic does not disclose mapped internal names or type details

#### Scenario: Nested member lookup
- **WHEN** `Customer.Address.City = 'Paris'` and `Customer.Address.Missing = 1` are analyzed against reflected nested entity metadata
- **THEN** the first is semantically valid when permitted and the second reports `unknown-property` covering the full invalid path
- **AND** fields as well as readable properties are handled without reading their values

#### Scenario: Mapping and source-name permissions
- **WHEN** `cost` maps exactly to `Price`, an allowlist contains only `cost`, and `Price` exists
- **THEN** `cost > 1` is permitted and `Price > 1` is rejected as `property-not-queryable`
- **AND** an explicit denial of canonical `Price` also denies `cost`

#### Scenario: Selective metadata overrides
- **WHEN** schema overrides describe the nullability or query permission of selected reflected members
- **THEN** all other members retain reflected metadata and default query behavior
- **AND** contradictory CLR types, duplicate case-insensitive declarations, or mappings to nonexistent members are rejected explicitly

### Requirement: Shared operand and operator semantics
Semantic analysis SHALL apply the same contextual literal conversion, operand ordering, type promotion, null lifting, operator applicability, membership, arithmetic, and Boolean-result rules as runtime predicate construction when those rules are decidable from metadata and literal text. It SHALL NOT introduce conversions or operators simply because C# could support them. A literal that cannot convert to its contextual target SHALL produce `incompatible-operand`, with expected contextual type and actual source type. An operator with known incompatible receiver/operand types SHALL produce `operator-not-applicable` at the operator. Failure of a child operand SHALL suppress dependent operator/result diagnostics. Metadata-only variable conversion checks SHALL distinguish a supported conversion requiring a future value from a provably unsupported conversion.

#### Scenario: Independent comparison failures
- **WHEN** `Price > 'abc' and UnknownField = 1` is analyzed with decimal `Price`
- **THEN** exactly two semantic diagnostics are returned: `incompatible-operand` at `(8, 5)` with expected Decimal and actual String, and `unknown-property` at `(18, 12)`
- **AND** no fabricated numeric replacement or dependent operator error is returned

#### Scenario: Numeric receiver cannot use string contains
- **WHEN** `Price.contains('text')` is analyzed with decimal `Price`
- **THEN** `operator-not-applicable` covers `(6, 8)` with an expected String receiver and actual Decimal receiver
- **AND** the analyzer does not suggest changing the user's operator or numeric value

#### Scenario: Valid contextual conversions and nullability
- **WHEN** quoted numeric, Guid, enum, Boolean, full date/time, or TimeSpan literals compatible with their entity operands are analyzed
- **THEN** they are accepted under the applicable runtime conversion rules and supplied conversion context
- **AND** nullable comparisons, null membership elements, and lifted arithmetic follow existing construction behavior rather than a newly imposed non-null policy

#### Scenario: Existing arithmetic rules
- **WHEN** numeric promotion, nullable arithmetic, mixed string concatenation, decimal with double, or unsigned with incompatible signed operands are analyzed
- **THEN** semantic decisions agree with runtime construction for those expressions
- **AND** analysis does not evaluate string conversion methods or compiled arithmetic

#### Scenario: Membership diagnoses individual elements
- **WHEN** `Price in [1, 'abc', $missing]` is analyzed without a declaration for `missing`
- **THEN** the invalid literal and undeclared variable receive their own positioned diagnostics
- **AND** valid elements remain useful and no cascading collection/operator diagnostic is added

#### Scenario: Value-dependent conversion is not a missing variable
- **WHEN** a declared String variable is used where the runtime supports conversion to a numeric field
- **THEN** analysis checks conversion support without obtaining or predicting the runtime string
- **AND** no undeclared-variable or invalid-value diagnostic is invented merely because the value is unavailable

### Requirement: Declared variable and member metadata
Variable metadata SHALL describe declared names, CLR types, nullability, and member paths without runtime values. It SHALL support reflected member declarations from a CLR type and explicit member declarations, including collection element types. Exact dotted declarations SHALL take precedence over traversal, consistent with direct runtime registrations. Explicit `$` references SHALL bind only to declared variables, never entity properties. An unknown variable SHALL produce `undeclared-variable`; an unknown member below a declared typed root SHALL produce `unknown-variable-member`. Bare identifiers SHALL bind to a permitted entity member first and to an explicitly declared bare variable only when no entity member exists. A denied entity member SHALL NOT be rescued by variable fallback. Analysis SHALL NOT assume built-in or custom resolver variables are declared unless included in metadata.

#### Scenario: Missing root versus missing member
- **WHEN** `$user.department = 'Sales'` is analyzed first without `user`, then with `user` declared but without `department`
- **THEN** the first result contains `undeclared-variable` and the second contains `unknown-variable-member`
- **AND** each diagnostic covers the full variable token, including `$`

#### Scenario: Declared value need not be available
- **WHEN** `$user.department = 'Sales'` is analyzed with a declared String member and no runtime value
- **THEN** the reference is valid and no runtime-availability diagnostic occurs

#### Scenario: Direct dotted registration and bare fallback
- **WHEN** `user.department` is declared directly and `threshold` is a declared numeric variable without a conflicting entity member
- **THEN** `$user.department = 'Sales'` and `Price > threshold` bind using declarations
- **AND** `$user.unknown` does not inherit invented member declarations from the direct dotted name

### Requirement: Metadata-only purity
Semantic analysis and metadata construction SHALL NOT execute variable resolvers, initialization hooks, entity or variable getters, compiled expressions, user-defined conversions/operators, collection enumeration of runtime values, database queries, or external service calls. Literal inspection SHALL be limited to built-in, side-effect-free operations on supplied source text. Analysis SHALL NOT invoke runtime predicate construction or node building. Reflection SHALL read only member/type descriptors. The same guarantees SHALL hold for malformed input and recovery paths.

#### Scenario: Throwing resolver proves noninvocation
- **WHEN** a throwing/counting custom resolver is installed alongside the runtime test fixture and a declared variable is semantically analyzed
- **THEN** its resolution and initialization counters remain zero and no resolver exception is observed
- **AND** a runtime parse control using that fixture demonstrates the resolver instrumentation is effective

#### Scenario: Getters and custom methods are never evaluated
- **WHEN** schema/member types have getters, conversion methods, enumeration methods, or operators that throw or record calls
- **THEN** valid and malformed semantic analysis does not invoke them
- **AND** unsupported checks are explicit rather than delegated to application code

### Requirement: Stable structured diagnostics and safe corrections
Semantic diagnostics SHALL expose stable documented codes, nonempty actionable messages, zero-based UTF-16 `Start`/`Length` into unmodified input, structured expected/actual type collections, and immutable correction suggestions. Type descriptors SHALL distinguish CLR types, null literals, unknown types, nullability, and collection element types. Unknown or permission-denied operands SHALL NOT pretend to have a known actual type. Codes SHALL include `unknown-property`, `property-not-queryable`, `incompatible-operand`, `operator-not-applicable`, `undeclared-variable`, `unknown-variable-member`, `predicate-result-not-boolean`, and `context-dependent-conversion`. Combined diagnostics SHALL retain syntax codes and be ordered by Start, Length, and ordinal Code, with duplicates of a code/span suppressed.

Corrections SHALL specify a replacement text and the original-input replacement span. Property suggestions SHALL use only permitted public spellings, never restricted canonical names, denied aliases, or invalid mappings. A suggestion SHALL be returned only for a unique conservative close match within the same path scope; ambiguous or speculative value/operator changes SHALL yield no suggestion. Permission-denied diagnostics SHALL contain no correction suggestion. Applying a suggested property correction SHALL resolve the referenced property without bypassing permission checks.

#### Scenario: Whitespace and UTF-16 spans remain exact
- **WHEN** semantic errors occur after leading whitespace, a supplementary Unicode character in quoted text, or spaced dotted paths
- **THEN** slicing the original input by every diagnostic and suggestion span identifies exactly the reported source text
- **AND** source text is never trimmed before offset calculation

#### Scenario: Permission-safe unique typo
- **WHEN** `Pric = 1` is analyzed and `Price` is its sole permitted close sibling while `PrivateCost` is denied
- **THEN** `unknown-property` at `(0, 4)` contains a replacement suggestion `Price` at `(0, 4)`
- **AND** no restricted member appears in diagnostic messages, type details, or suggestions

#### Scenario: Ambiguous and restricted corrections are absent
- **WHEN** two permitted names tie as close matches or the only close match is restricted
- **THEN** no correction is supplied
- **AND** an alias of a denied canonical member cannot appear as a suggestion

### Requirement: Recovery during incremental editing
Semantic analysis SHALL preserve all existing syntax tokens and diagnostics, retain stable semantic findings on complete independent clauses, and avoid diagnostics requiring missing, malformed, or unbound operands. An unfinished identifier SHALL NOT be reported as an unknown path; a missing operand SHALL NOT be treated as an operand of null or unknown type. Unresolved or erroneous children SHALL suppress parent type/operator/result errors, without suppressing independently checkable siblings. Blank input SHALL return no diagnostics with both validity flags false. Recovery SHALL make forward progress and never reinterpret quoted text as a synchronization boundary.

#### Scenario: Trailing missing operand suppresses cascades
- **WHEN** `Price > ` is analyzed
- **THEN** the original missing-operand syntax diagnostic remains at `(8, 0)`
- **AND** no incompatible-operand, operator-not-applicable, or predicate-result-not-boolean diagnostic is added

#### Scenario: Partial path remains an editing state
- **WHEN** `Customer.` or `$user.` is analyzed
- **THEN** existing incomplete-identifier diagnostics remain
- **AND** no unknown-property, undeclared-variable, or unknown-variable-member diagnostic is derived from the unfinished token

#### Scenario: Valid independent suffix survives syntax recovery
- **WHEN** `Price = ) and UnknownField = 1` is analyzed
- **THEN** the original syntax error is retained and the complete suffix reports `unknown-property`
- **AND** the broken comparison produces no cascading semantic diagnostic

#### Scenario: Unknown child does not poison sibling diagnostics
- **WHEN** `UnknownField + 1 = 2 and Price > 'abc'` is analyzed
- **THEN** both the unknown field and incompatible literal are diagnosed
- **AND** no arithmetic, comparison, or root Boolean cascade is emitted from the unknown field

### Requirement: Deterministic conversion context and analysis boundary
Schema SHALL carry an immutable conversion context containing an explicit read-only culture and date-format snapshot. Numeric language literals SHALL remain invariant-culture. Semantic conversion checks SHALL NOT depend on ambient culture, mutable global date formats, the clock, or local time zone. Temporal literals whose conversion needs the current date or local time zone SHALL produce `context-dependent-conversion`, not an assertion that runtime conversion necessarily fails; messages SHALL recommend an explicit date and, when needed, offset. Runtime conversion defaults SHALL remain unchanged. Semantic validity SHALL NOT guarantee SQL translation, runtime values, value-dependent conversion success, runtime null traversal, or execution without overflow or application exceptions.

#### Scenario: Culture and format snapshots are deterministic
- **WHEN** the same schema and expression are analyzed before and after changes to current culture or the global date-format list
- **THEN** codes, spans, type information, suggestions, and validity flags are identical
- **AND** supplied culture-sensitive date conversions are tested against runtime construction under the matching context

#### Scenario: Temporal defaults are explicitly diagnosed
- **WHEN** a time-only date literal or an offset-less DateTimeOffset literal requires machine date/time-zone defaults
- **THEN** `context-dependent-conversion` identifies the literal with structured target/source types
- **AND** the diagnostic explains the explicit-date/offset correction without fabricating a date

### Requirement: Documentation and compatibility verification
The feature SHALL include a dedicated Markdown guide and a concise README introduction/quick start linking to it with a relative path. The guide SHALL document complete C# entity schema, query restriction, mapping, variable declaration, and editor diagnostic-consumption examples; all public API/result/type/suggestion shapes; every stable semantic code and the existing syntax-code boundary; source spans including zero-length positions; nullability, supported conversions, incomplete-input recovery, and suppressed cascades. It SHALL explicitly distinguish syntax analysis, semantic analysis, and runtime predicate construction, explain conversion-context differences, and state no resolver/external-call execution and no EF Core SQL translation guarantee. Completion SHALL require compile-checked examples, resolved documentation links, and passing existing syntax-analysis and predicate-construction regression tests.

#### Scenario: Editor integration guide is executable
- **WHEN** guide and README examples are compiled against the implemented public API
- **THEN** they compile without placeholder APIs and demonstrate inspecting combined diagnostic codes, spans, expected/actual types, and safe suggestions
- **AND** every local guide/README link resolves

#### Scenario: Runtime compatibility remains verified
- **WHEN** the existing syntax, predicate, variable, mapping, conversion, arithmetic, filter, condition, and relational regression suites run after implementation
- **THEN** existing runtime outcomes and legacy diagnostics remain unchanged and the core library still builds for its existing target
