# Proposal

## Why

`StringExtensions.ToDateTime` accepts common JSON/ISO timestamps indirectly through culture-sensitive .NET parsing, but its explicit fallback contains only six date-only patterns and does not define compact ISO support. A documented, tested machine-format fallback will expand supported inputs without changing existing culture precedence or timezone conversions.

## What Changes

- Guarantee common extended ISO/JSON string values: calendar dates and full timestamps with no fraction or 1-7 fractional digits, optionally ending in `Z` or a colon-separated numeric offset.
- Add compact ISO calendar dates (`yyyyMMdd`) and full timestamps (`yyyyMMddTHHmmss`), including the same fractional precision and optional `Z` or compact numeric offsets (`+HHmm` / `-HHmm`).
- Preserve the existing runtime sequence: current-culture parsing, then caller-configurable exact formats with the supplied provider, then the new invariant machine-format fallback.
- Preserve `DateTimeStyles.None`, existing local-time conversion for offset-bearing `DateTime`, and offset retention for `DateTimeOffset`.
- Apply the same additions to the throwing and try APIs for both temporal types, and their existing `Cast`/predicate consumers.
- Keep semantic literal validation aligned for the new machine formats without introducing ambient-culture or mutable-global dependencies.
- Document the supported subset and add focused format, compatibility, error-contract, and integration tests.

## Capabilities

### New Capabilities

- `datetime-string-parsing`: Bounded ISO/JSON and compact ISO parsing support with explicit compatibility, timezone, error, and semantic-validation contracts.

### Modified Capabilities

None. Existing main specifications do not define temporal string conversion. The in-flight `add-expression-semantic-analysis` change defines deterministic semantic conversion; this proposal preserves that policy rather than replacing it.

## Impact

- Runtime implementation: `src/Kkts.Expressions/StringExtensions.cs` and a small internal shared machine-format helper if needed for reuse.
- Semantic validation: `src/Kkts.Expressions/Internal/ExpressionSemanticAnalyzer.cs`; preserve `ExpressionConversionContext` snapshots and existing diagnostic policies.
- Tests: new focused coverage under `src/Kkts.Expressions.UnitTest/Units/`, plus the existing conversion-context and semantic-analysis tests and predicate integration coverage.
- Documentation: `README.md`, and directly related API documentation where appropriate.
- No new packages, public signatures, target-framework changes, or changes to the six public default custom formats. The library remains compatible with `netstandard2.0`.
- Non-goals: legacy Microsoft JSON `/Date(milliseconds)/`, Unix epochs, raw JSON decoding, all ISO 8601 variants, provider-precedence changes, UTC-kind preservation changes, and unrelated parsing optimizations.
