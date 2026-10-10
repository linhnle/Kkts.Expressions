# Spec Delta

## ADDED Requirements

### Requirement: Independent filter tree depth and accounting
Policy configuration SHALL add an immutable optional maximum filter-tree depth without changing existing constructor signatures or parenthesis-depth meanings. Omitted tree depth SHALL mean unlimited; negative values SHALL be explicit configuration errors. A condition leaf SHALL have depth zero; each AND, OR, or NOT container SHALL add one on its descendant path. Single-child groups and repeated NOT SHALL each consume depth without normalization. The recommended preset SHALL explicitly select tree depth 16 for the new surface while retaining all existing values and opt-in behavior.

Tree length budgets SHALL sum supplied field/operator strings, literal strings, exact numeric token text, variable path strings, and canonical Boolean/null token lengths across all leaves, including duplicates, without JSON keys, delimiters, escaping, or synthetic grouping text. The condition filter budget SHALL aggregate Where, legacy structured text, and tree text in one operation; ordering SHALL retain its independent budget. Tree conditions SHALL each count one leaf and logical containers SHALL not add conditions. Membership size SHALL count supplied slots before conversion or deduplication, including nulls and scalar reference slots. Whole collection references SHALL use existing bounded runtime-resolution and unverifiable-size rules. Canonical field permissions, navigation, collection access, and normalized operator checks SHALL apply exactly as on other policy-aware surfaces. No synthetic legacy adapter container SHALL charge new tree depth to a legacy input.

The first excessive logical container SHALL produce `query-policy-filter-tree-depth-exceeded`, with configured limit, observed depth, zero text span, and its JSON Pointer location. Other tree policy failures SHALL reuse existing codes with tree pointer locations. Policy-aware diagnostics SHALL retain their maximum of 32, deduplication, truncation metadata, and no-partial-output guarantees. Processing and JSON decoding SHALL reject limits before descending or unbounded materialization, with no arbitrary lower serializer depth cap silently replacing the configured tree limit.

#### Scenario: Tree depth boundary
- **WHEN** maximum tree depth 2 is applied to a leaf inside AND then NOT, and to that input with an additional OR container
- **THEN** the first passes the depth check and the second reports observed depth 3 at the OR pointer
- **AND** setting parenthesis depth alone does not substitute a tree-depth limit

#### Scenario: Aggregate inputs and values
- **WHEN** Where, legacy Filters/FilterGroups, and FilterTree together exceed a length or condition limit
- **THEN** the condition operation fails at the first excessive supplied contribution before any resolver initialization
- **AND** duplicates and null membership items are not discarded to evade accounting

#### Scenario: Alias and collection bypass attempts
- **WHEN** tree input uses a short alias to a denied/deep/collection member, an operator alias denied by policy, or an infinite variable collection
- **THEN** the same canonical permissions and existing policy codes apply
- **AND** finite membership limits consume at most limit plus one runtime items and dispose the enumerator

## MODIFIED Requirements

### Requirement: Parenthesis and processing depth budgets
Parenthesis depth SHALL count simultaneously open `(` delimiters outside quoted strings, including grouping, NOT calls, comparison-function calls, and parenthesized membership lists. Quotes and escapes SHALL follow the existing grammar; brackets and braces SHALL NOT increment the parenthesis counter. An unterminated quoted string SHALL retain quoted status to end of input. The first opening parenthesis producing depth greater than the limit SHALL cause `query-policy-parenthesis-depth-exceeded` before deeper parser work or allocation.

Existing structured group containers SHALL consume one group-depth level against the existing parenthesis-depth setting, with a standalone Filter and a flat Filter sequence at depth zero and a FilterGroup at depth one. The additive nested filter tree SHALL instead use the independent filter-tree-depth setting; tree containers SHALL NOT be interpreted as source parentheses. Textual membership fragments SHALL also enforce parenthesis depth where their existing syntax uses parentheses. Policy-aware processing SHALL avoid unbounded call-stack growth from repeated NOT, long arithmetic/logical chains, nested tree containers, malformed delimiters, or schema paths; condition counting SHALL NOT incorrectly charge these operators as predicates.

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

#### Scenario: Separate tree and text depth
- **WHEN** a tree is built directly under parenthesis depth zero and sufficient tree depth
- **THEN** no parenthesis diagnostic is fabricated from tree containers
- **AND** conversion to text independently checks actual generated parentheses

### Requirement: Policy-aware entry point consistency and failure isolation
One reusable schema-bound policy context SHALL provide generic and runtime-type synchronous/asynchronous predicate parsing, direct property/operator/value construction, Filter and Filter sequence construction, FilterGroup and group sequence construction, nested tree construction, condition construction, and existing ordering-clause/source-ordering operations. Metadata semantic analysis, metadata-only tree validation, and bidirectional filter conversion SHALL use the same policy context. Type mismatches SHALL be explicit argument errors. Structured composition SHALL retain collection AND and group collection OR semantics; the additive nested tree SHALL provide arbitrary ordered AND/OR/NOT composition without changing those legacy semantics.

Runtime construction SHALL independently check the supplied input even if editor analysis was successful or not called. Static policy failures SHALL be detected before resolver initialization or runtime resolution; runtime size failures SHALL occur after the required resolution but before membership construction. Try/evaluation APIs SHALL return failure with null Result and actionable diagnostics; throwing builders SHALL throw an explicit policy exception carrying the same diagnostics. Invalid policy-aware conditions SHALL expose no partial predicates or ordering clause. Ordering SHALL apply field query permission, navigation, collection access, and applicable text-length restrictions, not comparison-operator sets or new pagination/sort semantics. Generic/runtime conversion SHALL preserve policy diagnostics. Async cancellation SHALL remain cancellation rather than a policy diagnostic.

#### Scenario: Editor is not the enforcement boundary
- **WHEN** a client skips editor analysis or changes its expression after analysis
- **THEN** the server context checks the actual supplied expression and rejects any violation independently

#### Scenario: Equivalent surfaces
- **WHEN** equivalent string, direct, legacy structured, and nested-tree predicates are supplied through generic/runtime-type and sync/async operations under the same policy
- **THEN** their applicable permissions, counts, normalized operator identities, and policy codes agree
- **AND** differences in textual/group/tree representation use the documented length/depth rules

#### Scenario: No usable failure output
- **WHEN** one condition leaf fails policy after another leaf could be built, or one ordering key is denied
- **THEN** the result is failed or invalid with no usable predicate, partial condition predicates, sorted-source Result, or ordering clause
