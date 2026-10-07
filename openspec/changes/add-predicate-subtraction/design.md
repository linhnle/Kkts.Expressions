# Design

## Context

See [proposal.md](proposal.md) for motivation and [the delta spec](specs/predicate-subtraction/spec.md) for the behavior contract.

The library targets `netstandard2.0`; its xUnit/VSTest project targets `net10.0`. `AdditionOperatorParser` recognizes only `+`; Number, Property, String, and Group parsers explicitly select addition after a value. `ExpressionParser.BuildSteps` reduces addition before comparisons using left-to-right list compaction and `ReadBinaryOperands`.

`Addition` prepares constants with natural types, normalizes numeric pairs through `NumericOperands`, and emits `Expression.Add`; it separately supports string concatenation. `Comparison` selects computed-operand handling via `ContainsAddition`, propagated through Group, Not, and Logicality. That path also handles counterpart typing, nulls, variables, and computed membership.

`NumericOperands.TryGetIntegralConstant` recognizes addition-only constant trees using unchecked arithmetic. Its incompatible-pair errors currently say "added." `NumberParser` accepts digits and decimal points, not a leading sign. Negative literals therefore require a narrow grammar extension.

There are no canonical specs yet. The complete, unarchived plus change is the compatibility baseline and is not modified or archived here.

## Goals / Non-Goals

**Goals:**
- Extend the existing additive pipeline rather than create a parallel expression evaluator.
- Preserve native, provider-visible expression trees and shared numeric semantics.
- Make the computed-operand path recognize subtraction-only and mixed expressions.
- Keep syntax errors distinct from operand errors and runtime arithmetic exceptions.

**Non-Goals:**
- Redesign comparison coercion for predicates without arithmetic.
- Add public arithmetic enums or change structured filters.
- Implement general unary expression parsing or arithmetic in arrays/order clauses.
- Guarantee translation by every LINQ provider or add dependencies.

## Decisions

### 1. Generalize the existing additive parser and reduction pass

Rename the internal addition operator parser to an additive operator parser that recognizes `+` and `-` and retains the actual operator token. Replace `GetAdditionParsers` with an additive transition helper and update Number, Property, String, and Group transitions. Keep strings syntactically reachable so unsupported subtraction yields an explicit operand error instead of an unrelated tokenizer failure.

Build both operators in the same `BuildSteps` pass, preserving the existing left-to-right reduction. Dispatch to Addition or a new Subtraction node and retain the operator's source index/character. Separate passes were rejected because they incorrectly group mixed chains; a new general precedence parser is unnecessary for two equal-precedence operators.

### 2. Share operand preparation, not string behavior

Extract the existing natural-type constant/group preparation into a small shared arithmetic helper used by Addition and Subtraction. Subtraction builds each operand once, invokes `NumericOperands.Normalize` with the nodes' `IsConstantValue` flags, and emits `Expression.Subtract`, never `SubtractChecked` or a user-defined method. BuildAsync awaits operands using their existing async node paths.

The numeric helper's supported type guard deliberately excludes Boolean, enum, date/time, TimeSpan, and custom operator types. Do not copy Addition's string branch. Catch only expected operand-construction failures, register `-` in `InvalidOperators`, and wrap with the source-aware node error as Addition does. Evaluation-time decimal overflow is not a parsing failure and is not swallowed.

### 3. Generalize computed-value detection and integral constant handling

Rename internal `ContainsAddition` to `ContainsArithmetic`, propagate it through all current wrappers, and mark both arithmetic nodes true. Rename Comparison's addition-specific operand routine accordingly. Preserve its existing counterpart preparation, numeric comparison normalization, and `in` element typing; do not alter legacy non-arithmetic paths.

Extend `TryGetIntegralConstant` to recognize `ExpressionType.Subtract` as well as Add, applying the expression's actual promoted integral type and unchecked arithmetic. This preserves permissible unsigned constant conversions for nested mixed chains and correctly rejects negative or wrapped results when they are incompatible. Generalize incompatible-pair messages to numeric-operation wording without weakening diagnostics. Leaving the helper addition-only was rejected because subtraction constant subtrees would behave inconsistently with otherwise identical addition subtrees.

### 4. Treat negative literals as tokens, not unary expressions

Allow one leading `-` in `NumberParser` only before numeric content, require contiguous sign and number, and validate with invariant leading-sign plus decimal-point styles. Parser state disambiguates the roles: after a completed value, `-` is an additive operator; when an operand is expected, `-` can start a number. Thus `Price--5` is binary subtraction followed by a signed literal, not decrement.

Use existing natural literal parsing for arithmetic, which already attempts signed `int` and `long` parsing; continue existing property-directed literal typing in non-arithmetic comparisons. Test min-value literals directly rather than implementing positive-token-plus-negation, which would overflow or assign the wrong unsigned type. No sign before a property, variable, or parenthesized expression; no whitespace inside the signed token. Allowing general unary negation was rejected as outside the request.

Array literals use a separate parser: do not broaden their grammar as a side effect. Quoted minus signs remain string content.

### 5. Verify observable results and tree types across existing surfaces

Follow `InterpreterPlusTest` and `ConditionOptionsPlusTest` with subtraction-focused partial test files, reusing or extracting their numeric matrix/entity helpers rather than duplicating a promotion table. Assert expression node kind, promoted/nullable type, compiled value, and failure diagnostics. Cover generic/runtime-type sync/async entry points, condition Where, async-only resolvers, cancellation, mappings, allowlists, nulls, variables, and membership.

Include mixed numeric/string plus chains to prove that grouping and existing concatenation are unchanged. Run addition and nearby parser/culture/cache/condition regressions after shared changes. Update README's v3.0 addition section into additive-arithmetic documentation, revising the "other arithmetic" and "no new signs" limitations only as required.

## Risks / Trade-offs

- [Minus ambiguity and whitespace regressions] -> Test compact/spaced operators, contiguous negative literals, missing operands, quoted text, and unsupported unary forms.
- [Addition-only flags silently bypass normalization] -> Replace every current flag propagation and comparison branch; test subtraction-only roots and both comparison sides.
- [Unsigned constant conversion changes] -> Test typed constant subtrees, negative results, wraparound, nullable pairs, and existing addition matrix expectations.
- [Shared helper refactoring affects concatenation] -> Keep Addition's string branch intact and rerun existing string-function/concatenation tests.
- [Negative literals were previously rejected] -> Document this intentional grammar extension; preserve all previously valid predicates and existing invariant culture rules.
- [Provider translation differs] -> Emit native subtraction and retain provider caveats; in-memory integration tests are not evidence of universal relational translation.

## Migration Plan

No persisted data, dependency, or public API migration is needed. Implement and validate in the existing v3.0 feature line; release numbering is not changed by this plan. Update README alongside the code, without changing the read-only attachment. Rollback removes subtraction and signed-literal support as one unit; applications using the new syntax would then receive explicit parse errors. Leave the completed plus change's artifacts untouched.
