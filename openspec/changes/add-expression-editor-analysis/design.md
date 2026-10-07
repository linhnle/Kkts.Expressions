# Design

## Context

See [proposal.md](./proposal.md) for motivation and the [capability delta](./specs/expression-editor-analysis/spec.md) for behavior.

The repository is a `netstandard2.0` library with xUnit tests targeting `net10.0`, console examples, and no UI application. The user selected a UI-independent analysis API with documentation, then explicitly selected whole-input highlighting and multiple-error recovery.

Observed implementation constraints:

- `Interpreter.ParsePredicate` validates input and delegates to `ExpressionParser`, which couples private `ParsingState` processing to node reduction and typed expression building.
- `ExpressionReader` trims its input and advances a character index; its current index convention and token parser end indices are not directly suitable as half-open editor spans.
- `ParsingState.CloseGroup` and `Complete` throw `FormatException` with a textual index. Node reduction has additional syntax failures, while node building also reports semantic failures.
- Internal nodes retain start indices but not reliable complete lexical ranges. Variables become `Constant` nodes, and bare identifiers can depend on property validation. Publishing those nodes would expose mutable internals and conflate syntax with semantics.
- `ArrayParser` consumes list text as an opaque value and supports `[]`, `()`, and `{}` delimiters. Editor highlighting must additionally expose individual list items and separators without changing existing value-conversion rules.
- Current grammar includes comparison functions, symbolic aliases, `not in`, binary plus and minus, negative literals, nested paths, Boolean/null keywords, and quote/escape handling. `NumericOperands` implements evaluation and promotion, not editor lexing, and remains unchanged.

## Goals / Non-Goals

**Goals:**

- Stable, immutable source-oriented results usable by any UI.
- Whole-input classification even when validation needs recovery.
- Shared language definitions and syntax validation rather than a divergent editor grammar.
- Explicit separation between syntax diagnostics and typed predicate validation.

**Non-Goals:**

- A public AST, semantic property validation, variable completion, execution, or HTTP endpoints.
- A new UI, frontend dependency, incremental parse cache, background scheduler, or fixed UI latency SLA.
- Changing existing predicate syntax or legacy exception text.

## Decisions

### 1. Add a dedicated snapshot analysis API

Add `Interpreter.AnalyzeExpression(string expression)` returning `ExpressionAnalysisResult`, with:

- `IReadOnlyList<ExpressionToken> Tokens`.
- `IReadOnlyList<ExpressionSyntaxDiagnostic> Diagnostics`.
- `bool IsComplete`, true only for nonblank syntax-valid input.

`ExpressionToken` exposes `ExpressionTokenKind Kind`, `int Start`, and `int Length`. Token kinds are `Property`, `Variable`, `Operator`, `Constant`, `Punctuation`, and `Unknown`. `ExpressionSyntaxDiagnostic` exposes `string Code`, `string Message`, `int Start`, and `int Length`. Initial codes cover unexpected tokens, unknown text, missing operands, unmatched delimiters, and unterminated strings. Document exact code values with the API implementation and test their stability.

Use sealed classes with get-only properties and defensively owned read-only collections compatible with `netstandard2.0`; do not introduce records or framework-specific source-span types. The caller already owns the source, so token text need not duplicate string slices.

Null is a programmer error and throws `ArgumentNullException`. Blank text is a normal empty editor state. Syntax errors are explicit diagnostics; unexpected implementation failures are not broadly caught and converted into valid-looking results.

**Alternative rejected:** extend `EvaluationResult` or publish internal nodes. Those shapes describe typed evaluation, have semantic dependencies, and do not retain the exact source needed for highlighting.

### 2. Lex the full original snapshot, then validate with recovery

Use a source-preserving lexical pass to produce all tokens and lexical diagnostics before grammar validation. Extract shared operator/keyword, delimiter, identifier, and quote/escape definitions from existing parsers where necessary and make existing parsers consume the same definitions without changing their behavior. Keep normalization limited to recognition; spans always reference raw source.

Use longest supported operator matching with existing context-sensitive rules: `not in` spans its two keywords and intervening whitespace, a negative sign belongs to a number only where an operand is expected, and comparison-function names are operators separate from dots and parentheses. Dotted identifiers remain whole property or variable paths. Recognize reserved constants before identifiers and never reinterpret string contents as operators. Unknown characters form bounded unknown tokens with diagnostics.

List delimiters and elements require a source-oriented view in addition to the legacy opaque array value. Honor the existing list and escape grammar; classify contents without converting values or adding new semantic validation rules.

**Alternative rejected:** projecting only a successfully built AST. It cannot highlight suffixes after a syntax failure, loses list-item boundaries, and requires type binding. A wholly independent regex grammar is also rejected because it would drift from predicate syntax.

### 3. Extract a shared syntax boundary without changing strict parsing

Factor reusable syntax acceptance/completion rules out of `ExpressionParser.ParsingState` and syntax-related reduction checks. Keep the current strict predicate path fail-fast, with its existing exception construction and trimmed reader behavior. The analysis path consumes original-text tokens and collects structured errors without invoking `BuildArgument`, property metadata, variable resolution, or expression building.

Do not globally remove `ExpressionReader.Trim()` or reinterpret legacy indices. Use a distinct source-preserving reader mode or analysis cursor, and normalize editor spans from explicit token boundaries instead of copying legacy `CurrentIndex` or `EndIndex`.

Any extraction must preserve strict parser buffer reuse and cached results. Add compatibility tests for the existing allocation-growth regression rather than enabling editor token allocation during ordinary predicate parsing.

**Alternative rejected:** parse positions out of `FormatException.Message`. This is brittle, reports trimmed locations, stops after one error, and cannot distinguish all syntax failures from semantic errors.

### 4. Recover locally with deterministic forward progress

Validation tracks expected operand/operator state and delimiter scopes using shared syntax rules. On an unexpected token, record its original span and advance at least one token. Synchronize at a logical clause boundary, an appropriate closing scope, or a list separator within a list, skipping only validation work, never lexical highlighting.

Maintain delimiter context across recovery and suppress immediate follow-on reports caused solely by the same rejected token. Resume checking subsequent independent clauses so `Id = ) and Name = ] and IsEnabled = true` reports both offending delimiters while highlighting every clause. Merge lexical and grammar diagnostics in source order and deduplicate identical code/span reports. Do not guarantee a minimum number of diagnostics for arbitrary broken text, but do guarantee detection of independently recoverable errors in the specified scenarios.

End-of-input missing constructs use `(input.Length, 0)`. An unfinished quote consumes the remaining text; recovery must not pretend to see clauses inside it. Missing-closing-scope diagnostics can share an EOF location when they represent different unmatched scopes, but duplicate cascades for the same scope must be suppressed.

**Alternative rejected:** stop after the first syntax error. The user explicitly chose whole-input highlighting and multiple-error recovery. Attempting arbitrary synthetic repairs is also rejected: analysis must not suggest recovered text is valid or executable.

### 5. Leave text-change scheduling and rendering to consumers

Analyze each complete snapshot synchronously with no shared mutable state or persistent cache. UI callers choose whether to debounce or schedule work; callers scheduling asynchronously apply a result only when its input snapshot is still current. Document mapping kinds to styles, preserving whitespace gaps, HTML/text-node escaping, UTF-16 offsets, and caret rendering for EOF diagnostics. Do not provide generated HTML or CSS in the analysis result.

**Alternative rejected:** library-managed UI callbacks or a frontend sample application. These introduce dependencies outside the confirmed scope.

## Risks / Trade-offs

- [Recovering validation diverges from strict grammar] -> Extract shared rules, retain strict mode, and add parity cases for every supported operator and expression shape, distinguishing syntax failures from semantic ones.
- [Legacy index conventions corrupt editor offsets] -> Test exact original-input tuples, leading/trailing whitespace, multiline inputs, surrogate pairs, escape sequences, and EOF spans.
- [Opaque arrays hide list syntax or alter validation] -> Classify list contents separately while keeping existing array conversion and acceptance rules intact; test all supported delimiters and variable/list forms.
- [Recovery generates cascades or loops] -> Require token progress, scope-aware synchronization, duplicate suppression, and bounded generated-input tests.
- [Extra allocation degrades existing parsing] -> Allocate editor tokens only in the analysis API; rerun existing parser allocation tests.
- [Whole-snapshot analysis is expensive for large text] -> Keep traversal bounded and avoid rescanning suffixes per diagnostic; consumers control debounce. Incremental caching and timing guarantees are deferred.
- [Syntax validity is mistaken for executability] -> Name the flag `IsComplete` and document its syntax-only meaning; require existing predicate validation before executing user expressions.

## Migration Plan

1. Add immutable public result models and shared lexical/syntax helpers behind the new analysis entry point.
2. Integrate recovering validation while retaining the existing strict predicate path.
3. Add contract and compatibility tests and README text-change integration guidance.
4. Validate the library build and focused tests, then run the full existing suite because the shared parser boundary changes.

No consumer migration is required. Rollback removes the additive analysis API and helper integration; no persisted data or UI state migration is introduced.
