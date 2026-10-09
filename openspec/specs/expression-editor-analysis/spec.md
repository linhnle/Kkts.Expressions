# Expression Editor Analysis Specification

## Purpose

Enable expression editors to highlight original source text and display structured syntax-error locations while users type, without evaluating expressions or depending on a UI framework.

## Requirements

### Requirement: Public syntax-only analysis
The library SHALL expose a synchronous analysis operation accepting expression text and returning read-only classified tokens, structured syntax diagnostics, and a completeness flag. Analysis SHALL NOT require an entity type, resolve variables, inspect property getters, convert constant values to entity types, or build or execute a predicate. Completeness SHALL mean nonblank input with no lexical or grammar errors, not semantic or Boolean-result validity.

#### Scenario: Unresolved identifiers remain analyzable
- **WHEN** `MissingProperty = $missing` is analyzed without entity metadata or a variable resolver
- **THEN** the property and variable are classified, no syntax diagnostic is produced, and completeness is true
- **AND** no variable lookup or property access occurs

#### Scenario: Syntax is distinct from predicate semantics
- **WHEN** the standalone expression `1 + 2` is analyzed
- **THEN** syntax completeness is true without asserting that the expression can be used as a Boolean predicate

### Requirement: Original-input source spans
Tokens and diagnostics SHALL expose zero-based UTF-16 `Start` and `Length` values into the exact input string, using half-open ranges. Token spans SHALL be positive-length, source-ordered, nonoverlapping, and within input bounds. Whitespace outside tokens SHALL remain unclassified source gaps. Diagnostic spans SHALL be within bounds, and end-of-input diagnostics SHALL use `Start = input.Length` and `Length = 0`. The analyzer SHALL NOT trim or normalize source text for span calculation.

#### Scenario: Exact shape including whitespace
- **WHEN** `  Id = $x  ` is analyzed
- **THEN** the tokens are exactly property `(2, 2)`, operator `(5, 1)`, and variable `(7, 2)`
- **AND** the input is complete with no diagnostics

#### Scenario: UTF-16 offsets after a supplementary character
- **WHEN** `Name = '😀' and Id = 1` is analyzed
- **THEN** the string constant has span `(7, 4)` and the `Id` property has span `(16, 2)`
- **AND** slicing the input by every token span preserves its exact source spelling

### Requirement: Highlight classifications follow the expression language
The analyzer SHALL classify properties, variables, operators, constants, punctuation, and unknown text separately. Dotted property and variable paths SHALL each form one token; variable tokens SHALL include their `$` prefix. Quoted strings including delimiters, numeric literals including an adjacent negative sign when an operand is expected, and unquoted Boolean and null keywords SHALL be constants. Supported comparison, logical, membership, negation, arithmetic, and comparison-function spellings SHALL be operators. Function-call dots, parentheses, array delimiters, and list separators SHALL be punctuation. Keyword recognition SHALL be case-insensitive and culture-independent without changing source spans. Analysis SHALL preserve existing syntax rules rather than introducing additional executable operators or literal forms.

#### Scenario: Nested paths and compound operators
- **WHEN** `Customer.Name not in [$user.name, 'a']` is analyzed
- **THEN** `Customer.Name` is one property token, `not in` is one operator token, `$user.name` is one variable token, and `'a'` is one constant token
- **AND** brackets and the comma are separate punctuation tokens

#### Scenario: Signs and quoted operators
- **WHEN** `Id--5 = 9 and Name.contains('a+b')` is analyzed
- **THEN** the first minus is an operator, `-5` is one constant, and `contains` is an operator
- **AND** `'a+b'` is one constant with no operator token inside it

#### Scenario: Existing alternate syntax
- **WHEN** membership lists using brackets, parentheses, or braces and existing symbolic logical or comparison operators are analyzed
- **THEN** their syntax and classifications agree with the existing expression language
- **AND** list elements remain individually highlightable rather than becoming one opaque list token

### Requirement: Whole-input highlighting with multiple-error recovery
The analyzer SHALL continue token classification through recoverable syntax errors and report multiple independent syntax diagnostics rather than stop at the first error. Every non-whitespace source character SHALL belong to a classified token, including an unknown-text token when no supported token is recognized. Recovery SHALL always make forward progress and SHALL NOT report the input as complete when recovery was used. Diagnostics SHALL be source-ordered, have stable machine-readable codes and nonempty messages, and suppress duplicate reports of the same error at the same span.

#### Scenario: Later clauses retain highlighting and errors
- **WHEN** `Id = ) and Name = ] and IsEnabled = true` is analyzed
- **THEN** distinct syntax diagnostics identify the unexpected `)` at `(5, 1)` and `]` at `(18, 1)`
- **AND** `Name`, `IsEnabled`, and `true` remain classified at their original locations
- **AND** completeness is false

#### Scenario: Unknown character does not hide the suffix
- **WHEN** `Id = 1 # and Name = 'x'` is analyzed
- **THEN** `#` is an unknown token with a positioned diagnostic
- **AND** the suffix contains the classified `Name` property and `'x'` constant

#### Scenario: Quotes prevent false recovery boundaries
- **WHEN** `Name = 'and ]' and Id = )` is analyzed
- **THEN** the quoted text remains a single constant and causes no internal operator or delimiter diagnostics
- **AND** the unexpected final `)` receives a syntax diagnostic

### Requirement: Editing states and structured error positions
Empty and whitespace-only input SHALL return empty token and diagnostic collections with completeness false. Null input SHALL throw an argument-null exception. Expected incomplete or malformed user input SHALL return diagnostics rather than throw. An offending character or token SHALL receive its actual source span; a missing operand, closing delimiter, or closing quote at end of input SHALL receive a zero-length end-of-input diagnostic. An unfinished token SHALL retain its recognizable classification. Unterminated quoted text SHALL extend to end of input without inventing closing delimiters or interpreting its contents as subsequent clauses.

#### Scenario: Blank editor
- **WHEN** an empty string or a string containing only spaces, tabs, and newlines is analyzed
- **THEN** both returned collections are empty and completeness is false without a syntax exception

#### Scenario: Missing operand after trailing whitespace
- **WHEN** `  Id = ` is analyzed
- **THEN** `Id` and `=` remain highlighted and a missing-operand diagnostic has span `(7, 0)`
- **AND** completeness is false

#### Scenario: Unterminated string
- **WHEN** `Name = 'abc` is analyzed
- **THEN** the unfinished string has constant span `(7, 4)`
- **AND** an unterminated-string diagnostic has span `(11, 0)`

#### Scenario: Incremental editing without retained state
- **WHEN** the same caller successively analyzes `Id`, `Id =`, `Id = 1`, and an empty string
- **THEN** each result reflects only its own input and previous results remain unchanged
- **AND** the complete third input has no stale diagnostics

### Requirement: Compatibility and UI integration contract
The new analysis API SHALL be additive and compatible with the library's existing target framework. Existing sync, async, generic, and runtime-type predicate APIs SHALL preserve behavior, variable resolution, precedence, semantic validation, and legacy exception diagnostics. Integration documentation SHALL demonstrate analyzing each text snapshot, mapping all token kinds to client styles, displaying diagnostic spans including zero-length end positions, safely rendering original user text, and treating syntax completeness as distinct from executable predicate validity.

#### Scenario: Analysis does not alter predicate behavior
- **WHEN** existing predicate parsing tests for arithmetic, membership, variables, culture, and invalid syntax are run after analysis integration
- **THEN** existing outcomes and legacy diagnostic expectations remain unchanged

#### Scenario: Consumers can integrate without a UI package
- **WHEN** a client uses the documented text-change integration
- **THEN** it can obtain classifications and error positions using the library alone without installing a frontend framework or UI-specific package
