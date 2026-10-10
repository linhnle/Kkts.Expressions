# Tasks

## 1. Extend the existing schema with typed public registrations

- [x] 1.1 Add `QuerySchema<T>.Field<TResult>` and `Build()` returning the existing `ExpressionSchema`, explicit public-mode state, and internal registered-field descriptors; verify direct/nested/computed inferred types, empty-public-schema state, unchanged `FromType` behavior, and a core `netstandard2.0` build.
- [x] 1.2 Validate names through existing grammar conventions and validate supported selector nodes/types/parameters before mutation; add tests for null/blank/reserved/dotted/whitespace names, case-insensitive duplicates, mismatched/programmatically invalid trees, unsupported invocation/lambda/block/assignment/dynamic/extension/construction/index shapes, and nonscalar results; verify each rejection identifies the field/reason and leaves prior registrations unchanged.
- [x] 1.3 Add optional permissions/operators/display name/description/nullability and immutable registration snapshots; verify defaults, empty operator sets, invalid enum/nullable overrides, builder/operator-input mutation, registration order, and reuse of existing conversion type metadata.
- [x] 1.4 Add central public-mode configuration conflict guards without introducing combined runtime schema-plus-map overloads; verify nonempty legacy maps/canonical overrides fail through the schema invariant and legacy construction still accepts its existing valid inputs.
- [x] 1.5 Start `docs/expression-field-mapping.md` with complete typed registration/metadata examples, supported expression subset, defaults, explicit public-mode activation through `Build()`, and configuration errors; verify registration examples compile in the existing documentation-example test pattern.

## 2. Add shared field resolution and expression composition

- [x] 2.1 Implement shared schema resolution with public registered identity versus legacy canonical-path identity; verify case-insensitive exact public lookup, no reflected/member-prefix/suffix fallback, empty schema denial, and all existing schema/mapping tests.
- [x] 2.2 Implement root-parameter identity rebinding on resolved descriptors, retaining the existing legacy member builder; verify combined selectors with different/same parameter names have only the operation parameter, no Invocation nodes, and unchanged MethodInfo/conversion/lifting/checked/conditional semantics.
- [x] 2.3 Add scalar-computation tests for Decimal times Int32, explicit Int64 conversion, nullable multiplication/coalesce, Boolean computations, and captured/static scalar method calls; verify operand/key CLR types and handwritten in-memory equivalence without executing application code during composition.
- [x] 2.4 Add explicit guarded nullable-navigation and unguarded-path tests; verify null results/nullable comparisons for guarded expressions and ordinary C# null-reference execution for unguarded expressions, with zero getter calls during registration/composition.
- [x] 2.5 Extend the guide with computed fields, null guards, conversions, live captured-state behavior, and the distinction between trusted selector arithmetic and supported query-language syntax; verify examples compile and generated trees match their documented result types.

## 3. Bind permissions and semantic analysis to public field metadata

- [x] 3.1 Add context filter/sort admission and public-name allowed-operator binding, preserving legacy canonical alias/ancestor rules; verify filter-only, sort-only, deny-all, omitted/empty/intersected operator sets, normalized aliases, and independent registrations of the same internal member.
- [x] 3.2 Validate/snapshot additional public-name allowlists and policy keys; verify unknown/internal names and entity mismatches fail configuration, valid allowlists only narrow access, empty allowlists cannot disable public visibility, and mutation cannot change an existing context.
- [x] 3.3 Apply the trusted opaque-mapping rule to navigation/collection admission without inspecting selector bodies; verify nested/Items.Count public projections remain admissible under depth zero/collection denial while existing legacy alias depth/collection tests retain their outcomes.
- [x] 3.4 Rewire semantic binding and suggestions to the shared descriptor and disable bare-variable fallback only in public mode; verify unknown-property, operation-specific property-not-queryable, query-policy-operator-denied, operator-not-applicable, incompatible-operand, explicit `$` references, registered comparison functions, and denied suffix paths.
- [x] 3.5 Add semantic purity and budget tests covering throwing getters/methods, captures, variable metadata, incomplete/oversized queries, and computed Boolean fields; verify zero application execution, retained source spans/truncation, and condition counts based on user input rather than selector internals.
- [x] 3.6 Document filter/sort independence, operator intersections, public versus legacy identity, navigation/collection trust boundaries, conflict/narrowing rules, and explicit variables in the guide; update `docs/query-policies.md` and `docs/semantic-analysis.md` with scoped public-mode exceptions and relative cross-references; verify the examples and links.

## 4. Wire all predicate and condition construction paths

- [x] 4.1 Route `BuildArgument`, parser/property classification, property AST building, and direct field/operator/value construction through schema descriptors; verify generic/runtime-type sync/async string/direct predicates compose registered selectors and unknown/denied bare names never initialize/resolve variables.
- [x] 4.2 Rewire flat `Filter`, sequences, `FilterGroup`, group sequences, and both async filter builders to shared composition/types; verify AND/OR behavior, value conversion, membership, permissions, failure diagnostics, and handwritten equivalence across applicable context operations.
- [x] 4.3 Rewire nested-tree semantic validation, policy scanning, and sync/async construction without converting selectors to member paths or expression text; verify public result types, nullable/membership behavior, normalized operators, JSON Pointer diagnostics, and denied-field failures before resolution.
- [x] 4.4 Integrate mapped fields into context `ConditionOptions` processing and all Where/Filters/FilterGroups/FilterTree combinations; verify shared budgets/parameters, no partial predicates on failure, context entity matching, async cancellation, and legacy condition behavior.
- [x] 4.5 Add a cross-surface predicate diagnostic/permission matrix for permitted, unknown/internal, denied, incompatible-operator/value, suffix-path, and variable-fallback inputs; verify existing text/flat/tree location forms, null failed Result, throwing diagnostics, and no internal expression/path/captured-value disclosures.
- [x] 4.6 Extend the guide with complete string/direct/flat/group/tree/condition examples using the same context and metadata, checking failures before consumption; update `docs/nested-filters.md` with the public-field boundary; verify example compilation and expected in-memory IDs.

## 5. Carry mapped selectors through ordering

- [x] 5.1 Extend `OrderByParser`/`OrderByClause` internal key representation to retain resolved field descriptors, preserve legacy helpers, and validate sort permission for all keys; verify string and `OrderByInfo` inputs, filter-only rejection, sort-only acceptance, unknown-field locations, and no clause on a later denied key.
- [x] 5.2 Build typed rebound Queryable OrderBy/ThenBy keys for ascending/descending and generic/runtime-type source APIs; verify exact TKey/quoted lambdas, no invocation/boxing/compilation/client fallback, computed/nested/nullable ordering, and explicit expected multi-key order.
- [x] 5.3 Preserve Enumerable ordering with compilation only of final composed keys when applying a clause; verify clause construction executes no application code, sorting matches handwritten LINQ, no premature getter/method execution occurs before enumeration, and legacy ordering tests remain green.
- [x] 5.4 Verify context conditions retain mapped ordering and discard every partial key/predicate when ordering fails; run condition and query-policy ordering tests covering independent sort permission, length budgets, and operator sets that deny filtering but permit sorting.
- [x] 5.5 Document permitted mapped multi-key sorting, expensive filter-only computations, the Enumerable execution-only compilation exception, and compile-free IQueryable composition; verify complete examples compile and their expected ordering is asserted.

## 6. Publish stable metadata and preserve expression/tree exchange

- [x] 6.1 Expose read-only schema field metadata and context-effective permissions/operators without selectors/internal paths/captures; verify registration order, immutable copies, declared versus effective sets, labels/descriptions/nullability, and no entity-only fields or provider-translation claims.
- [x] 6.2 Use shared registered field metadata in the existing metadata-only expression/tree exchange subset; verify computed field names/types survive supported round trips, denied/unknown fields are rejected, and internal mapping expressions are never serialized.
- [x] 6.3 Add end-to-end instrumented purity tests for registration, binding, metadata enumeration, semantic/tree validation/exchange, construction, and error recovery; verify zero selectors/getters/resolvers/conversions/enumerations/database/external calls and include execution controls proving instrumentation works.
- [x] 6.4 Finish the guide's metadata and application-owned API-documentation examples, legacy compatibility/migration steps, and exact-name projection guidance; add a concise README quick start/contents entry with a relative guide link; verify public API example compilation and all directly related local links.

## 7. Verify representative SQL Server and MySQL execution

- [x] 7.1 Add shared mapped-field relational cases using existing Parent/DecimalValue/Integer/nullable/date fixtures; execute generated and handwritten string/structured/tree predicates on both providers and assert explicit expected IDs: customerName North = 1/2/5, total > 8 = 3/4/5, optionalTotal null = 2/4, optionalTotal > 8 = 3/5.
- [x] 7.2 Execute mapped Queryable ordering and filter-plus-sort cases on both providers, including descending totals/date keys and customerName with an explicitly registered Id tie-breaker; assert handwritten parity and exact expected order, and test filter-only sort denial separately before database use.
- [x] 7.3 Extend the existing relational model/seed with an optional relationship for guarded nullable-navigation selectors without changing existing five-record expectations; verify null/present navigation filtering and deterministic null-key ordering against handwritten LINQ on SQL Server and MySQL.
- [x] 7.4 Add a valid C# scalar application-method selector with no provider mapping; verify registration/semantic validity separately, require explicit filter/order translation failure on each provider, and assert no application method execution or client-evaluation fallback.
- [x] 7.5 Document the tested relational subset, optional-navigation behavior, provider/version limitations, and a complete nontranslatable-method example in the dedicated guide; verify examples match the executed tests and clearly distinguish compile-time, registration, semantic, and SQL-translation validity.

## 8. Complete integration and compatibility verification

- [x] 8.1 Verify documented positive selectors compile and temporary negative application snippets fail for removed/renamed members and incompatible C# operands with expected compiler diagnostics; retain no new compiler package and clean up only the specific temporary project artifacts.
- [x] 8.2 Run the relevant schema/semantic/policy/filter/tree/condition/ordering/documentation tests together, then the existing complete unit and both-provider relational suites; verify legacy mappings, allowlists, canonical denials, bare variables, temporal conversions, exceptions, and overload resolution remain unchanged outside public mode.
- [x] 8.3 Build core for its existing target framework and check dependencies/documentation links/example coverage; verify no EF Core/provider dependency, no unresolved local links, and coverage of every acceptance scenario and operation-specific permission.
- [x] 8.4 Reconcile this change's capability references against the still-active semantic-analysis/nested-tree deltas before eventual spec synchronization; verify no public-mode exception is lost and do not modify/archive unrelated change artifacts without explicit authorization.
- [x] 8.5 Record actual unit/build/provider verification outcomes and remaining environmental blockers in the implementation handoff; verify absent database execution is reported as blocked rather than replaced by construction/SQL-text/in-memory success, and mark this checklist complete only after required verification succeeds.
