# Tasks

## 1. Public source-oriented result contract

- [x] 1.1 Add immutable `ExpressionAnalysisResult`, `ExpressionToken`, `ExpressionTokenKind`, and `ExpressionSyntaxDiagnostic` models under `src/Kkts.Expressions`, with owned read-only collections, half-open UTF-16 spans, and stable documented diagnostic codes; verify model tests reject collection mutation and retain independent snapshots, and the library builds for `netstandard2.0`.
- [x] 1.2 Add API/XML documentation for all result fields and token kinds, including the syntax-only meaning of `IsComplete`, original-input offsets, and zero-length EOF spans; verify documented fields match the public models and model tests assert the expected output shape.

## 2. Shared grammar and whole-input classification

- [x] 2.1 Extract shared operator, keyword, identifier, delimiter, and quote/escape definitions from the existing token parsers for use by strict parsing and editor analysis without changing accepted syntax; verify focused existing culture, plus, subtraction, membership, regression, and parser-optimization tests still pass, including legacy failure messages and allocation-growth checks.
- [x] 2.2 Implement a source-preserving lexical pass covering properties, variables, constants, operators, punctuation, and unknown text through the entire input; add token tests for `  Id = $x  `, supplementary characters, tabs/newlines, repeated lexemes, dotted paths, all symbolic aliases, compound `not in`, comparison functions, both quote styles, escapes, Boolean/null keywords, and context-sensitive negative numbers; verify exact kind/start/length tuples and source slices.
- [x] 2.3 Add list-item classification for bracket, parenthesis, and brace membership lists without changing legacy opaque-array conversion or acceptance rules; verify tests highlight each item and separator, including empty lists, collection variables, quoted separators, and escaped closing delimiters, and existing membership tests still pass.
- [x] 2.4 Preserve recognizable unfinished tokens and cover unknown source characters without synthetic source or missing suffix tokens; verify unfinished-string and variable-prefix tests plus token invariants show every non-whitespace source character is covered exactly once and no token is out of bounds.
- [x] 2.5 Document token classification and source slicing in the README editor-analysis section, including compound operators, list punctuation, unknown text, and whitespace gaps; verify every documented example's token slices against the classification tests.

## 3. Recovering syntax analysis and public entry point

- [x] 3.1 Factor syntax acceptance/completion and syntax-related reduction checks into a reusable boundary while preserving the strict predicate path, trimmed legacy positions, and exception construction; verify generic/runtime-type sync/async predicate regression tests and parser cache/optimization tests remain unchanged.
- [x] 3.2 Implement token-based syntax validation with scope-aware recovery, forward progress, source-ordered structured diagnostics, and duplicate/cascade suppression; verify `Id = ) and Name = ] and IsEnabled = true` reports both specified error spans and keeps suffix highlighting, and test unknown characters, invalid operators, nested scopes, list separators, and quoted recovery boundaries.
- [x] 3.3 Wire `Interpreter.AnalyzeExpression(string expression)` to classify the whole snapshot and collect lexical/grammar diagnostics without entity binding, variable resolution, numeric conversion, or LINQ building; verify blank/null behavior, unknown properties/variables, syntax-valid non-Boolean expressions, overflow-sized numeric text, incomplete EOF cases, and exact `(input.Length, 0)` diagnostic spans.
- [x] 3.4 Add snapshot-sequence and parallel-call tests for result independence and no stale diagnostics; verify `Id` -> `Id =` -> `Id = 1` -> empty input produces the specified states without modifying prior results.
- [x] 3.5 Add bounded generated-input and long-input tests for termination, nonoverlapping in-bounds tokens, coverage, in-bounds diagnostics, ordering, and duplicate suppression; verify malformed quotes, delimiters, and long error sequences do not hang or throw expected syntax exceptions.
- [x] 3.6 Add grammar parity tests spanning every supported operator, precedence/grouping, all list delimiters, strings, constants, and variables; verify valid predicate syntax is accepted by analysis and lexical/grammar failures are diagnosed while explicitly excluding entity/type/Boolean-result validation failures from syntax parity.
- [x] 3.7 Complete README text-change integration using the actual public API, all token-kind styling mappings, source-safe rendering, diagnostic range/caret display, and snapshot freshness when consumers schedule analysis; verify the C# example compiles and tests reproduce its valid, incomplete, and multi-error outputs, and documentation requires predicate validation before execution.

## 4. Cross-cutting compatibility validation

- [x] 4.1 Build the library and run the focused analysis and coupled parser tests using the existing test runner; verify the public API remains `netstandard2.0` compatible and contract tests assert exact spans and multiple-error behavior rather than only success flags.
- [x] 4.2 Run the full existing xUnit suite after the shared parser extraction and confirm structured filters, conditions, sorting, async resolution, culture handling, arithmetic, and membership behavior remain unchanged; verify all tests pass and no UI framework or new package dependency was introduced.
