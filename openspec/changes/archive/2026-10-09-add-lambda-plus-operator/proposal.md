# Proposal

## Why

The expression interpreter can build comparisons and logical predicates, but cannot calculate numeric sums or concatenate strings inside them. Supporting binary `+` lets callers express computed conditions directly instead of adding derived properties or assembling expression trees manually.

## What Changes

- Accept binary `+` between literals, properties, resolved variables, and parenthesized value expressions on either side of a comparison.
- Support numeric addition with C# binary numeric promotion and lifted nullable behavior.
- Support string concatenation when either operand is a string, including mixed operands and null-as-empty behavior.
- Evaluate consecutive additions left to right, before comparisons and logical operators, while respecting parentheses.
- Preserve property mapping, property allowlists, variable resolution, synchronous/asynchronous parity, and existing failure reporting.
- Document supported operands, precedence, null behavior, and examples; add focused correctness and compatibility tests.
- Exclude unary plus, other arithmetic operators, date/time arithmetic, user-defined addition operators, new value-returning parse APIs, computed ordering, and arithmetic inside `in` array literals.

## Capabilities

### New Capabilities

- `lambda-plus-operator`: Binary numeric addition and string concatenation within parsed lambda predicates, including grouping, typing, validation, and synchronous/asynchronous behavior.

### Modified Capabilities

None. The project currently has no main capability specifications.

## Impact

- Parser transitions for numbers, strings, properties/variables, groups, and comparison operands; precedence reduction in `Internal/ExpressionParser.cs`.
- A new internal addition parser and node, operand typing shared with comparison construction where necessary, and existing variable/error infrastructure.
- Existing generic and runtime-type `Interpreter.ParsePredicate` / `ParsePredicateAsync` overloads and `ConditionOptions.Where` consumers; signatures stay unchanged.
- xUnit interpreter and condition-options tests, test fixtures where needed, and the README operator documentation.
- No new packages or framework changes. The library remains `netstandard2.0`; tests remain `net6.0`. Expression trees use standard addition/conversion and string concatenation operations, without compiling nested delegates. Relational provider translation is not guaranteed.
