# Proposal

## Why

Expression editors need classified source spans and precise syntax-error locations on every text change, including unfinished input. The current predicate API builds typed LINQ expressions and reports syntax positions in exception text relative to trimmed input, making it unsuitable as a direct editor integration contract.

## What Changes

- Add a public, UI-independent expression analysis API with read-only classified source tokens, structured syntax diagnostics, and an explicit completeness flag.
- Classify properties, `$` variables, operators, and constants, with punctuation and unrecognized text identified separately so clients can render the original text without losing delimiters or whitespace.
- Analyze unfinished and malformed input without throwing for expected syntax failures; continue highlighting the entire input and recover to report multiple syntax errors at original-input locations.
- Share grammar knowledge with predicate parsing without resolving variables, reflecting over entity types, building LINQ expressions, or exposing mutable internal parser nodes.
- Document text-change integration, UTF-16 source spans, diagnostic rendering, and safe rendering of user input.
- Preserve existing predicate/build APIs, their validation behavior, and their public diagnostics.

## Capabilities

### New Capabilities

- `expression-editor-analysis`: Syntax-only expression analysis for editor highlighting and positioned diagnostics, including incomplete input and original-text span fidelity.

### Modified Capabilities

None. No main capability specs currently exist; the completed arithmetic and membership changes remain grammar inputs, not requirements to revise.

## Impact

- Public API additions in `src/Kkts.Expressions`, retaining `netstandard2.0` compatibility and introducing no UI framework dependency.
- Internal integration around `ExpressionParser`, `ParsingState`, `ExpressionReader`, and token-specific parsers. Existing internal node spans and parser end indices require normalization rather than direct publication.
- xUnit tests in `src/Kkts.Expressions.UnitTest` for exact token shapes, malformed input, editing sequences, grammar agreement, and predicate compatibility.
- README integration documentation. No new frontend, HTTP endpoint, package dependency, semantic validation service, or arithmetic changes in `NumericOperands` are included.
