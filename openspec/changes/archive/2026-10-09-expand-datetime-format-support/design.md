# Design

## Context

See [proposal.md](proposal.md) for motivation and [the delta specification](specs/datetime-string-parsing/spec.md) for observable behavior.

- `StringExtensions.ToDateTime` and `ToDateTimeOffset` first call the respective .NET `TryParse` with `CurrentCulture` and `DateTimeStyles.None`, then try a snapshot of the public mutable six-entry format list with the supplied provider or invariant culture.
- Their try counterparts call the throwing counterparts and catch `FormatException`. `Cast` routes string temporal conversion through these methods, while handling nullable blank inputs itself.
- `ExpressionSemanticAnalyzer` separately checks literals using the immutable `ExpressionConversionContext`. Its `RequiresDateDefaults` heuristic treats separator-free dates as context-dependent, so compact date support requires an explicit adjustment, not only another format string. `HasExplicitOffset` already recognizes uppercase `T` plus a signed suffix, and trailing `Z`.
- Existing `ExpressionConversionContextTest` asserts the original six default formats. `ExpressionSemanticAnalysisTest` covers fixed-context determinism, time-only inputs, missing offsets, and invalid dates. `NotInTest` provides an existing multi-entry-point temporal integration pattern.
- The library targets `netstandard2.0`; xUnit tests target `net10.0`. There are no dedicated direct temporal extension tests in the inspected test files.
- The in-flight `add-expression-semantic-analysis` change owns deterministic conversion policy. This change must preserve it and coordinate tightly related analyzer edits without changing or archiving that change's artifacts.

## Goals / Non-Goals

**Goals:**

- Add a bounded machine-format fallback without changing an earlier successful conversion.
- Share immutable machine-format knowledge between runtime conversion and semantic checks.
- Validate actual ticks, kinds, offsets, and diagnostic codes, not just successful parsing.
- Keep the existing public format list and conversion-context snapshots intact.

**Non-Goals:**

- Reordering parsing, honoring the provider in the first attempt, introducing `RoundtripKind`, or changing nullable behavior.
- A new public configuration API, parser dependency, generalized ISO grammar, or legacy JSON/epoch support.
- Reworking exception-based try methods, broad `TryCast` catches, mutable-list thread safety, or per-call custom-format allocations. Those are separate concerns, not necessary for format expansion.

## Decisions

### 1. Append, do not replace or reorder, the runtime fallback

Retain both current attempts byte-for-byte in behavioral terms, then attempt the new invariant parser with `DateTimeStyles.None`. Apply this to both temporal types; the existing try and cast methods inherit support through their current routes.

Alternatives rejected:

- ISO-first parsing could change existing results under non-Gregorian calendars or caller-defined formats.
- Switching the general parser to the supplied provider changes ambiguous date behavior and indirectly changes `Cast`, which supplies invariant culture by default.
- Appending ISO defaults to the mutable public list would expose the guarantees to user removal and change observable defaults.

### 2. Use a small internal shared machine-format parser

Introduce one internal helper in `src/Kkts.Expressions/Internal/` for immutable format definitions, narrowly scoped suffix normalization, typed nonthrowing parsing, and machine-form recognition used by semantic validation. Do not expose format arrays or build a general conversion framework.

Use `TryParseExact` with invariant culture for calendar date forms and both full timestamp layouts. Express the zero-fraction form separately and each fractional width from 1 through 7 explicitly using `f` patterns. This avoids treating a bare decimal point as a guaranteed form or assuming the single `"O"` pattern accepts all JSON timestamps.

Normalize only supported machine timestamp suffixes in this last fallback:

- Translate trailing uppercase `Z` to numeric `+00:00`.
- For a compact full timestamp, translate its validated final signed four-digit offset to `+HH:mm` / `-HH:mm`.
- Leave offset-less values unchanged.
- Parse offset-bearing forms with an actual offset pattern (`zzz`), never a quoted literal `Z`, so UTC is not mistaken for unspecified local time.

Normalization must not strip quotes, truncate fractional seconds, repair invalid dates, or broadly rewrite legacy inputs. Exact parsing still validates calendar values and offset ranges.

Alternatives rejected:

- Relying only on invariant `TryParse` does not define a compact ISO contract.
- A permissive regular-expression date parser duplicates framework validation and adds unnecessary complexity.
- Duplicating the machine-format list in runtime and semantic code risks divergent acceptance.

### 3. Preserve runtime timezone behavior, validate semantics without local conversion

Runtime date/time parsing retains `DateTimeStyles.None`: zoned inputs become local-kind machine-local values; unzoned inputs remain unspecified. Runtime offset-aware parsing retains supplied offsets, or local defaults for unzoned values.

For semantic validation, distinguish full calendar shape and explicit-offset presence from validity:

- Recognize the supported compact calendar shape before applying the existing separator-based date-default heuristic. Invalid full compact dates such as `20260230` must reach calendar validation and produce `incompatible-operand`, not a today-dependent diagnostic.
- Continue using schema culture and its exact-format snapshot for existing validation paths.
- Add the shared invariant machine-format validation fallback.
- Validate machine values with explicit offsets through offset-aware parsing without converting them to local `DateTime`.
- Preserve `context-dependent-conversion` for offset-less offset-aware literals and existing partial/time-only legacy inputs. Do not turn every separator-free string into a full date.

The shared helper receives no ambient culture or mutable public format list. Do not call the public runtime extensions from semantic validation.

### 4. Define guaranteed support without making the API strict

The new fallback implements the documented subset; existing permissive .NET parsing remains first. Unsupported ISO variants may still succeed through legacy parsing. Documentation and tests must not claim global rejection of inputs outside the subset, including fraction lengths that an existing runtime parser may already accept.

JSON support means an extracted string value, not deserializing JSON. Explicit offsets are limited to valid .NET temporal offsets, including the +/-14-hour bounds. No epoch or `/Date(...) /` interpretation is added.

### 5. Validate compatibility and every affected surface

Add focused xUnit temporal-extension tests with shared test data for extended/compact layouts, zero and 1-7 fractional digits, `Z`, positive and negative offsets, and date-only values. Assert ticks, `Kind`, `Offset`, and represented instants. Expected local results must be computed for the represented date, not from today's offset.

Run culture cases under `en-US`, `fr-FR`, and `th-TH` with culture restoration in `finally`. Restore any temporary public-format mutations and serialize tests that mutate that shared list using the repository's xUnit collection conventions or a narrowly scoped nonparallel collection.

Cover current-culture precedence with a conflicting provider, custom exact formats, existing default-format snapshots, nullable casts, null/blank input, invalid leap dates, invalid component ranges, malformed compact offsets, and overprecision rejected by the fallback when legacy paths also reject it.

Add predicate equality and membership tests following existing sync/async and generic/runtime-type patterns. Add semantic tests for compact full dates, valid explicit offsets, invalid dates, missing-offset diagnostics, and ambient-state independence. Do not conflate semantic acceptance with SQL translation.

## Risks / Trade-offs

- [Legacy precedence prevents uniform interpretation of every already-accepted extended date across all cultures] -> Preserve the explicitly selected compatibility policy, document it, and test unchanged legacy results separately from newly accepted compact forms.
- [A literal `Z` pattern would lose timezone meaning] -> Normalize to a real numeric offset and verify local kind and zero-offset behavior.
- [Semantic heuristics reject compact dates before format parsing] -> Recognize full machine calendar shapes before default-dependency checks, with invalid-date diagnostic regression tests.
- [Mutable public format-list tests affect unrelated tests] -> Restore state and isolate mutations; do not silently replace the public list with a cached snapshot.
- [Timezone/DST-dependent expectations are brittle] -> Compute expected conversions for the input instant and run the targeted tests in UTC and a non-UTC timezone as separate processes.
- [The in-flight semantic change may evolve before implementation] -> Integrate with its then-current code and retain its documented deterministic context policy; avoid unrelated semantic fixes.

## Migration Plan

1. Implement the additive helper and runtime fallback, then semantic alignment and focused tests.
2. Update README/API documentation with exact support and compatibility caveats.
3. Build the library's existing target and run the combined focused temporal, conversion-context, semantic, and integration tests; repeat timezone-sensitive cases under UTC and a non-UTC zone.
4. No data migration, public API migration, or package additions are required. Rollback consists of removing the fallback and directly coupled semantic support; callers relying on newly supported compact inputs would again receive existing conversion failures.
