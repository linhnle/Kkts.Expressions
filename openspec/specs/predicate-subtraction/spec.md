# Predicate Subtraction Specification

## Purpose

Enable numeric differences in textual predicates with predictable additive precedence, nullable behavior, negative literals, and consistent validation across predicate APIs.

## Requirements

### Requirement: Binary subtraction and additive precedence
The interpreter SHALL accept binary `-` between numeric value operands, including properties, literals, variables, and parenthesized values, with or without surrounding whitespace. Binary `+` and `-` SHALL share one precedence level, associate left to right, and bind before comparisons and logical operators. Parentheses SHALL override grouping. Predicate APIs SHALL continue to require a Boolean result.

#### Scenario: Computed filters
- **WHEN** `Price - Discount >= 100`, `Stock - Reserved > 0`, `Balance - $withdrawal >= 0`, or `(Score + Bonus) - Penalty >= 80` is parsed with suitable numeric properties and resolved variables
- **THEN** the predicate evaluates according to the computed numeric difference and comparison

#### Scenario: Shared left associativity
- **WHEN** `10 - 3 + 2 = 9`, `10 + 3 - 2 = 11`, and `10 - 3 - 2 = 5` are parsed
- **THEN** all three predicates evaluate to true

#### Scenario: Parentheses and both comparison sides
- **WHEN** `10 - (3 + 2) = 5` and `(Stock-Reserved) = (10-3)` are parsed with Stock equal to 10 and Reserved equal to 3
- **THEN** both predicates evaluate to true

#### Scenario: Comparisons, negation, and logical composition
- **WHEN** `Stock - Reserved > 0 and not(Balance - 5 < 0) or Enabled = true` is parsed
- **THEN** the result follows `((Stock - Reserved > 0) and not(Balance - 5 < 0)) or (Enabled = true)`

#### Scenario: Computed membership operand
- **WHEN** `Stock - Reserved in [5,7]` is evaluated with Stock equal to 10 and Reserved equal to 3
- **THEN** the predicate evaluates to true without permitting arithmetic inside the array literal

### Requirement: Numeric promotion and overflow
Subtraction SHALL use the same numeric type set, promotion, and permissible constant conversions as numeric addition, including nullable forms and small integral promotion. Whole-number arithmetic literals SHALL use the first representable type in `int`, `uint`, `long`, `ulong` order; fractional arithmetic literals SHALL use `double`. Incompatible numeric pairs SHALL fail rather than narrow or convert through strings. Integral subtraction SHALL be unchecked, decimal overflow SHALL throw during predicate evaluation, and floating-point subtraction SHALL follow ordinary .NET behavior.

#### Scenario: Mixed numeric operands and expression shape
- **WHEN** an integer property equal to 4 is subtracted from a double property equal to 6.5
- **THEN** the difference has type `double` and value 2.5
- **AND** the returned expression tree represents native numeric subtraction rather than a custom arithmetic method call

#### Scenario: Small integer promotion
- **WHEN** byte properties equal to 100 and 200 are subtracted in that order
- **THEN** the difference has type `int` and value -100, not byte underflow

#### Scenario: Constant conversions with unsigned arithmetic
- **WHEN** `UnsignedLong - (3 - 2) = 4` is parsed with an unsigned-long property equal to 5
- **THEN** the nonnegative integral constant expression is converted consistently with addition and the predicate evaluates to true
- **AND** `UnsignedLong - (2 - 3) = 4` fails explicitly because the negative signed result cannot be converted to unsigned long

#### Scenario: Incompatible numeric pairs
- **WHEN** subtraction combines decimal with float or double, or an unsigned-long property with a signed integral property
- **THEN** parsing fails with an explicit operand error and no predicate result

#### Scenario: Integral overflow and unsigned underflow
- **WHEN** an int property equal to `int.MinValue` is subtracted by 1, or a uint property equal to zero is subtracted by a uint property equal to 1
- **THEN** the differences are `int.MaxValue` and `uint.MaxValue`, respectively

#### Scenario: Decimal overflow
- **WHEN** a successfully parsed predicate subtracts decimal 1 from a decimal property equal to `decimal.MinValue`
- **THEN** evaluating the compiled predicate throws `OverflowException` rather than saturating or substituting a value

### Requirement: Nullable subtraction
Subtraction SHALL promote underlying numeric types and lift the result when either operand is nullable. A numeric null operand SHALL yield a null difference, not zero. A bare null paired with a numeric operand SHALL adopt the promoted nullable numeric type; two bare null operands SHALL fail because no numeric type is available.

#### Scenario: Nullable value and null propagation
- **WHEN** `NullableStock - 1 = 4` and `NullableStock - 1 = null` are evaluated with NullableStock equal to 5 and null
- **THEN** the first predicate is true only for 5 and the second is true only for null

#### Scenario: Bare null on either side
- **WHEN** `Stock - null = null` and `null - Stock = null` are parsed with numeric Stock
- **THEN** both predicates evaluate to true
- **AND** `null - null = null` fails explicitly

### Requirement: Negative numeric literals without general unary negation
A leading minus SHALL be accepted as part of a numeric literal only where a value operand is expected, with the sign adjacent to the numeric token. Binary subtraction SHALL remain distinguishable from this sign, including adjacent minus characters. Negative whole-number literals SHALL use a fitting signed type under the arithmetic literal rules; out-of-range literals SHALL fail explicitly. This change SHALL NOT introduce negation of properties, variables, or groups, unary plus, exponent notation, or numeric suffixes. Existing invariant decimal-point parsing SHALL remain unchanged.

#### Scenario: Subtracting a negative literal
- **WHEN** `Price - -5 = 15`, `Price--5 = 15`, and `Price - (-5) = 15` are evaluated with Price equal to 10
- **THEN** all three predicates evaluate to true

#### Scenario: Signed values in operand positions
- **WHEN** `-5 + 2 = -3`, `Price + -5 = 5`, `Price < -0.5`, or `Price = -5` is parsed with a suitable numeric property
- **THEN** each negative token is treated as a numeric literal using the existing comparison or arithmetic typing conventions

#### Scenario: Signed literal boundaries
- **WHEN** arithmetic literals `-2147483648` and `-9223372036854775808` are parsed
- **THEN** their types are `int` and `long`, respectively
- **AND** a literal below `long.MinValue` fails explicitly

#### Scenario: Unsupported unary forms
- **WHEN** `-Price = 5`, `-$withdrawal = 5`, `-(Price + 1) = 5`, `Price - - = 5`, or `Price - - 5 = 5` is parsed
- **THEN** parsing fails explicitly rather than treating a missing value as zero or introducing general unary negation

### Requirement: API parity and existing validation
Generic and runtime-type synchronous/asynchronous predicate APIs and condition builders using textual `Where` SHALL expose equivalent subtraction semantics. Mappings and allowlists SHALL apply to every referenced property. Variables SHALL retain their resolved numeric types and existing unresolved-variable diagnostics. Async parsing SHALL use async resolution and the supplied cancellation token without synchronous fallback.

#### Scenario: All predicate entry points and condition Where
- **WHEN** `Stock - $reserved > 0` is parsed through each generic/runtime-type sync/async predicate entry point or condition `Where` builder
- **THEN** equivalent inputs yield equivalent Boolean predicates
- **AND** invalid subtraction yields an unsuccessful evaluation result or invalid condition with reported errors and no usable predicate

#### Scenario: Property mapping and validation on both operands
- **WHEN** aliases for Stock and Reserved are mapped to allowed numeric properties
- **THEN** their subtraction succeeds
- **AND** a missing or disallowed property on either side produces existing property diagnostics

#### Scenario: Variables, asynchronous-only resolution, and cancellation
- **WHEN** either subtraction operand contains an unresolved variable
- **THEN** parsing reports existing variable diagnostics
- **AND** async-only resolvers work through async APIs without synchronous resolution
- **AND** cancellation yields an unsuccessful result containing the cancellation error, not a successful predicate

### Requirement: Explicit rejection and compatibility boundaries
Subtraction SHALL reject strings, quoted numeric text, Boolean values, enums, date/time values, durations, and user-defined arithmetic even when those types define subtraction. Malformed syntax and unsupported operands SHALL fail explicitly through existing result diagnostics; operand failures SHALL identify `-` as invalid and include the source location where supported. Existing valid comparison, addition, concatenation, negation, string-function, and membership behavior SHALL remain unchanged. Subtraction SHALL remain textual predicate arithmetic, with no new structured comparison operator, computed ordering, multiplication/division, or arithmetic within `in` array literals.

#### Scenario: Unsupported operands
- **WHEN** `Name - 1 = 0`, `'5' - 2 = 3`, `Timestamp - Timestamp = 0`, `Duration - Duration = 0`, or Boolean, enum, or custom-type subtraction is parsed
- **THEN** parsing fails with an explicit error, an invalid `-` diagnostic, and no predicate result

#### Scenario: Missing operands and non-Boolean root
- **WHEN** `Stock - = 5`, `Stock -`, or `Stock - 1` is passed to a predicate API
- **THEN** the result is unsuccessful with an explicit error and no predicate

#### Scenario: Mixed string addition remains left associative
- **WHEN** `10 - 3 + 'x' = '7x'` and `10 + 'x' - 3 = '7x'` are parsed
- **THEN** the first predicate evaluates to true using existing concatenation
- **AND** the second fails because subtraction receives a string operand

#### Scenario: Literal text and excluded contexts
- **WHEN** `Name = 'a-b'` is parsed
- **THEN** the minus remains literal string content
- **AND** `Stock in [10-3]`, computed ordering, and multiplication/division are not enabled by this change
