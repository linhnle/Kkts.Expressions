# Spec Delta

## MODIFIED Requirements

### Requirement: Canonical paths and navigation budgets
Outside explicit public-schema mode, field binding SHALL follow existing case-insensitive, exact full external-path mapping conventions without prefix rewriting. Permissions and navigation accounting SHALL use the resolved canonical CLR path, not the alias spelling. Entity navigation depth SHALL count transitions from an entity into another nonscalar, noncollection member type; the final scalar field and scalar member accesses SHALL not count. Scalar classification SHALL consistently use the library's existing scalar/conversion type knowledge, including string, enum, numeric, Boolean, Guid, temporal types, and nullable forms.

Access through an entity collection member SHALL be separately identified and permission checked, and SHALL consume one navigation level for that collection transition when permitted. Variable paths SHALL NOT count as entity navigation. Schema/member inspection SHALL read descriptors only. Exceeding navigation depth SHALL produce `query-policy-navigation-depth-exceeded` on the user-facing field path.

In explicit public-schema mode, registered expressions SHALL be opaque application-owned configuration. Navigation and entity-collection permissions SHALL constrain client-authored access, not internal selector bodies. Exact registered scalar names SHALL have zero client navigation depth and SHALL not be rejected solely because their trusted expressions navigate members or access collections. Appended member paths SHALL remain unavailable unless exposed as separate registrations. Legacy string aliases SHALL retain canonical accounting and SHALL not be combined with public mode to evade restrictions.

#### Scenario: Navigation versus scalar access
- **WHEN** navigation limit 0 is applied to `CreatedAt.Year = 2026` and `Customer.Name = 'A'` outside public mode
- **THEN** the scalar DateTime member path has navigation depth zero and the Customer transition has depth one and is denied

#### Scenario: Aliases cannot shorten depth
- **WHEN** `city` maps exactly to Customer.Address.City under limit 1 in a legacy reflected schema
- **THEN** `city = 'Paris'` is checked at canonical depth 2 and denied
- **AND** the diagnostic references city without exposing a restricted canonical path

#### Scenario: Trusted public projection
- **WHEN** city is a registered expression projecting Customer.Address.City and itemCount is a registered scalar projection of Items.Count, under navigation limit 0 and collection access denied
- **THEN** both exact public fields remain usable subject to their filter/sort/operator permissions
- **AND** Customer.Address.City, Items.Count, and city.Length remain unknown unless explicitly registered as independent public names

### Requirement: Intersection of field permissions and operator normalization
Outside explicit public-schema mode, policy-aware operations SHALL require all applicable schema query permissions, nonempty legacy-style validProperties restrictions, policy field permissions, navigation limits, and collection permissions. A policy SHALL never grant access denied by another restriction. Schema and additional validProperties allowlists SHALL remain restrictions on external source names; canonical schema ancestor denials SHALL propagate through aliases. Known denied entity names SHALL NOT fall back to variables.

Outside public mode, allowed-operator rules SHALL bind to canonical field identity. Rules configured through multiple aliases of the same member SHALL intersect; an empty intersection SHALL deny comparison use. Existing symbolic and word aliases, casing, and supported whitespace variations SHALL be normalized through existing operator identities before permission checks. Unknown-property suggestions SHALL use only spellings permitted by every restriction and by the current operator when known.

In public mode, filtering SHALL require registered filter permission and sorting SHALL independently require registered sort permission, with additional registered-public-name allowlists intersecting either operation. Field-declared operator sets and policy sets SHALL intersect by registered public identity after normalization. Omitted operator sets SHALL remain unrestricted for existing applicable comparisons; an empty set SHALL deny filtering comparison use but SHALL not deny sorting. Differently named public registrations SHALL remain independently governed even when their selectors use the same internal member. Nonempty legacy mappings, canonical-member overrides, and unknown names in additional allowlists or policy rules SHALL fail binding explicitly. Internal canonical paths SHALL not become additional public permission keys. Bare identifiers SHALL never fall back to variables in public mode; explicit references SHALL preserve existing variable rules.

#### Scenario: Mapping and operator alias bypass attempts
- **WHEN** Price allows Equal only, cost maps to Price, and a user supplies cost using an existing GreaterThan or contains alias outside public mode
- **THEN** every normalized equivalent is denied with `query-policy-operator-denied`
- **AND** using the canonical spelling or another alias cannot bypass the rule

#### Scenario: Independent restrictions
- **WHEN** a legacy schema denies InternalCost, or an external-name validProperties restriction excludes a requested alias
- **THEN** allowing all operators in the policy does not permit the field
- **AND** the existing `property-not-queryable` diagnostic remains applicable without restricted-name/type disclosure

#### Scenario: Restrictive aliases combine
- **WHEN** two legacy aliases of the same member configure operator sets Equal/In and Equal/GreaterThan
- **THEN** their effective set is Equal regardless of which external spelling the query uses

#### Scenario: Public field operators and operation permissions
- **WHEN** total is filter-only and allows Equal/In, its policy allows Equal/GreaterThan, and label is sort-only
- **THEN** total filtering permits Equal only, total sorting is denied, label filtering is denied, and label sorting can succeed
- **AND** normalized operator aliases cannot expand the effective set

#### Scenario: Public identities cannot be bypassed by aliases
- **WHEN** public mode registers cost from Price but does not register Price, and a caller supplies a legacy map or a policy key Price
- **THEN** binding fails explicitly rather than admitting Price as a query name
- **AND** two deliberately registered names using Price can have different public operator permissions without canonical alias intersection

### Requirement: Application-owned computed members
Existing string property mappings SHALL remain mappings to CLR member paths, with all visible canonical paths checked by policy outside explicit public-schema mode. Computed CLR property getters SHALL be opaque application-owned members: metadata-only analysis SHALL NOT invoke or inspect their execution, and policies SHALL constrain the exposed member and visible path rather than promise to constrain getter-internal navigation, collections, or calls. Such members SHALL remain subject to schema/allowlist/operator permissions.

Explicit public-schema mode SHALL additionally support trusted typed expression registrations under the expression-field-mapping contract. Policies SHALL constrain public field identity, result metadata, independent filter/sort permissions, and allowed operators without interpreting or executing expression-internal navigation, collections, getters, or calls. Registration SHALL not execute mapping callbacks or compile selectors. Documentation SHALL explain that applications must not expose trusted computed members or expression fields whose behavior defeats intended authorization, complexity, cost, or translation restrictions.

#### Scenario: Opaque computed getter
- **WHEN** an exposed scalar computed property has a getter that touches an internal collection or throws
- **THEN** metadata analysis invokes it zero times and checks the scalar property's visible permissions
- **AND** policy acceptance is not a guarantee about hidden getter behavior or provider translation

#### Scenario: Opaque computed expression
- **WHEN** an application registers a scalar expression accessing internal navigation or calling a method that would throw
- **THEN** registration, policy binding, and semantic analysis execute it zero times and check the public field permissions
- **AND** policy acceptance is not a guarantee of selector execution safety, hidden cost, or SQL translation

### Requirement: Policy-aware entry point consistency and failure isolation
One reusable schema-bound policy context SHALL provide generic and runtime-type synchronous/asynchronous predicate parsing, direct property/operator/value construction, Filter and Filter sequence construction, FilterGroup and group sequence construction, condition construction, and existing ordering-clause/source-ordering operations. Metadata semantic analysis SHALL use the same policy context. Type mismatches SHALL be explicit argument errors. Structured composition SHALL retain collection AND and group collection OR semantics; unsupported recursive filter structures SHALL remain unsupported by these legacy APIs.

Runtime construction SHALL independently check the supplied input even if editor analysis was successful or not called. Static policy failures SHALL be detected before resolver initialization or runtime resolution; runtime size failures SHALL occur after the required resolution but before membership construction. Try/evaluation APIs SHALL return failure with null Result and actionable diagnostics; throwing builders SHALL throw an explicit policy exception carrying the same diagnostics. Invalid conditions SHALL expose no partial predicates or ordering clause. Outside public mode, ordering SHALL apply field query permission, navigation, collection access, and applicable text-length restrictions, not comparison-operator sets or new pagination/sort semantics. In public mode, filtering and ordering SHALL apply their separate field permissions and the public-field resolution contract; ordering SHALL not require filtering permission or comparison-operator sets. Generic/runtime conversion SHALL preserve policy diagnostics. Async cancellation SHALL remain cancellation rather than a policy diagnostic.

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

#### Scenario: Public-mode operation parity
- **WHEN** a registered field is filter-only or sort-only and equivalent inputs are supplied to every applicable context operation
- **THEN** string, direct, structured, tree, and condition filtering agree on filter permission and string/structured ordering agrees on sort permission
- **AND** no entry point reconstructs an internal path to bypass public visibility
