# Spec Delta

## Purpose

Provide documented temporal string conversion for common JSON/ISO and compact ISO inputs while preserving existing culture, timezone, and failure behavior.

## ADDED Requirements

### Requirement: Supported machine-readable calendar formats
Temporal string conversion SHALL accept extended calendar dates (`yyyy-MM-dd`) and compact calendar dates (`yyyyMMdd`). It SHALL accept full extended timestamps (`yyyy-MM-ddTHH:mm:ss`) and compact timestamps (`yyyyMMddTHHmmss`), each with either no fractional seconds or a decimal point followed by 1-7 fractional digits. Full timestamps SHALL support no timezone suffix, `Z`, or a signed numeric offset; extended numeric offsets SHALL use `+HH:mm` / `-HH:mm`, and compact numeric offsets SHALL use `+HHmm` / `-HHmm`. Guaranteed forms SHALL use uppercase `T` and `Z`, four-digit years, and zero-padded components. These forms SHALL be available for both date/time and offset-aware date/time conversion independently of ambient culture when prior compatible parsing stages do not accept the input.

#### Scenario: Extended JSON string values
- **WHEN** extracted JSON string values `2026-10-09`, `2026-10-09T15:26:46`, `2026-10-09T08:26:46.427Z`, and `2026-10-09T15:26:46.1234567+07:00` are converted
- **THEN** each succeeds for both temporal types with its represented calendar components, precision, and the documented timezone rules
- **AND** the API does not require or decode surrounding JSON quotes

#### Scenario: Compact dates and timestamps
- **WHEN** `20261009`, `20261009T152646`, `20261009T082646Z`, `20261009T152646.1+0700`, and `20261009T052646.1234567-0300` are converted under `en-US`, `fr-FR`, or `th-TH`
- **THEN** each succeeds with the same represented Gregorian date and fractional precision subject to the documented timezone rules

#### Scenario: Fractional precision boundaries
- **WHEN** full timestamps with each fractional length from 1 through 7 are converted and are not accepted by earlier legacy parsing stages
- **THEN** each succeeds and retains its representable fractional-second ticks
- **AND** the new fallback does not round or truncate fractions of 8 or more digits

### Requirement: Additive compatibility and precedence
Runtime conversion SHALL preserve current-culture parsing as the first attempt and caller-configurable exact formats as the second attempt, using the supplied provider or invariant culture for that second attempt. The new machine-format support SHALL be an invariant-culture fallback after both existing attempts. Existing accepted inputs SHALL retain their previous results. The public mutable custom-format list, its six default entries, custom additions and removals, public signatures, and nullable casting behavior SHALL remain unchanged.

#### Scenario: Ambiguous culture-specific date
- **WHEN** `01/02/2026` is converted with current culture `en-US` and supplied provider `fr-FR`
- **THEN** the result remains January 2, 2026 because current-culture parsing retains precedence

#### Scenario: Caller-configured exact format wins
- **WHEN** a custom exact format accepts an input that the current-culture parser rejects
- **THEN** that format and its supplied provider determine the result before the machine-format fallback is attempted
- **AND** removing the custom format removes that custom support unless another existing or machine-format path accepts the input

#### Scenario: Nullable casting compatibility
- **WHEN** null, empty, or whitespace input is cast to a nullable temporal type
- **THEN** the result remains null
- **AND** casting the same input to a nonnullable temporal type retains its existing failure contract

### Requirement: Preserve temporal timezone semantics
Offset-bearing date/time conversion SHALL continue to produce machine-local time and local kind, rather than preserving UTC kind or the original offset. Offset-aware conversion SHALL retain an explicit numeric offset and SHALL interpret `Z` as zero offset. Offset-less full date/time values SHALL remain unspecified-kind values, while offset-less offset-aware values SHALL continue to use the machine-local offset for the represented date. Date-only values SHALL represent midnight under these same rules.

#### Scenario: UTC and signed offsets
- **WHEN** equivalent extended and compact timestamps with `Z`, positive offsets, or negative offsets are converted
- **THEN** date/time results equal the represented instant converted to machine-local time and have local kind
- **AND** offset-aware results retain zero offset for `Z` or the supplied signed offset

#### Scenario: No timezone suffix
- **WHEN** `20261009T152646` is converted
- **THEN** date/time conversion returns October 9, 2026 at 15:26:46 with unspecified kind
- **AND** offset-aware conversion uses the machine-local offset for that date and time

### Requirement: Consistent success and failure across conversion entry points
The throwing and try temporal APIs, string casting, and existing synchronous/asynchronous generic/runtime-type predicate conversion SHALL recognize the same new formats. Invalid calendar dates, malformed compact timestamps, and invalid offsets SHALL NOT be repaired by the new fallback. Failure SHALL preserve the existing format-exception contract for throwing temporal conversion and false with the respective minimum-value output for try temporal conversion. This change SHALL NOT add Unix epoch, legacy Microsoft JSON, raw JSON decoding, ISO week-date, or ordinal-date parsers; legacy acceptance outside the guaranteed subset SHALL remain unchanged.

#### Scenario: Invalid new forms
- **WHEN** `20260230`, `20261009T256146Z`, `20261009T152646+1500`, null, empty input, or arbitrary text is passed to temporal conversion and no legacy path accepts it
- **THEN** throwing conversion raises a format exception
- **AND** try conversion returns false with the corresponding minimum value

#### Scenario: Runtime predicate integration
- **WHEN** equality and membership predicates contain a supported compact temporal string for a date/time or offset-aware entity property
- **THEN** synchronous/asynchronous and generic/runtime-type entry points construct predicates with equivalent typed constants and evaluation results

### Requirement: Deterministic semantic validation of the new formats
Semantic literal validation SHALL recognize the new machine formats without depending on current culture, mutable global custom formats, today's date, or machine-local timezone. It SHALL preserve schema-supplied culture and exact-format snapshots and existing context-dependent-conversion diagnostics. A valid compact full calendar date SHALL NOT be misclassified as requiring today's date merely because it lacks date separators. An offset-aware literal without an explicit offset SHALL retain a context-dependent-conversion diagnostic.

#### Scenario: Compact Gregorian date in an editor
- **WHEN** a date/time property is compared with `20261009` or `20261009T152646` using a fixed schema
- **THEN** semantic validation accepts the literal without a context-dependent-conversion diagnostic
- **AND** changing ambient culture or global custom formats does not change the validation result

#### Scenario: Explicit compact offset
- **WHEN** an offset-aware property is compared with `20261009T152646+0700` or `20261009T082646Z`
- **THEN** semantic validation accepts the literal without obtaining a local-time-dependent converted value

#### Scenario: Missing offset and invalid compact date
- **WHEN** an offset-aware property is compared with `20261009T152646`
- **THEN** validation reports context-dependent-conversion
- **WHEN** a date/time property is compared with `20260230`
- **THEN** validation reports incompatible-operand rather than treating the invalid full date as dependent on today's date

### Requirement: Document the supported subset and compatibility policy
User documentation SHALL enumerate the guaranteed extended and compact forms, fractional precision, extracted-JSON-string requirement, culture precedence, and timezone behavior for both temporal types. It SHALL distinguish guaranteed support from permissive legacy parsing and explain that this is not an all-ISO-8601 or legacy Microsoft JSON parser.

#### Scenario: Choosing the correct temporal type
- **WHEN** a caller consults the temporal conversion documentation
- **THEN** examples show compact and extended values and recommend offset-aware conversion when retaining an explicit offset matters
- **AND** documentation states that UTC-kind preservation and provider-precedence changes are outside this change
