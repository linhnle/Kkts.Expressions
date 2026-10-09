# Design

## Context

See [proposal.md](./proposal.md) for motivation and [the delta spec](./specs/lambda-plus-operator/spec.md) for the behavior contract.

The library targets `netstandard2.0`. Its xUnit test project targets `net6.0` and uses EF Core InMemory. `ExpressionParser` maintains candidate parsers, builds grouped parser chains, and reduces nodes through ordered `BuildSteps`: values, groups/negation/string functions, comparisons, logical AND, and remaining logical operations.

`NumberParser`, `StringParser`, and `PropertyParser` currently choose subsequent operators using `LeftHand`; comparison right operands cannot start with a group. `GroupParser` closes with comparison/logical transitions. String-function arguments use `EndFunction` to stop at the closing parenthesis. These transitions all need coordinated changes rather than adding `+` to the comparison operator table.

Numeric constants currently retain raw text with no type until comparison construction provides one. `Comparison` mainly infers types from a direct property/constant pair; computed values need a scoped extension to this logic. `Constant` resolves variables in distinct synchronous/asynchronous paths. `Node.BuildAsync` otherwise delegates to synchronous construction unless overridden.

Public parse methods build Boolean predicates only. Generic wrappers cast the resulting lambda, so a non-Boolean plus root must be rejected inside the parser's existing result-producing error boundary. Both condition builders pass textual `Where` directly to `ExpressionParser`.

## Goals / Non-Goals

**Goals:**
- Extend existing parser/node patterns without introducing a second expression engine.
- Preserve native expression-tree structure, source diagnostics, and current validation infrastructure.
- Share operand normalization and addition construction between sync and async paths.
- Limit changes to legacy constant inference and comparison construction to cases involving computed addition.

**Non-Goals:**
- Replacing the entire grammar or changing legacy comparison conversion semantics.
- General method invocation or adding dot-method receivers on arbitrary computed groups.
- Guaranteeing translation by every database provider or introducing provider-specific logic.
- Adding numeric signs, exponent/suffix syntax, or other numeric-literal grammar extensions.

## Decisions

### 1. Add a dedicated binary operator parser and node

Introduce `AdditionOperatorParser` and a node such as `Addition`, with left/right operands and source position. Keep `+` separate from `Interpreter.ComparisonOperators` and structured filter comparison enums.

Allow addition transitions after numeric/string/property/variable values and closed value groups, on either side of comparisons. Addition's right operand can begin with a literal, property/variable, or group, but not another plus. Permit grouped comparison right operands. Carry `LeftHand` and function-scope termination consistently through addition and nested groups. In string-function bodies, allow plus before applying the existing `EndFunction` stop behavior.

Do not add arithmetic to array elements or ordering syntax. Existing literal quoting and escaping stay in `StringParser`; a plus inside quotes is never an operator.

**Alternative:** treating plus as a comparison would conflate value computation with Boolean operations and fail precedence and structured-filter semantics.

### 2. Reduce additions left to right before comparisons

Add an addition reduction step after value/group construction and before consumers of computed values. Build each closed group recursively. Reduce adjacent addition chains into a single retained node, preserving parser-chain invariants for comparisons, functions, and logical operators.

The current reducer increments indexes and removes consumed parsers, so chained additions need an explicit loop/revisit strategy that does not skip the next plus. Ensure string-function argument groups finish their internal additions before constructing their enclosing comparison.

Preserve existing negation and logical precedence; do not incidentally change how expressions without plus are reduced.

**Alternative:** splitting the input on `+` would mishandle quotes, nested groups, diagnostics, and existing parser transitions.

### 3. Apply scoped operand typing and C# numeric promotion

Extract or reuse internal typing/conversion helpers after checking existing extensions. A literal participating in addition gets its own numeric type: whole numbers choose the first fitting `int`, `uint`, `long`, or `ulong`; fractional numbers use `double`, with existing culture parsing. Quoted constants retain string type. Do not turn resolved numeric variables into strings or allow an enclosing comparison to reinterpret a quoted addition operand.

Resolve properties and variables through existing infrastructure, determine the predefined numeric common type, and insert explicit `Expression.Convert` operations before `Expression.Add`. Cover small integral/char promotion, signed/unsigned combinations, constant-expression conversions permitted by C#, floating-point promotion, and decimal incompatibility with float/double. Reject unsupported types before expression construction; do not accept user-defined addition accidentally.

For nullable operands, promote underlying types first and convert both operands to the resulting nullable type. Bind a bare null to a compatible nullable numeric type; reject `null + null` because no numeric or string type can be determined. `Expression.Add`, rather than `AddChecked`, provides ordinary unchecked integral behavior and normal decimal overflow behavior.

After constructing a computed value, normalize its comparison counterpart as needed: type an untyped comparison constant from the computed expression, lift a null comparison operand, and type `in` array elements from the computed left value. Normalize mixed numeric comparison operands involving addition without changing unrelated legacy property/constant branches.

**Alternative:** converting all operands to double would lose integer/decimal precision and nullable semantics. Assigning every addition literal its neighboring property's type would defeat the user-selected C# promotion rules.

### 4. Use standard string concatenation calls

Select concatenation whenever either operand has string type. Use cached `string.Concat(string, string)` metadata for two string operands and the appropriate standard object overload with boxing when an operand is non-string. This provides .NET conversion and null-as-empty behavior without eagerly evaluating entity properties or using a custom formatting helper.

Concatenation uses the same left-associative node structure as numeric addition: `1 + 2 + 'x'` produces `3x`, while `1 + (2 + 'x')` produces `12x`. Grouped and infix string comparisons consume the resulting string expression; existing property/literal dot-function syntax supports computed arguments.

**Alternative:** inserting arbitrary `ToString` calls complicates nullable handling and diverges from standard concatenation behavior. Requiring two strings conflicts with the selected mixed-operand scope.

### 5. Share construction while preserving asynchronous resolution

Implement `Addition.Build` and `Addition.BuildAsync`; each builds/resolves children through its corresponding path and passes expressions to one shared normalization/construction routine. Resolve each operand once per build, retain evaluation order, and pass the existing `BuildArgument` cancellation token through variable resolution.

Integrate computed-value comparison typing into both `Comparison.Build` and `BuildAsync`, reusing a helper instead of maintaining separate promotion rules. Do not rely on `Node.BuildAsync`'s synchronous default for a node with asynchronously resolved descendants.

**Alternative:** building synchronously after awaiting only some variables would break async-only resolvers and parity.

### 6. Keep failures within existing evaluation contracts

Syntax errors retain parser source indexes. Unsupported operand errors identify the plus node using existing node error formatting and record the operator through existing `BuildArgument` diagnostics. Property and variable failures continue through the current validation lists; do not substitute zero, empty strings, or successful predicates for failed resolution.

Validate the Boolean root before producing a successful predicate lambda, within the parser's existing exception-to-`EvaluationResult` boundary. Restrict this compatibility fix to preventing new plus expressions from escaping as invalid generic lambda casts.

Generic wrappers currently omit copying `InvalidValues`, unlike the runtime-type result path. Do not expand this change into an unrelated diagnostics redesign: explicit exceptions and the diagnostic fields already forwarded by each API must work for plus failures.

**Alternative:** allowing raw expression-construction or wrapper cast exceptions to escape would make equivalent public entry points behave inconsistently.

## Risks / Trade-offs

- [Parser flags encode operand position and function scope] -> Test no-space syntax, right-hand groups, nested groups, function arguments, and chains; retain existing grammar regression coverage.
- [Legacy constants depend on comparison context] -> Limit new typing rules to addition trees and their immediate comparison consumers; assert unchanged property/constant predicates.
- [C# numeric promotion has signed/unsigned and constant exceptions] -> Use table-driven tests for every supported underlying numeric pair, boundary literals, invalid pairs, nullable variants, and exact expression result types.
- [Mixed string concatenation may not translate to SQL] -> Emit standard calls, verify compiled predicates and existing InMemory consumers, and document that relational translation remains provider-dependent.
- [String conversion and fractional parsing are culture-sensitive] -> Keep ordinary .NET/current repository behavior and test under explicit cultures without globally leaking culture changes.
- [Async paths can accidentally resolve variables synchronously] -> Include an async-only resolver, unresolved-variable tests, cancellation forwarding, and sync/async output parity.
- [Evaluation-time overflow differs by numeric type] -> Verify unchecked integral wraparound and normal decimal overflow; do not misreport runtime arithmetic as a parse failure.

## Migration Plan

No configuration, API, package, framework, or data migration is required. Ship the parser/node changes with tests and README documentation. Existing inputs remain compatible; previously invalid binary-plus inputs become valid. Rollback consists of reverting the implementation and documentation change; callers relying on new plus syntax would then receive syntax failures.
