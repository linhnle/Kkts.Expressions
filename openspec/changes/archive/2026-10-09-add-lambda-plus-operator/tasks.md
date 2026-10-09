# Tasks

## 1. Numeric addition and parser integration

- [x] 1.1 Add an internal binary addition parser and node; wire number/property/variable/group transitions and grouped comparison right operands in the existing parsers. Add interpreter tests for spaced/unspaced syntax, additions on either comparison side, nested groups, malformed operands, and unchanged quoted plus characters; verify each succeeds or fails as specified.
- [x] 1.2 Integrate left-associative addition reduction into `ExpressionParser.BuildSteps` before comparison consumers, preserving existing negation and logical precedence. Add chain and grouping tests, including floating-point cases that distinguish association; verify evaluated predicates match explicit C# equivalents.
- [x] 1.3 Reuse or extract existing conversion helpers for addition literal typing and predefined C# numeric promotion, including small integral/char, signed/unsigned, float/double/decimal, constant conversions, and nullable lifting. Add a table-driven numeric-pair matrix and literal-boundary tests; verify exact addition expression types, values, rejected pairs, unchecked integral overflow, decimal precision, and decimal runtime overflow.
- [x] 1.4 Extend synchronous/asynchronous comparison construction only where computed addition needs constant/null typing, numeric counterpart normalization, or `in` element typing. Add tests for `1 + 2 = 3`, mixed property sums, nullable sums, `Integer + null = null`, and `Integer + 1 in [2,3]`; verify success, null propagation, and unchanged legacy property/constant comparisons.
- [x] 1.5 Document numeric `+`, precedence, literal typing, nullable behavior, overflow, and excluded arithmetic syntax in the README. Verify the numeric and grouping examples by matching them to passing interpreter tests.

## 2. String concatenation and string comparisons

- [x] 2.1 Extend the addition node's shared construction routine to select standard string concatenation when either operand is string-typed, using appropriate cached string/object overloads. Add tests for literal/property concatenation, both mixed-operand orders, nullable operands, Boolean/date/enum string conversion, culture-sensitive conversion, and quoted numeric strings; verify values match ordinary .NET concatenation.
- [x] 2.2 Wire addition after string operands and inside existing string-function argument scopes, respecting `EndFunction` and quoted delimiters. Add tests for `1 + 2 + 'x'`, `1 + (2 + 'x')`, computed infix string comparisons, and `String.contains('he' + 'llo')`; verify association, closing-parenthesis handling, and existing string-function behavior.
- [x] 2.3 Document concatenation examples, mixed operands, null-as-empty behavior, current-culture conversion, and provider-dependent relational translation in the README. Verify documented expressions correspond to passing compiled-predicate tests.

## 3. Public API parity and validation

- [x] 3.1 Ensure addition and computed comparison async paths build each operand asynchronously once, sharing expression construction with synchronous paths and forwarding the existing cancellation token. Add tests using an async-only resolver and a token-observing resolver; verify no synchronous resolution occurs, operands are not resolved twice, and sync/async predicates agree where both paths are supported.
- [x] 3.2 Preserve mapping, allowlists, nested properties, and variable diagnostics for both addition operands; report unsupported operand combinations with source-position-aware errors and existing operator diagnostics. Add generic/runtime-type sync/async tests for mapped properties, disallowed properties, unresolved variables, unsupported types, and incomplete chains; verify failure results contain no predicate and retain each API's existing forwarded diagnostics.
- [x] 3.3 Reject new non-Boolean plus roots inside the parser's evaluation-result boundary before generic wrappers cast the lambda. Add tests for standalone numeric/string additions through all four predicate entry points; verify an unsuccessful result with an explicit error, not an escaped cast exception, and preserve behavior of existing non-plus inputs.
- [x] 3.4 Add synchronous/asynchronous `ConditionOptions.Where` integration tests for numeric addition, concatenation, and invalid plus expressions, following existing EF Core InMemory patterns; verify correct entity counts and invalid-condition errors without changing structured filters or ordering.
- [x] 3.5 Add README examples for variables and condition `Where` with plus, and document Boolean-root and validation requirements. Verify examples against matching interpreter and condition-options tests.

## 4. Integration verification

- [x] 4.1 Build the unchanged `netstandard2.0` library using `dotnet build src/Kkts.Expressions/Kkts.Expressions.csproj` and run the affected xUnit classes together using `dotnet test src/Kkts.Expressions.UnitTest/Kkts.Expressions.UnitTest.csproj --filter "FullyQualifiedName~InterpreterTest|FullyQualifiedName~InterpreterAsyncTest|FullyQualifiedName~ConditionOptionsTest|FullyQualifiedName~ConditionOptionsAsyncTest"`; verify successful build and all targeted tests pass, escalating to the full test project only if shared-parser regressions or baseline questions require it.
- [x] 4.2 Verify expression-tree shapes in the targeted tests use native addition/conversion and standard concatenation calls, with no compiled nested delegate or invocation fallback; verify direct compilation and existing InMemory query consumers produce the specified outputs.
- [x] 4.3 Run `openspec validate add-lambda-plus-operator --strict`; verify validation passes, every delta scenario has a corresponding automated assertion, and the completed checklist reflects actual implementation and validation.

Validation completed: library build passed with zero warnings/errors; the affected
classes passed, then the full suite passed all 732 tests with
`LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8`. Four existing double/group tests fail under
the machine's default locale, reproduce unchanged on clean HEAD, and pass under
the explicit locale. Strict OpenSpec validation passed.
