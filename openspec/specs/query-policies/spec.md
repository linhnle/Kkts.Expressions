# query-policies Specification

## Purpose

Allow applications to configure reusable limits and field permissions for user-supplied queries, with consistent metadata-only editor diagnostics and independent server-side enforcement across existing query input forms.

## Requirements

### Requirement: Explicit reusable policy configuration
The library SHALL expose immutable, reusable policy configuration for maximum expression length, parenthesis nesting depth, atomic condition count, membership item count, entity navigation depth, collection access permission, and allowed comparison operators per field. Policy configuration SHALL be bound to existing entity schema metadata and SHALL NOT introduce new expression syntax. Omitted numeric limits SHALL mean unlimited, omitted field operator rules SHALL mean all existing applicable operators, and collection access SHALL default to allowed. An explicitly empty operator set SHALL deny comparison use of that field. Limits SHALL accept zero and positive integers; negative limits, undefined operator enum values, duplicate case-insensitive field declarations, null declarations, and unknown configured field paths SHALL produce explicit configuration argument errors, not user-input diagnostics. Binding SHALL snapshot mutable configuration and SHALL NOT call application getters or resolvers.

The library SHALL offer an explicit recommended preset: expression length 4096 UTF-16 units, parenthesis depth 16, atomic conditions 64, membership items 100, navigation depth 3, and collection access denied. The preset SHALL NOT silently restrict unspecified field operators or become the default for existing operations.

#### Scenario: Defaults and explicit opt-in
- **WHEN** an application uses an existing entry point without a policy
- **THEN** its documented behavior, overload resolution, conversions, diagnostics, and exceptions remain unchanged
- **AND** the recommended limits apply only after explicit selection

#### Scenario: Invalid and immutable configuration
- **WHEN** a policy is configured with a negative limit or invalid field/operator declaration
- **THEN** configuration fails explicitly before processing a user query
- **AND** later mutation of collections used to construct a valid policy does not change its behavior

#### Scenario: Zero limits and empty permissions
- **WHEN** a policy has zero conditions, zero navigation depth, or an empty operator set for a field
- **THEN** every atomic predicate is denied by the first setting, direct scalar fields remain usable under the second, and comparison use of the named field is denied by the third

### Requirement: Original-source length budget
Policy-aware string processing SHALL compare the exact untrimmed .NET input string length with the configured maximum before tokenization, trimming, parser creation, or variable resolution. Length SHALL be measured in UTF-16 units, including whitespace, quotes, escapes, and both units of a supplementary Unicode character. At the limit input SHALL be permitted to proceed; beyond it processing SHALL fail with `query-policy-expression-length-exceeded` and no usable predicate.

For structured filters, the length budget SHALL be the cumulative UTF-16 lengths of the supplied Property, Operator, and Value strings across all filter leaves in the operation, with null values contributing zero and no synthetic delimiters or JSON escaping. A condition operation SHALL include its Where text and all structured filter leaves in that one filter budget. Ordering text SHALL have a separate expression-length budget; structured order entries SHALL sum their supplied property names. The implementation SHALL stop accumulating when the budget is exceeded, before materializing the entire input.

Direct property/operator/value APIs SHALL count supplied property text, the operator's canonical spelling, and a supplied string value for their text budget. They SHALL NOT call application object ToString methods to invent a text representation; nonstring values SHALL instead receive applicable runtime type and membership checks.

#### Scenario: Exact source length boundary
- **WHEN** an otherwise valid expression has untrimmed UTF-16 length L under a maximum L, then one additional space is appended
- **THEN** the first proceeds and the second fails before lexing with configured limit L and exact observed length L + 1

#### Scenario: Supplementary characters and structured accounting
- **WHEN** quoted text contains a supplementary character or structured membership values contain duplicate strings
- **THEN** the character consumes two length units and every supplied duplicate contributes its original string length
- **AND** no deduplication or generated representation changes accounting

### Requirement: Parenthesis and processing depth budgets
Parenthesis depth SHALL count simultaneously open `(` delimiters outside quoted strings, including grouping, NOT calls, comparison-function calls, and parenthesized membership lists. Quotes and escapes SHALL follow the existing grammar; brackets and braces SHALL NOT increment the parenthesis counter. An unterminated quoted string SHALL retain quoted status to end of input. The first opening parenthesis producing depth greater than the limit SHALL cause `query-policy-parenthesis-depth-exceeded` before deeper parser work or allocation.

Existing structured group containers SHALL consume one group-depth level, with a standalone Filter and a flat Filter sequence at depth zero and a FilterGroup at depth one. No recursive filter DTO SHALL be added. Textual membership fragments SHALL also enforce parenthesis depth where their existing syntax uses parentheses. Policy-aware processing SHALL avoid unbounded call-stack growth from repeated NOT, long arithmetic/logical chains, malformed delimiters, or schema paths; condition counting SHALL NOT incorrectly charge these operators as predicates.

#### Scenario: Parentheses and quotes
- **WHEN** depth limit 2 is applied to `((Id = 1))`, `(((Id = 1)))`, and `Name = '((()))'`
- **THEN** the first meets the boundary, the second fails at the third opening parenthesis with observed depth 3, and the quoted parentheses contribute zero depth

#### Scenario: Incomplete input
- **WHEN** an incomplete expression crosses the depth limit before its closing delimiters are supplied
- **THEN** the offending opening delimiter receives the policy diagnostic without requiring complete syntax
- **AND** malformed input cannot trigger unbounded recursive recovery

#### Scenario: Structured group depth
- **WHEN** a depth-zero policy is used for a flat Filter sequence and then for a FilterGroup containing the same leaves
- **THEN** the flat sequence can pass and the group fails at its structured group location
- **AND** existing AND/OR composition is not rewritten to evade the group restriction

### Requirement: Atomic predicate counting
Condition counting SHALL count one for each comparison, membership test, supported Boolean comparison-function call, and standalone Boolean-valued operand used as a predicate. AND, OR, NOT, parentheses, arithmetic nodes, and membership elements SHALL NOT add conditions. Negating a predicate SHALL preserve its count and its underlying comparison operator permission; logical negation SHALL NOT be normalized to a different field comparison. Every field contributing to a computed comparison operand SHALL be checked against that comparison's normalized operator. A standalone Boolean field SHALL use Equal permission as its implicit comparison identity.

An incomplete comparison SHALL reserve one condition once its operator is recognized. A bare identifier SHALL count as a predicate only when complete in Boolean position, not twice after a following comparison is recognized. Missing operands SHALL not be invented as conditions. Structured filters SHALL count one per Filter leaf, including incomplete leaves; groups SHALL sum their leaves. A condition operation SHALL share one count across Where, Filters, and FilterGroups rather than resetting the budget at each surface. Exceeding the limit SHALL produce `query-policy-condition-count-exceeded` before constructing the additional predicate.

#### Scenario: Logical composition and NOT
- **WHEN** limit 2 is applied to `not(Id = 1 or Id = 2)` and then `not(Id = 1 or Id = 2) and IsEnabled`
- **THEN** the first has two conditions and the second fails on the third condition with observed count 3

#### Scenario: Arithmetic and field operator checks
- **WHEN** `Price + Discount > 10` is processed
- **THEN** it counts as one condition and GreaterThan permission is required for both Price and Discount
- **AND** arithmetic is not counted as another condition or introduced as a new policy operator

#### Scenario: Incomplete comparison and aggregate inputs
- **WHEN** one permitted condition is followed by `and Id =` under limit 1, or separate condition input surfaces together contain two filter leaves
- **THEN** the recognized incomplete comparison or second aggregate leaf exceeds the budget
- **AND** incomplete syntax does not bypass the condition budget

### Requirement: Membership sizes and bounded runtime resolution
IN and NOT IN SHALL enforce the same maximum items per collection. Items SHALL be counted before deduplication, conversion, or expression construction; duplicate values, nulls, and each scalar variable slot SHALL each count as one. Empty collections SHALL count zero. All currently supported list delimiters and structured comma-separated membership values SHALL use the existing item grammar, including quoted commas and escapes, rather than a naive string split. A started but unfinished membership element SHALL consume an item slot without being treated as a valid literal; trailing separators alone SHALL NOT fabricate an item.

A collection-valued variable SHALL be checked after runtime resolution and before constructing its collection expression. When enumeration is needed, processing SHALL consume at most limit plus one items, stop immediately on excess, dispose the enumerator, preserve cancellation, and retain only a bounded snapshot of accepted values. Accepted sequences SHALL be used from that snapshot, not re-enumerated during predicate execution. Failure SHALL produce `query-policy-in-items-exceeded` with observed count limit plus one marked as a lower bound unless the exact count is known without further enumeration. No unbounded Count, ToArray, or deduplication SHALL precede the check.

Metadata-only analysis SHALL check literal collection slots and declared element types without resolving, enumerating, or guessing variable-backed cardinality. Unknown cardinality SHALL NOT be a policy violation solely because runtime validation is deferred. When a supported entity-member collection has per-entity cardinality unavailable without reading entity values, a finite item limit SHALL reject that use with `query-policy-in-items-unverifiable` in both metadata analysis and construction; it SHALL NOT pretend to have enforced a size bound. Collection access permission SHALL be checked independently.

A runtime-resolved query-backed collection requiring database enumeration to determine its size SHALL be rejected with `query-policy-in-items-unverifiable` under a finite item limit rather than executing the query inside size validation. Applications SHALL be able to supply an already resolved, bounded in-memory collection instead; metadata declarations alone SHALL not be assumed to reveal whether the eventual runtime collection is query-backed.

#### Scenario: Duplicate and empty collection boundary
- **WHEN** limit 2 is applied to `Id in [1, 1]`, `Id not in [1, 1, 1]`, and `Id in []`
- **THEN** the first has two items, the second fails on item 3, and the empty collection has zero items

#### Scenario: Quoted commas and incomplete lists
- **WHEN** `Name in ['a,b', 'c']` or `Id in [1, 2, $unfinished` is analyzed under limit 2
- **THEN** the first has two slots and the second exceeds the limit on its started third slot
- **AND** the unfinished element does not receive a fabricated value-conversion error

#### Scenario: Infinite variable sequence
- **WHEN** a runtime resolver returns an infinite supported collection for membership with limit N
- **THEN** at most N + 1 items are consumed, the enumerator is disposed, construction fails, and no predicate is returned

#### Scenario: Deferred size and accepted snapshot
- **WHEN** metadata declares a supported collection variable and runtime later resolves exactly N items
- **THEN** metadata analysis performs no enumeration and runtime accepts the size boundary
- **AND** the accepted sequence is enumerated only once and its snapshot is used in the emitted predicate

#### Scenario: Query-backed collection size is not silently evaluated
- **WHEN** a runtime variable resolves to a database-query-backed collection and a finite item limit applies
- **THEN** size validation reports `query-policy-in-items-unverifiable` without enumerating the query
- **AND** a bounded in-memory collection returned by an application resolver can pass the same policy

### Requirement: Canonical paths and navigation budgets
Field binding SHALL follow existing case-insensitive, exact full external-path mapping conventions without prefix rewriting. Permissions and navigation accounting SHALL use the resolved canonical CLR path, not the alias spelling. Entity navigation depth SHALL count transitions from an entity into another nonscalar, noncollection member type; the final scalar field and scalar member accesses SHALL not count. Scalar classification SHALL consistently use the library's existing scalar/conversion type knowledge, including string, enum, numeric, Boolean, Guid, temporal types, and nullable forms.

Access through an entity collection member SHALL be separately identified and permission checked, and SHALL consume one navigation level for that collection transition when permitted. Variable paths SHALL NOT count as entity navigation. Schema/member inspection SHALL read descriptors only. Exceeding navigation depth SHALL produce `query-policy-navigation-depth-exceeded` on the user-facing field path.

#### Scenario: Navigation versus scalar access
- **WHEN** navigation limit 0 is applied to `CreatedAt.Year = 2026` and `Customer.Name = 'A'`
- **THEN** the scalar DateTime member path has navigation depth zero and the Customer transition has depth one and is denied

#### Scenario: Aliases cannot shorten depth
- **WHEN** `city` maps exactly to Customer.Address.City under limit 1
- **THEN** `city = 'Paris'` is checked at canonical depth 2 and denied
- **AND** the diagnostic references city without exposing a restricted canonical path

### Requirement: Collection access permission
A denied collection access policy SHALL reject user expressions and ordering paths that reference or traverse entity collection members, including currently supported scalar members such as collection Count, and SHALL report `query-policy-collection-access-denied`. String scalar member/function use SHALL NOT be treated as collection traversal. Literal membership lists and application-resolved variable collections SHALL NOT be entity collection access. Allowing access SHALL preserve only operations already supported by the parser/builders; it SHALL NOT introduce Any, All, element lambdas, indexing, or additional collection syntax.

#### Scenario: Supported entity collection member
- **WHEN** a supported List member's Count path is used in a filter or order key with collection access denied
- **THEN** both operations fail at that external field location
- **AND** the equivalent string scalar access remains subject to its ordinary permissions rather than collection denial

#### Scenario: Variable membership remains distinct
- **WHEN** `Id in $ids` is used with entity collection access denied
- **THEN** the denial does not reject ids merely because it is collection-valued
- **AND** runtime membership size validation still applies

### Requirement: Intersection of field permissions and operator normalization
Policy-aware operations SHALL require all applicable schema query permissions, nonempty legacy-style validProperties restrictions, policy field permissions, navigation limits, and collection permissions. A policy SHALL never grant access denied by another restriction. Schema and additional validProperties allowlists SHALL remain restrictions on external source names; canonical schema ancestor denials SHALL propagate through aliases. Known denied entity names SHALL NOT fall back to variables.

Allowed-operator rules SHALL bind to canonical field identity. Rules configured through multiple aliases of the same member SHALL intersect; an empty intersection SHALL deny comparison use. Existing symbolic and word aliases, casing, and supported whitespace variations SHALL be normalized through existing operator identities before permission checks. Unknown-property suggestions SHALL use only spellings permitted by every restriction and by the current operator when known.

#### Scenario: Mapping and operator alias bypass attempts
- **WHEN** Price allows Equal only, cost maps to Price, and a user supplies cost using an existing GreaterThan or contains alias
- **THEN** every normalized equivalent is denied with `query-policy-operator-denied`
- **AND** using the canonical spelling or another alias cannot bypass the rule

#### Scenario: Independent restrictions
- **WHEN** a schema denies InternalCost, or an external-name validProperties restriction excludes a requested alias
- **THEN** allowing all operators in the policy does not permit the field
- **AND** the existing `property-not-queryable` diagnostic remains applicable without restricted-name/type disclosure

#### Scenario: Restrictive aliases combine
- **WHEN** two aliases of the same member configure operator sets Equal/In and Equal/GreaterThan
- **THEN** their effective set is Equal regardless of which external spelling the query uses

### Requirement: Application-owned computed members
Existing string property mappings SHALL remain mappings to CLR member paths, with all visible canonical paths checked by policy. Computed CLR property getters SHALL be opaque application-owned members: metadata-only analysis SHALL NOT invoke or inspect their execution, and policies SHALL constrain the exposed member and visible path rather than promise to constrain getter-internal navigation, collections, or calls. Such members SHALL remain subject to schema/allowlist/operator permissions. This feature SHALL NOT add lambda/expression mapping registration or execute application mapping callbacks. Documentation SHALL explain that applications must not expose trusted computed members whose hidden behavior defeats their intended restrictions.

#### Scenario: Opaque computed getter
- **WHEN** an exposed scalar computed property has a getter that touches an internal collection or throws
- **THEN** metadata analysis invokes it zero times and checks the scalar property's visible permissions
- **AND** policy acceptance is not a guarantee about hidden getter behavior or provider translation

### Requirement: Policy-aware entry point consistency and failure isolation
One reusable schema-bound policy context SHALL provide generic and runtime-type synchronous/asynchronous predicate parsing, direct property/operator/value construction, Filter and Filter sequence construction, FilterGroup and group sequence construction, condition construction, and existing ordering-clause/source-ordering operations. Metadata semantic analysis SHALL use the same policy context. Type mismatches SHALL be explicit argument errors. Structured composition SHALL retain collection AND and group collection OR semantics; unsupported recursive filter structures SHALL remain unsupported.

Runtime construction SHALL independently check the supplied input even if editor analysis was successful or not called. Static policy failures SHALL be detected before resolver initialization or runtime resolution; runtime size failures SHALL occur after the required resolution but before membership construction. Try/evaluation APIs SHALL return failure with null Result and actionable diagnostics; throwing builders SHALL throw an explicit policy exception carrying the same diagnostics. Invalid conditions SHALL expose no partial predicates or ordering clause. Ordering SHALL apply field query permission, navigation, collection access, and applicable text-length restrictions, not comparison-operator sets or new pagination/sort semantics. Generic/runtime conversion SHALL preserve policy diagnostics. Async cancellation SHALL remain cancellation rather than a policy diagnostic.

#### Scenario: Editor is not the enforcement boundary
- **WHEN** a client skips editor analysis or changes its expression after analysis
- **THEN** the server context checks the actual supplied expression and rejects any violation independently

#### Scenario: Equivalent surfaces
- **WHEN** equivalent string, direct, and structured predicates are supplied through generic/runtime-type and sync/async operations under the same policy
- **THEN** their applicable permissions, counts, normalized operator identities, and policy codes agree
- **AND** differences in textual/group representation use the documented length/depth rules

#### Scenario: No usable failure output
- **WHEN** one condition leaf fails policy after another leaf could be built, or one ordering key is denied
- **THEN** the result is failed or invalid with no usable predicate, partial condition predicates, sorted-source Result, or ordering clause

### Requirement: Stable diagnostics and bounded editor processing
Policy violations SHALL reuse the existing structured diagnostic model and zero-based half-open UTF-16 source spans. Stable codes SHALL include `query-policy-expression-length-exceeded`, `query-policy-parenthesis-depth-exceeded`, `query-policy-condition-count-exceeded`, `query-policy-in-items-exceeded`, `query-policy-navigation-depth-exceeded`, `query-policy-collection-access-denied`, `query-policy-operator-denied`, and `query-policy-in-items-unverifiable`. Limit diagnostics SHALL expose configured limit, observed value, and whether that value is only a lower bound. Messages SHALL be actionable without disclosing restricted canonical names/types or variable values. Denial diagnostics SHALL have no replacement suggestion.

Text locations SHALL identify the excess length suffix, first excessive opening delimiter, first excessive condition/operator, first excessive membership item or collection variable, full navigation/collection path, or denied operator token as applicable. Unfinished recognized tokens SHALL use their actual spans; missing positions SHALL use a zero-length end-of-input caret. Structured diagnostics SHALL expose an explicit input path such as `Filters[2].Operator`, `FilterGroups[1].Filters[0].Value`, `OrderBys[0].Property`, or `Where`; nontext locations SHALL use Start/Length zero rather than invented string offsets.

Policy-aware processing SHALL return at most 32 combined syntax and semantic/policy diagnostics, reserving the final position for `query-policy-diagnostics-truncated` when more diagnostics are suppressed. Duplicate code/location reports SHALL be suppressed. Fatal resource-limit failure SHALL stop deeper processing; tokens SHALL cover only the safely processed prefix, with explicit `IsTruncated` metadata and no assertion of unestablished syntax completeness. Policy permission errors SHALL make semantic validity false without redefining proven syntax completeness. Legacy syntax-only analysis SHALL retain whole-input classification and its existing contract.

#### Scenario: Precise static and structured spans
- **WHEN** `Id in [1, 2, 3]` exceeds item limit 2 and an equivalent structured filter exceeds the same limit
- **THEN** the text diagnostic covers the third item and the structured diagnostic identifies the leaf Value location
- **AND** both expose limit 2, observed count 3, and the same policy code

#### Scenario: Bounded malformed input
- **WHEN** a policy-aware editor input would generate more than 32 independent diagnostics
- **THEN** at most 32 are returned with the truncation marker and invalidity preserved
- **AND** oversized/deep input is not fully tokenized merely to preserve highlighting

### Requirement: Metadata-only purity and deferred limits
Policy-aware analysis and schema/policy binding SHALL remain synchronous and metadata-only. They SHALL NOT execute variable resolvers, initialization hooks, entity/variable getters, collection enumeration of runtime values, compiled expressions, user conversions/operators, database queries, or external services, including malformed-input and diagnostic-recovery paths. Documentation SHALL distinguish statically checkable budgets from variable-backed membership cardinality and runtime value/conversion/availability failures that cannot be decided from declarations.

#### Scenario: Instrumented purity
- **WHEN** valid, invalid, incomplete, and oversized inputs reference declared variables and throwing entity members
- **THEN** resolver, initialization, getter, enumeration, conversion, and external-call counters remain zero during metadata analysis
- **AND** a runtime control confirms the instrumentation can observe actual resolution

### Requirement: Documentation, compatibility, and validation
Implementation SHALL include `docs/query-policies.md`, a concise README quick start with a relative guide link, and a semantic-analysis cross-reference. The guide SHALL document complete API/configuration examples, defaults and recommended opt-in limits, configuration errors, all counting and location rules, stable codes, aliases/mappings, structured/ordering behavior, bounded enumeration, metadata/runtime differences, opaque computed members, and compatibility. Examples SHALL compile against implemented APIs and every local documentation link SHALL resolve.

Policies SHALL be documented as permitted-expression construction and complexity controls, not substitutes for authorization, tenant isolation, database timeouts, provider translation validation, or query-cost controls. An example SHALL apply an application-owned tenant predicate outside and together with the user predicate, checking failure before consuming it; tenant/authorization predicates SHALL not be derived from or removable by user input.

Acceptance SHALL test each numeric boundary and one unit beyond, every permission denial/allowance, quoted and incomplete input, NOT and nested groups, duplicate lists, aliases/operator aliases/mappings, cross-surface parity, failed-output nullability, purity, bounded enumeration/disposal/cancellation, and no-policy compatibility. Existing unit and relational suites SHALL continue to pass.

#### Scenario: Tenant example and docs verification
- **WHEN** documentation examples are compiled and their links checked
- **THEN** examples use real public APIs, reject failed user predicates, and combine tenant and user predicates outside user-controlled text
- **AND** README links to the dedicated guide with a resolving relative path

#### Scenario: Compatibility regression
- **WHEN** existing tests run with no policy after implementation
- **THEN** their documented outcomes remain unchanged and the core library still builds for its existing target framework
