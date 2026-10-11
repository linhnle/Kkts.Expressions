# Spec Delta

## Purpose

Enable expression editors to request deterministic, contextually compatible completions from public query metadata without evaluating expressions, exposing restricted schema information, or depending on a UI framework.

## ADDED Requirements

### Requirement: Additive metadata-only completion API

The library SHALL expose synchronous completion accepting exact expression text, a cursor offset, the same entity schema and optional declared variable metadata used by semantic analysis, optional declared value hints, and optional completion options. A schema-bound query context entry point SHALL use that context's policy and additional field restrictions without requiring callers to reconstruct them. Omitting variable metadata SHALL mean no declared variables, not implicit built-in declarations. The API SHALL be independent of editor, database, and HTTP frameworks and remain compatible with the existing core target framework.

Null text or a required null schema SHALL throw `ArgumentNullException`. Cursor offsets outside `[0, text.Length]` SHALL throw `ArgumentOutOfRangeException`; offsets between the two units of a surrogate pair SHALL also throw that exception. Valid blank input SHALL be an editing state, not an argument error. Malformed user expressions SHALL not throw configuration exceptions.

#### Scenario: Blank expression with declared metadata
- **WHEN** a caller completes blank text with a schema exposing Price and Status
- **THEN** permitted field and condition-start suggestions are returned without requiring a complete predicate
- **AND** no variable is offered unless declared

#### Scenario: Invalid cursor and required arguments
- **WHEN** a cursor is negative, beyond the input length, or splits a supplementary character, or a required argument is null
- **THEN** the documented argument exception is thrown without evaluating application code

#### Scenario: Query context parity
- **WHEN** completion is called on a query context with an additional allowlist and operator restrictions
- **THEN** both restrictions apply alongside its schema and policy
- **AND** passing the same effective metadata through the schema entry point produces equivalent suggestions

### Requirement: Immutable structured result contract

Results SHALL expose an owned read-only item collection, existing structured diagnostics, a stable status (`Available`, `NoMatches`, `ContextUnavailable`, or `LimitExceeded`), and `IsIncomplete` indicating an omitted eligible tail or work truncation. Every item SHALL expose a stable kind (`Field`, `Operator`, `Value`, `Variable`, `LogicalOperator`, or `Delimiter`), display label, insertion text, replacement `Start` and `Length`, non-null description, and optional data-type metadata containing CLR type, nullability, and collection element type where applicable. Type metadata SHALL describe the public result or contextual value, not internal selector inputs. Labels SHALL use application display names when supplied, but insertion SHALL always use accepted public spellings.

Diagnostics SHALL distinguish a deliberately suppressed context from a processing limit; empty items SHALL not masquerade as a successful determination of context. An ordinary valid context with no matching candidates SHALL return `NoMatches` without a fabricated syntax error. Incomplete syntax SHALL not itself make completion unavailable. Completion SHALL not assert predicate executability or replace runtime validation.

#### Scenario: Snapshot and public type information
- **WHEN** completion offers a public computed decimal field named total with a display label
- **THEN** its label uses the display label, insertion uses total, and its type is Decimal
- **AND** results cannot be mutated and disclose neither selector bodies nor their input members

#### Scenario: Recognized empty versus ambiguous context
- **WHEN** a reliable field-prefix context has no match, and separately an unknown receiver makes an operator context undecidable
- **THEN** the former returns `NoMatches` and the latter returns `ContextUnavailable`
- **AND** suppression has a stable `completion-context-unavailable` diagnostic at the cursor

### Requirement: Original-source cursor and replacement spans

Offsets and replacement spans SHALL use zero-based UTF-16 units into the exact, untrimmed input, with half-open ranges `[Start, Start + Length)`. End-of-input insertion SHALL use `(text.Length, 0)`. Every replacement SHALL be within input bounds and SHALL preserve all text outside its span. Whitespace gaps SHALL not be consumed.

An editable identifier or variable SHALL replace its complete token, including partial text to the right of the cursor, dots, and an existing `$`; insertion SHALL contain the complete selected public path or variable reference exactly once. A quoted literal SHALL replace the entire quoted token, including existing quotes; an unterminated quoted token extends to end of input. Unquoted partial literals and operators SHALL replace their complete editable token, including existing contiguous characters on both sides. A recognized compound operator SHALL be replaced as one operator span, including its internal whitespace. Deeper path segments beyond the active identifier segment SHALL constrain candidates rather than be silently discarded. Outside an editable token, replacement SHALL have zero length at the cursor.

Insertion SHALL omit optional whitespace, commas, and delimiters. Only minimal spaces required to separate the inserted token from adjacent surviving text SHALL be included. An opening delimiter item SHALL insert just that delimiter, never an automatic closer. Existing logical operators, commas, quotes, dots, `$`, and closing delimiters SHALL not be duplicated.

#### Scenario: Completing a field in the middle
- **WHEN** the cursor is after Sta in `StaTus = 'Active' and Price > 1` and Status is selected
- **THEN** replacement is `(0, 6)` with insertion `Status`
- **AND** accepting it yields `Status = 'Active' and Price > 1`

#### Scenario: Nested path and variable prefix
- **WHEN** Name is selected at the end of `Customer.` or utcnow is selected after `$` in `CreatedAt > $`
- **THEN** replacements cover `Customer.` and `$` respectively
- **AND** insertion uses `Customer.Name` and `$utcnow` without duplicated punctuation

#### Scenario: Partial operator and surviving delimiter
- **WHEN** `Price >` is edited within the `>` token to select `>=`, or completion runs immediately before an existing group closer after a complete condition
- **THEN** the operator token is replaced with `>=`
- **AND** no second closing delimiter is offered at the existing closer

#### Scenario: Multiline and supplementary text
- **WHEN** text before the cursor includes CRLF, whitespace, and a supplementary Unicode character
- **THEN** offsets include both CRLF units and both surrogate units
- **AND** applying the returned slice replacement preserves the exact surrounding input

### Requirement: Cursor-local recoverable grammar context

Completion SHALL determine context using text both before and after the cursor and the existing language's tokenization, precedence, parser state, and recovery boundaries. It SHALL not require whole-input parse success or infer context solely from the final character. At the end of an exact, complete operand, continuation context SHALL take precedence over identifier-prefix completion; inside an identifier or at the end of an incomplete identifier, identifier completion SHALL apply.

Where an operand or condition can begin, completion SHALL offer permitted fields, compatible declared variables, and existing applicable group/NOT constructs. After a field or typed operand, it SHALL offer only compatible existing operators allowed for every field contributing to that operand. After an operator, it SHALL offer compatible literal hints and variable references; fields SHALL also be eligible where existing operand semantics permit them. After a complete Boolean condition, it SHALL offer existing logical operators and unmatched closing delimiters appropriate to its scope. Existing comparison-function contexts SHALL use the same receiver and argument rules.

Grouping, repeated NOT, supported membership delimiters, membership item positions, and partial identifiers/paths SHALL be supported. A later invalid clause SHALL not disable a reliable earlier context, and recovery after a logical/scope boundary SHALL permit later reliable contexts. Unknown or denied receivers, conflicting suffix syntax, ambiguous malformed scope, and non-Boolean incomplete conditions SHALL suppress suggestions whose validity cannot be established.

#### Scenario: Numeric operator context
- **WHEN** completion runs at the end of `Price ` with Decimal Price and an explicit policy allowing Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, and In
- **THEN** canonical suggestions are `=`, `!=`, `>`, `>=`, `<`, `<=`, and `in`
- **AND** contains and nonconfigured operators are absent

#### Scenario: Nested NOT and complete conditions
- **WHEN** completion runs after `not (Price > 1` in a policy-permitted scope
- **THEN** AND, OR, and a matching `)` are eligible in canonical form
- **AND** completions do not change NOT precedence or invent another condition

#### Scenario: Membership item position
- **WHEN** completion runs after the comma in `Status in ['Active', ]`
- **THEN** compatible Status values and scalar variables are eligible
- **AND** an entire collection variable is not offered as one scalar list element

#### Scenario: Interior cursor and independent recovery
- **WHEN** a cursor is in the first clause of `Price > 1 and Unknown = )`, or after a recoverable boundary before a later valid field prefix
- **THEN** a reliable local completion is returned despite the independent error
- **AND** suggestions are suppressed if the local suffix itself conflicts with that completion

### Requirement: Partial operators and literal editing

Within a partially typed operator, suggestions SHALL match the typed operator prefix and be receiver/type/policy compatible. At the end of an already complete comparison operator followed by whitespace or an operand slot, value context SHALL apply. A partial `not in` SHALL be distinguished from unary NOT using the surrounding operand state. Recognized partial Boolean/null literals SHALL offer matching compatible literals; arbitrary incomplete numbers and temporal literals SHALL not receive invented values.

Inside a quoted token, completion SHALL offer only compatible declared literal or enum-value suggestions for a reliably bound value slot, matching the decoded content before the cursor. It SHALL not offer fields, variables, operators, or punctuation inside arbitrary quoted text. Replacement SHALL retain the active quote style when representable and repair missing quotes only by replacing the whole literal. If decoded prefix or lossless encoding cannot be determined, those suggestions SHALL be suppressed.

#### Scenario: Partial compound operator
- **WHEN** a cursor follows `Price not i` and NotIn is type-compatible and permitted
- **THEN** `not in` is offered as one replacement of the partial compound operator
- **AND** unary NOT is not substituted in that position

#### Scenario: Literal prefix and closed quote
- **WHEN** Status has declared string hint Active and the cursor follows Ac inside `Status = 'AcZZ' and Price > 1`
- **THEN** Active replaces the complete quoted token with `'Active'`
- **AND** the trailing condition remains unchanged

#### Scenario: Quoted text without a reliable value slot
- **WHEN** the cursor is inside a quoted string used in an undecidable malformed expression or arbitrary free-form text with no matching hints
- **THEN** fields and operators are not offered as if the quote were outside a literal
- **AND** unavailable context and a reliable value slot with no matches remain distinguishable

### Requirement: Public schema and policy visibility

Field suggestions SHALL satisfy filter/query permission, applicable schema and context allowlists, canonical-identity operator restrictions, navigation depth, and collection-access restrictions. Policy SHALL not widen schema permission. Denied fields SHALL not expose names, types, descriptions, internal aliases, or selector details through items or generated diagnostics; completion SHALL not echo hidden metadata merely to explain denial. User-supplied text in existing diagnostics SHALL not be augmented with undisclosed canonical names.

Typed public schemas SHALL offer only exact registered scalar names with their result metadata. They SHALL not append CLR members to those names or expose selector input paths. Legacy reflected schemas SHALL follow existing readable-member/path rules, ancestor denials, exact full-path mappings, and nonempty allowlists. Nested suggestions SHALL be generated only at the requested path level or from explicit full-path registrations; aliases SHALL not implicitly rewrite prefixes. Navigation/collection policy checks SHALL use existing canonical accounting, including the public-expression mapping exception.

#### Scenario: Restricted field and operator
- **WHEN** InternalCost is denied, sortOnly is nonfilterable, and Price permits only Equal
- **THEN** neither restricted field is suggested and only applicable Equal comparison spelling is offered for Price
- **AND** no restricted description, type, or canonical mapping path appears in output

#### Scenario: Computed public field remains opaque
- **WHEN** total maps to internal UnitPrice and Quantity and only total is registered
- **THEN** total has its declared scalar result type
- **AND** UnitPrice, Quantity, `total.Year`, and unregistered entity members are never suggested

#### Scenario: Permitted nested fields versus exact aliases
- **WHEN** a legacy schema explicitly permits Customer.Name and maps buyer exactly to Customer.Name
- **THEN** `Customer.` can offer Customer.Name if policy allows its canonical depth
- **AND** `buyer.` does not imply buyer.Name or expose the internal mapping target

#### Scenario: Navigation and collections
- **WHEN** a legacy Customer.Name path exceeds navigation depth or a collection Count path violates collection-access policy
- **THEN** those paths are absent even if their public prefixes match
- **AND** variable collections remain governed by variable/member and membership semantics rather than entity navigation rules

### Requirement: Declared values are hints rather than constraints

Completion SHALL offer declared enum names as existing quoted literals, applicable Boolean literals, and null only where existing operator/literal/null semantics permit it. Null suggestions SHALL follow the existing contextual rules, including comparison null lifting, rather than impose a new blanket non-nullable-field restriction. Completion SHALL not invent arbitrary string, numeric, date, flag-enum combinations, or runtime enum values.

Applications SHALL be able to supply immutable literal hints keyed by accepted public field names, with optional labels and descriptions. Hints SHALL be explicit scalar literal representations, not callbacks or arbitrary objects. Null declarations, unknown field keys, duplicate field keys under field-name comparison rules, unsupported hint kinds, incompatible literal types, and unrepresentable literal encodings SHALL fail explicitly at metadata binding. Hints SHALL not modify semantic validity or runtime allowed values. Restricted hints SHALL be filtered before exposing their labels or descriptions. Membership-specific grammar ambiguity SHALL exclude a hint in that context without widening the grammar.

#### Scenario: Explicit status choices
- **WHEN** Status has declared hints Active, Pending, and Closed and completion runs at `Status = `
- **THEN** `'Active'`, `'Pending'`, and `'Closed'` are eligible
- **AND** an otherwise compatible unlisted Status value still passes the existing semantic rules

#### Scenario: Boolean enum and null semantics
- **WHEN** completion targets Boolean, enum, and null-compatible operand positions
- **THEN** it offers existing literal spellings with contextual type metadata
- **AND** incompatible operators, fabricated enum combinations, and null-incompatible membership items are absent

#### Scenario: Invalid hint metadata
- **WHEN** hints use a nonexistent public field, an incompatible numeric value, a variable/collection hint kind, or text that cannot round-trip through the existing grammar
- **THEN** binding throws an explicit argument error naming the configuration issue
- **AND** arbitrary object formatting or application code is not executed

### Requirement: Declared variables and purity

Completion SHALL offer only declared variable roots and declared or explicitly reflection-enabled member paths, using the same root/member name comparison rules and exact dotted-declaration precedence as semantic binding. Insertion SHALL use explicit `$` references even where legacy bare-variable fallback is accepted. Type compatibility SHALL reuse metadata-decidable conversion rules, preserving supported value-dependent conversions without claiming future runtime availability.

Collection variables SHALL be eligible as whole membership operands only when their element type is compatible under existing semantics. Scalar list slots SHALL use scalar variable compatibility. A member-path prefix whose root type is not itself compatible SHALL remain completable when a directly exposed compatible child is available. No resolver, initialization hook, getter, runtime collection enumerator, expression selector, user conversion/operator, database query, or external service SHALL run during completion or its metadata binding.

#### Scenario: Explicit temporal declarations
- **WHEN** only DateTime variables utcnow and startOfMonth are declared and completion runs after `$` in `CreatedAt > $`
- **THEN** those names are offered with `$` insertion and compatible type metadata
- **AND** no undeclared built-in or resolver-discovered variable is offered

#### Scenario: Direct and reflected variable members
- **WHEN** explicit member metadata or reflection-enabled metadata declares a compatible user.department member
- **THEN** `$user.` can offer `$user.department`
- **AND** exact dotted declarations take precedence and undeclared member trees are not invented

#### Scenario: Membership collection compatibility
- **WHEN** Id is Int32 and variables declare Int32[] ids, String[] names, and scalar Int32 minimum
- **THEN** `Id in $` can offer ids but not an incompatible collection
- **AND** a scalar list item can offer minimum without enumerating ids

#### Scenario: Instrumented no-execution guarantee
- **WHEN** valid, incomplete, invalid, restricted, and over-limit inputs refer to throwing getters, resolvers, selectors, and runtime collections
- **THEN** every application-execution counter remains zero
- **AND** a separate runtime control proves that the instrumentation detects actual execution

### Requirement: Deterministic bounded completion

Results SHALL use prefix matching rather than fuzzy guesses. Field/path and literal prefix matching SHALL be ordinal case-insensitive; variable matching SHALL preserve binding-valid declared casing and root/member comparison rules. Deduplication SHALL happen before result limiting using kind, binding identity or literal value, and replacement span; public aliases with distinct accepted spellings SHALL remain distinct. Equivalent operator aliases SHALL yield one canonical spelling. Duplicate value hints SHALL not multiply items; explicit hint labels/descriptions SHALL take precedence over generated metadata for the same literal.

Ordering SHALL use kind order Field, Operator, Value, Variable, LogicalOperator, Delimiter; exact prefix matches before longer matches; then ordinal case-insensitive accepted spelling and ordinal spelling as a tie-breaker. Operators SHALL additionally use canonical language order `=`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `not in`, followed by existing string comparisons; logical operators SHALL use `and`, `or`, then unary `not` where applicable. Delimiters SHALL use their grammatical scope order. Display labels SHALL not change filtering or rank.

Maximum results SHALL default to 50; explicit values from 1 through 200 SHALL be accepted and other values SHALL throw an argument-range exception. Additional eligible results SHALL set `IsIncomplete`. Completion SHALL enforce independent ceilings of 16,384 input UTF-16 units, 8,192 tokens, 64 simultaneously open syntactic scopes, and 4,096 candidate-descriptor visits per request, without changing existing analysis/policy defaults. Metadata traversal SHALL be deterministic and limited to the requested level; exhaustion of the candidate budget SHALL return only proven, safely ordered candidates with `IsIncomplete`, never an unbounded discovery pass. Fixed source/token/scope ceiling violations SHALL return no items, `LimitExceeded`, `IsIncomplete`, and `completion-work-limit-exceeded`.

#### Scenario: Ordering deduplication and limit
- **WHEN** metadata declares repeated hints, equivalent operator aliases, and more than 50 eligible candidates
- **THEN** repeated equivalent items are removed, canonical ordering is stable across repeated calls, and at most 50 items are returned
- **AND** `IsIncomplete` is true only when results or work were actually omitted

#### Scenario: Explicit maximum and bounded discovery
- **WHEN** MaxResults is 1 or 200, or a metadata level exceeds 4,096 descriptor visits
- **THEN** the configured result cap and descriptor ceiling are honored
- **AND** partial results identify truncation without exposing unvalidated candidates

#### Scenario: Fixed work boundaries
- **WHEN** input crosses a fixed source, token, or scope ceiling by one
- **THEN** completion stops before deeper processing and returns explicit limit status
- **AND** at the ceiling otherwise permitted input is not rejected merely for equaling the budget

### Requirement: Determinable query limits and failure isolation

Completion SHALL check the exact input against policy length/depth/static condition/membership budgets before deeper processing. Already exceeded resource limits SHALL return no items, `LimitExceeded`, `IsIncomplete`, and existing positioned policy diagnostics. A tighter query-policy limit SHALL take precedence over the corresponding completion ceiling. Combined diagnostics SHALL be capped at 32 using the existing truncation marker convention.

Before offering a candidate, completion SHALL account for its actual replacement in the surviving expression and suppress candidates that determinably exceed length, parenthesis depth, condition, membership-item, navigation, collection, or operator limits. Replacing an existing token or started list slot SHALL not charge it twice. At a full condition/item budget, logical operators or separators that necessarily begin another prohibited construct SHALL be absent; matching closers and edits within an existing permitted slot SHALL remain eligible. Variable-backed cardinality SHALL remain deferred, not guessed or treated as a static violation.

#### Scenario: Over-limit input
- **WHEN** an unfinished expression exceeds the policy length or nesting budget
- **THEN** no suggestion is returned and the existing exact-source policy diagnostic identifies the limit
- **AND** the entire oversized input is not tokenized for completion

#### Scenario: Boundary completion is an edit rather than another slot
- **WHEN** a membership list already has the maximum number of started items and the cursor edits one of them
- **THEN** compatible replacement values remain eligible if their resulting text stays within limits
- **AND** a suggestion that necessarily creates an extra item is absent

#### Scenario: Complete condition at maximum count
- **WHEN** the input contains the maximum permitted conditions and the cursor follows a complete condition in an open group
- **THEN** a matching closer can be offered
- **AND** AND/OR continuations that require another condition are absent

### Requirement: Documentation integration and performance verification

Implementation SHALL provide a dedicated Markdown guide covering actual API signatures, item/status contracts, contexts, field/variable/policy configuration, literal hints, precise replacements, UTF-16/editor coordinate mapping, hard limits, deferred runtime checks, and limitations. README SHALL include a concise configured example and relative links to the guide and one runnable, minimal editor sample outside the core library. That sample SHALL map cursor offsets, item kinds, replacement spans, and available semantic diagnostics; any backend completion path SHALL debounce requests and ignore stale text/cursor responses. Dependencies SHALL remain sample-local.

Acceptance SHALL test configured representative contexts, incomplete/partial tokens, interior cursor and suffix text, nested NOT/groups, all existing membership delimiters, quoted strings/escapes, accepted replacement text, type/permission boundaries, non-disclosure, no-execution instrumentation, deterministic ordering, duplicate handling, result/work limits, and invalid positions. Existing analysis and predicate-construction tests SHALL continue to pass.

Performance verification SHALL use warmed Release runs over representative typing snapshots, interior cursor edits, large declared metadata levels, and malformed boundary inputs. It SHALL record p50/p95 latency and allocated bytes per request, environment and workload sizes, and enforce deterministic work ceilings in automated tests. For a documented reference-machine workload of at most 4,096 source units and 256 field/value/variable descriptors, p95 SHALL be at most 20 ms and mean allocation at most 256 KiB per request over at least 1,000 measured requests after at least 100 warmups. Boundary/adversarial workloads SHALL verify bounded processing separately rather than weaken those representative thresholds.

#### Scenario: Runnable integration and resolving links
- **WHEN** the documented sample is built and run and README/guide links are checked
- **THEN** configured metadata produces working editor completions and correct diagnostics/range mapping
- **AND** local links resolve and stale backend results cannot overwrite a newer text/cursor snapshot

#### Scenario: Representative typing performance and compatibility
- **WHEN** the specified warmed workloads and existing regression suites run
- **THEN** measured latency/allocation satisfy the representative thresholds, deterministic ceilings pass boundary tests, and existing behavior is preserved
- **AND** the report identifies the machine, runtime, sample counts, and workload sizes
