# Tasks

## 1. Additive runtime machine-format support

- [x] 1.1 Add the small internal shared machine-format helper described in `design.md`, with invariant exact parsing for extended/compact calendar dates and full timestamps, zero or 1-7 fractional digits, and narrowly scoped `Z`/compact-offset normalization; verify focused helper or public-extension tests assert calendar validity, tick precision, signed offsets, +/-14-hour boundaries, and malformed-input rejection.
- [x] 1.2 Append the helper fallback to `StringExtensions.ToDateTime` and `ToDateTimeOffset` after both existing attempts without altering `DateTimeStyles.None`, public format defaults, exception messages, or signatures; verify new temporal-extension theories pass for all guaranteed forms through throwing, try, `Cast`, and `TryCast` entry points, including nullable targets.
- [x] 1.3 Add compatibility tests for current-culture precedence with a conflicting provider, the six existing default formats, custom format addition/removal and provider use, null/blank inputs, and timezone kinds/offsets; verify tests pass under `en-US`, `fr-FR`, and `th-TH`, restoring cultures and serializing/restoring shared-format mutations.
- [x] 1.4 Document the supported form table, precision, extracted-JSON-string requirement, permissive legacy precedence, and DateTime versus DateTimeOffset timezone behavior in `README.md` and directly related API documentation; verify every documented example is represented by a passing runtime test and unsupported parser families are explicitly excluded.

## 2. Deterministic semantic conversion alignment

- [x] 2.1 Extend semantic full-date recognition to supported compact machine forms before the existing date-default heuristic, and add the shared machine-format validation fallback while retaining schema culture/custom-format snapshots and offset-required policy; verify valid compact dates are accepted, `20260230` reports `incompatible-operand`, and offset-less DateTimeOffset still reports `context-dependent-conversion`.
- [x] 2.2 Add semantic theories for extended/compact temporal forms, explicit `Z` and signed offsets, invalid calendar/component/offset values, and ambient culture/global-format independence; verify `ExpressionSemanticAnalysisTest` and `ExpressionConversionContextTest` pass, including unchanged default-format snapshot and existing time-only diagnostics.
- [x] 2.3 Update the related README semantic-validation guidance with compact literal examples and the missing-offset diagnostic caveat; verify examples agree with semantic tests and do not claim machine-independent runtime conversion or SQL translation guarantees.

## 3. Predicate integration and cross-cutting verification

- [x] 3.1 Add focused equality and membership predicate tests for DateTime, DateTimeOffset, and nullable temporal properties using newly supported compact strings, following the existing sync/async generic/runtime-type entry-point patterns; verify all variants produce equivalent typed constants and compiled evaluation outcomes.
- [x] 3.2 Build `src/Kkts.Expressions/Kkts.Expressions.csproj` for its existing `netstandard2.0` target and run one combined filtered xUnit invocation covering the new temporal-extension and predicate tests plus `ExpressionConversionContextTest` and `ExpressionSemanticAnalysisTest`; verify all pass without new dependencies or target-framework changes.
- [x] 3.3 Repeat the combined timezone-sensitive temporal, semantic, and predicate tests in separate processes with `TZ=UTC` and `TZ=America/New_York`; verify exact kinds/offsets and locally converted instants match input-date expectations, while semantic diagnostics remain identical.
