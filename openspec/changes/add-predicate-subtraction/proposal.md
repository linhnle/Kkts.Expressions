# Proposal

## Why

Binary subtraction enables computed filters such as available stock, discounted prices, and remaining balances that comparisons and negation alone cannot express. It extends the existing v3.0 predicate addition feature without introducing multiplication or division precedence.

## What Changes

- Add numeric-only binary `-` between properties, literals, variables, and parenthesized values in textual predicates.
- Give `+` and `-` one shared precedence level, evaluated left to right before comparisons and logical operators.
- Reuse addition's numeric promotion, constant conversions, nullable lifting, null propagation, and overflow behavior; emit native subtraction expression trees.
- Expose identical behavior through generic/runtime-type sync/async predicate APIs and condition `Where`, preserving property validation, variable diagnostics, and cancellation.
- Support negative numeric literals, including `Price - -5` and `Price--5`. Discovery found that the current number parser does not accept a leading minus, so this is a prerequisite addition rather than an already-supported behavior to preserve.
- Reject string, date/time, duration, Boolean, enum, and user-defined subtraction explicitly. Preserve existing numeric addition and string concatenation.
- Keep subtraction out of `ComparisonOperator` and structured filter operators. General unary negation, unary plus, multiplication/division, computed ordering, and arithmetic within `in` array literals remain out of scope.
- Document syntax, mixed additive precedence, negative literals, numeric rules, and diagnostics alongside the v3.0 addition documentation.

## Capabilities

### New Capabilities

- `predicate-subtraction`: Numeric binary subtraction, shared additive precedence, negative numeric literals, and consistent textual-predicate API validation.

### Modified Capabilities

None. There are no canonical specs under `openspec/specs/` yet. The completed but unarchived `add-lambda-plus-operator` change is a compatibility baseline, not a canonical capability to modify; its exclusion of other arithmetic describes the previous feature's scope, which this change intentionally extends.

## Impact

- Internal token transitions, number parsing, the additive reduction pass, and arithmetic nodes.
- Shared `NumericOperands` promotion/constant handling and comparison handling currently keyed on `ContainsAddition`.
- Existing interpreter and condition APIs need integration coverage, not new public API signatures or enum members.
- Addition-adjacent xUnit tests, parser regression/culture/cache coverage, and README predicate documentation.
- Library target remains `netstandard2.0`; tests remain `net10.0`. No new dependencies. Query-provider translation remains provider-dependent.
