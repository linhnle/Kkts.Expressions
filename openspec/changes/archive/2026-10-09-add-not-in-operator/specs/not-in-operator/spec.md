# Spec Delta

## Purpose

Allow callers to express exclusion from a set consistently in predicate strings, direct predicate builders, and structured filters while preserving existing membership semantics.

## ADDED Requirements

### Requirement: Two-keyword exclusion syntax

Predicate parsing SHALL recognize `not in` as a case-insensitive infix comparison operator. One or more whitespace characters accepted by the query language MUST separate `not` and `in`; token boundaries MUST prevent treating `notin` or partial keywords as this operator. Whitespace around the operator SHALL follow existing comparison syntax.

#### Scenario: Match and non-match
- **WHEN** `Integer not in [1, 2]` is evaluated for integers 1 and 3
- **THEN** the results are false and true respectively

#### Scenario: Case and whitespace
- **WHEN** equivalent queries use `not in`, `NOT IN`, `NoT   iN`, or a tab/newline between the two keywords
- **THEN** all parse successfully and produce identical results, independent of the current culture

#### Scenario: Invalid keyword sequences
- **WHEN** predicates contain `Integer not [1]`, `Integer notin [1]`, `Integer not inside [1]`, or `Integer not in`
- **THEN** parsing fails explicitly rather than producing a predicate

### Requirement: Complement existing membership semantics

For every supported `in` operand pair, `not in` SHALL accept the equivalent operands and return the Boolean negation of `in`. The operator MUST reuse membership conversion and equality semantics, including nullable values, null elements, duplicates, and empty lists. It MUST NOT introduce SQL-style unknown results or implicit null filtering.

#### Scenario: Supported scalar types
- **WHEN** matching and non-matching values are tested for numeric, string, Guid, Boolean, enum, DateTime, DateTimeOffset, and TimeSpan properties, including their supported nullable forms
- **THEN** `not in` returns the opposite result of the equivalent `in` predicate

#### Scenario: Nullable membership
- **WHEN** a nullable integer is null
- **THEN** `NullableInteger not in [1, 2]` is true and `NullableInteger not in [null, 1]` is false

#### Scenario: Empty and duplicate lists
- **WHEN** a value is tested against an empty list or a list with duplicate elements
- **THEN** exclusion from the empty list is true and duplicates do not change the result

### Requirement: Membership variables and arithmetic operands

The operator SHALL support the list elements, collection-valued variables, and computed left operands already supported by `in`. Variable resolution, conversion, property mappings, and property allowlists MUST apply as they do to membership queries.

#### Scenario: Collection variable
- **WHEN** `$excluded` resolves to a supported integer collection containing 1 and 2 and `Integer not in $excluded` is evaluated for 3
- **THEN** evaluation succeeds and returns true

#### Scenario: Variable within a list
- **WHEN** `$blocked` resolves to 2 and `Integer not in [1, $blocked]` is evaluated for 2
- **THEN** evaluation succeeds and returns false

#### Scenario: Computed left operands
- **WHEN** `Integer + 1 not in [2, 3]` and `(Integer - 1) not in [0, 1]` are evaluated for Integer equal to 1
- **THEN** both predicates return false and arithmetic is evaluated before membership

#### Scenario: Mapping and restrictions
- **WHEN** a permitted alias maps to an integer property in an exclusion predicate
- **THEN** the mapped property is used, while an equivalent query referencing a disallowed property fails with property diagnostics

### Requirement: Composition and negation compatibility

Exclusion SHALL have the same precedence as `in` and compose with grouping, logical AND/OR, and existing Boolean negation. Existing `in`, `not(...)`, `!`, and repeated-negation behavior MUST remain unchanged.

#### Scenario: Logical composition
- **WHEN** `Integer not in [1] and IsEnabled` is evaluated for Integer equal to 2 and IsEnabled equal to false
- **THEN** the predicate returns false

#### Scenario: Negating exclusion
- **WHEN** `!(Integer not in [1, 2])` or `not(Integer not in [1, 2])` is evaluated
- **THEN** its result equals `Integer in [1, 2]`

### Requirement: Structured and direct predicate APIs

The public enum SHALL expose `ComparisonOperator.NotIn` without changing existing numeric values. Direct predicate builders SHALL accept that member. Structured filters SHALL recognize `Operator = "not in"` case-insensitively, with leading/trailing whitespace and one or more accepted whitespace characters between the keywords. Filter collections and groups MUST preserve their existing AND/OR rules.

#### Scenario: Direct predicate builder
- **WHEN** a caller builds a predicate using `ComparisonOperator.NotIn` and the same property/list value representation accepted by `ComparisonOperator.In`
- **THEN** the compiled predicate returns the complement of the corresponding `In` predicate

#### Scenario: Structured filter spelling
- **WHEN** filters use `not in`, ` NOT IN `, or `not   in` with `Value = "1, 2"` for an integer property
- **THEN** each yields the same exclusion predicate

#### Scenario: Collections and groups
- **WHEN** exclusion filters are used alongside existing filters in a collection or filter group
- **THEN** the results follow the existing collection-AND and group-OR behavior

### Requirement: Consistency across entry points

Generic and runtime-type synchronous/asynchronous parsing and building APIs SHALL provide equivalent exclusion behavior. Conditions SHALL support exclusion both in `Where` and structured filters, with existing sorting and condition-validation behavior preserved.

#### Scenario: Predicate overload parity
- **WHEN** the same exclusion query is parsed through generic/runtime-type and synchronous/asynchronous overloads
- **THEN** every successful result evaluates identically

#### Scenario: Condition integration
- **WHEN** equivalent exclusion criteria are supplied in condition `Where` or structured filters through generic/runtime-type synchronous/asynchronous condition APIs
- **THEN** each valid condition selects the same records

### Requirement: Explicit failure and cancellation behavior

Malformed exclusion syntax, invalid list values, unresolved variables, and disallowed properties SHALL use the existing failure channels of their entry points. Throwing builders MUST throw; evaluation/try APIs MUST report failure with the applicable existing diagnostics, never a success-shaped fallback. Async cancellation MUST remain cancellation rather than being classified as an invalid value.

#### Scenario: Invalid operand values
- **WHEN** an integer exclusion list contains text that cannot be converted to an integer
- **THEN** evaluation fails with an exception and the same applicable diagnostics as the corresponding `in` query

#### Scenario: Missing variable
- **WHEN** an exclusion query references an unresolved collection or list-element variable
- **THEN** evaluation fails and identifies the unresolved variable

#### Scenario: Cancellation during resolution
- **WHEN** asynchronous exclusion parsing or filter building is cancelled during supported variable resolution
- **THEN** cancellation is surfaced through the existing API contract and is not reported as an invalid value

### Requirement: Native expression-tree output and documentation

Exclusion predicates SHALL emit native expression trees equivalent to negating the existing membership expression, without compiling or invoking intermediate predicates. Documentation MUST explain predicate and structured-filter syntax, supported types, complement/null behavior, and query-provider translation limitations.

#### Scenario: Inspect generated predicate
- **WHEN** the expression tree for a simple exclusion predicate is inspected
- **THEN** its Boolean body negates the membership call and contains no compiled delegate or invocation node

#### Scenario: Documentation examples
- **WHEN** users consult the supported-operator documentation
- **THEN** they can find `Id not in [1, 2]`, a structured `not in` example, and the statement that relational translation depends on the provider
