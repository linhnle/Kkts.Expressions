# Lambda Plus Operator Specification

## Purpose

Allow callers to express numeric sums and concatenated strings inside lambda predicates while preserving existing query validation and evaluation behavior.

## Requirements

### Requirement: Binary plus syntax and precedence
The interpreter SHALL accept binary `+` between value operands, including literals, properties, variables, and parenthesized value expressions, with or without surrounding whitespace. It SHALL evaluate repeated additions left to right, respect parentheses, and bind addition more tightly than comparison and logical operators. It SHALL continue to require a Boolean predicate result.

#### Scenario: Addition on either side of a comparison
- **WHEN** `Integer+1 = 1+Integer` is parsed for an entity with an integer property
- **THEN** parsing succeeds and the predicate evaluates to true for values without overflow

#### Scenario: Parenthesized comparison operands
- **WHEN** `(Integer + 1) = (2 + 3)` is parsed and evaluated with `Integer` equal to 4
- **THEN** parsing succeeds and the predicate evaluates to true

#### Scenario: Left associativity versus explicit grouping
- **WHEN** `1 + 2 + 'x' = '3x'` and `1 + (2 + 'x') = '12x'` are parsed
- **THEN** both predicates evaluate to true

#### Scenario: Comparison and logical precedence
- **WHEN** `Integer + 1 = 5 and Double + 0.5 > 2 or Boolean = true` is parsed
- **THEN** it evaluates like `(Integer + 1 == 5 && Double + 0.5 > 2) || Boolean == true`

#### Scenario: Plus inside quoted text
- **WHEN** `String = 'a+b'` is parsed
- **THEN** the plus character remains part of the string literal

### Requirement: Numeric addition and type promotion
When neither operand is a string, the interpreter SHALL support predefined C# binary numeric addition and promotion rules for numeric operands, including nullable forms. It SHALL reject incompatible numeric combinations rather than narrowing or converting through strings. Whole-number literals SHALL use the first representable type in `int`, `uint`, `long`, `ulong` order; fractional literals SHALL have type `double`. Existing literal syntax and culture conventions SHALL remain unchanged. Numeric overflow SHALL follow ordinary unchecked C# addition, including decimal's normal overflow exceptions.

#### Scenario: Mixed numeric operands
- **WHEN** `Integer + Double = 3.5` is evaluated with `Integer` equal to 1 and `Double` equal to 2.5
- **THEN** parsing succeeds, the sum has type `double`, and the predicate evaluates to true

#### Scenario: Literal-only numeric addition
- **WHEN** `1 + 2 = 3` and `1.5 + 2 = 3.5` are parsed
- **THEN** both predicates evaluate to true with integer and double sums respectively

#### Scenario: Small integral operands
- **WHEN** two byte-valued properties containing 200 and 100 are added and compared to 300
- **THEN** the sum has type `int` and the predicate evaluates to true

#### Scenario: Incompatible numeric combination
- **WHEN** a decimal-valued property is added to a double-valued property
- **THEN** parsing fails with an explicit error instead of coercing either operand

#### Scenario: Integral overflow
- **WHEN** `Integer + 1 = OtherInteger` is evaluated with `Integer` equal to `int.MaxValue` and `OtherInteger` equal to `int.MinValue`
- **THEN** the predicate evaluates to true under unchecked integer addition

### Requirement: Nullable numeric addition
The interpreter SHALL lift numeric addition when either operand is nullable, promoting underlying numeric types before lifting. A numeric null operand SHALL produce a null sum, not zero. A bare null paired with a numeric operand SHALL be treated as that operand's promoted nullable numeric type.

#### Scenario: Nullable value and null
- **WHEN** `IntegerNullable + 1 = 5` is evaluated with values 4 and null
- **THEN** it evaluates to true for 4 and false for null

#### Scenario: Comparing a null sum
- **WHEN** `IntegerNullable + 1 = null` is evaluated with a null property
- **THEN** parsing succeeds and the predicate evaluates to true

#### Scenario: Explicit null numeric operand
- **WHEN** `Integer + null = null` is parsed
- **THEN** parsing succeeds and the predicate evaluates to true

### Requirement: String concatenation
When either operand has string type, binary `+` SHALL concatenate in operand order. Other operands SHALL use ordinary .NET string conversion, with null contributing an empty string and without a custom invariant formatting rule. Numeric-looking quoted literals SHALL remain strings. Concatenation SHALL be usable as a value in supported string comparisons and string-function arguments.

#### Scenario: String property and literal
- **WHEN** `String + '!' = 'hello!'` is evaluated with `String` equal to `hello`
- **THEN** parsing succeeds and the predicate evaluates to true

#### Scenario: Mixed string and number
- **WHEN** `'ID: ' + Integer = 'ID: 7'` and `Integer + ' items' = '7 items'` are evaluated with `Integer` equal to 7
- **THEN** both predicates evaluate to true

#### Scenario: Null string and null literal
- **WHEN** `String + 'x' = 'x'` is evaluated with a null string property and `'x' + null = 'x'` is parsed
- **THEN** both predicates evaluate to true

#### Scenario: Quoted number remains a string
- **WHEN** `'1' + 2 = '12'` is parsed
- **THEN** the predicate evaluates to true rather than calculating a numeric sum

#### Scenario: Computed string comparison operands
- **WHEN** `String + '!' contains 'lo!'` and `String.contains('he' + 'llo')` are evaluated with `String` equal to `hello`
- **THEN** both predicates evaluate to true

### Requirement: Consistent API integration and validation
The generic and runtime-type synchronous/asynchronous predicate APIs and condition-building APIs using a textual `Where` SHALL expose equivalent plus semantics. Every property and variable in an addition SHALL retain existing mapping, allowlist, resolution, and error-reporting behavior. Asynchronous parsing SHALL use asynchronous variable resolution and the supplied cancellation token.

#### Scenario: Property mapping and allowlist
- **WHEN** `alias + 1 = 5` is parsed with `alias` mapped to `Integer` and that property allowed
- **THEN** it succeeds and evaluates to true for `Integer` equal to 4
- **AND** a disallowed property on either side of an addition fails validation through existing property diagnostics

#### Scenario: Variables on both sides of addition
- **WHEN** `Integer + $increment = $target` is parsed with resolved values 2 and 6 and evaluated with `Integer` equal to 4
- **THEN** synchronous and asynchronous parsing produce equivalent predicates
- **AND** an unresolved variable in either operand produces existing variable diagnostics

#### Scenario: Asynchronous-only variable resolver
- **WHEN** an addition predicate uses a resolver whose values are available only asynchronously
- **THEN** asynchronous parsing resolves each operand asynchronously with the supplied cancellation token, without invoking synchronous resolution

#### Scenario: Condition Where integration
- **WHEN** a condition is built with `Where` equal to `Integer + 1 = 5`, synchronously or asynchronously
- **THEN** its predicate evaluates to true for `Integer` equal to 4
- **AND** an invalid plus expression makes the condition invalid with reported errors

### Requirement: Invalid plus expressions and compatibility
Malformed additions and unsupported operand pairs SHALL fail explicitly through existing evaluation results, with no successful predicate result. Syntax and operand errors SHALL identify the relevant source location where existing diagnostics support it. Existing expressions without a binary plus SHALL retain their behavior. Unary plus, other arithmetic operators, user-defined addition, date/time arithmetic, computed ordering, and additions inside `in` array literals SHALL NOT be introduced by this capability.

#### Scenario: Missing operands and unary plus
- **WHEN** `Integer + = 5`, `Integer +`, `+Integer = 5`, or `Integer ++ 1 = 5` is parsed
- **THEN** parsing fails with a syntax error and no predicate result

#### Scenario: Unsupported operand types
- **WHEN** `Boolean + 1 = 2`, `DateTime + 1 = DateTime`, or `null + null = null` is parsed
- **THEN** parsing fails with an explicit operand error and no predicate result

#### Scenario: Non-Boolean root
- **WHEN** `Integer + 1` or `String + 'x'` is passed to a predicate API
- **THEN** the API returns an unsuccessful evaluation result with an explicit error rather than throwing a lambda-cast exception or returning success

#### Scenario: Computed membership operand
- **WHEN** `Integer + 1 in [2,3]` is parsed and evaluated with `Integer` equal to 1
- **THEN** parsing succeeds and the predicate evaluates to true

#### Scenario: Existing grammar is preserved
- **WHEN** existing comparison aliases, logical groups, negation, string functions, null comparisons, variables, and `in` predicates without binary addition are parsed
- **THEN** their results and validation behavior remain unchanged
