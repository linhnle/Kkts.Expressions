# Expression autocomplete

Completion metadata is synchronous, UI-independent, and advisory. It reuses the
entity schema, declared variable types, and query permissions used by semantic
analysis; it must never resolve values or execute a predicate. Runtime predicate
construction remains the final validation boundary. See
[semantic analysis](semantic-analysis.md),
[query policies](query-policies.md), and
[public expression fields](expression-field-mapping.md).

The core has no editor, HTTP, or database dependency. The isolated
[CodeMirror sample](../examples/Kkts.Examples/Kkts.Examples.Autocomplete/README.md)
demonstrates backend integration and contains reproducible performance tooling.

## API

```csharp
Interpreter.CompleteExpression(
    string expression, int cursorPosition, ExpressionSchema schema,
    ExpressionVariableSchema variables = null,
    ExpressionValueSuggestionSchema valueSuggestions = null,
    ExpressionCompletionOptions options = null);

queryContext.CompleteExpression(
    string expression, int cursorPosition,
    ExpressionVariableSchema variables = null,
    ExpressionValueSuggestionSchema valueSuggestions = null,
    ExpressionCompletionOptions options = null);
```

The context overload automatically uses its policy and additional field
allowlist. The schema overload uses schema permissions with no additional query
policy limits. Neither accepts a resolver. Null required arguments throw
`ArgumentNullException`; a cursor outside `[0, expression.Length]` or between a
surrogate pair throws `ArgumentOutOfRangeException`. Blank or malformed text is
an editing state, not a configuration exception.

With the metadata declared below:

```csharp
var result = Interpreter.CompleteExpression(
    "Status = ", 9, schema, valueSuggestions: hints);
var active = result.Items.First(item => item.InsertionText == "'Active'");
var edited = "Status = ".Substring(0, active.Start) + active.InsertionText +
    "Status = ".Substring(active.Start + active.Length);
```

Import `System.Linq` for `First`. `edited` is `Status = 'Active'`. Always run
semantic analysis and the normal runtime predicate checks on accepted text;
completion is not execution permission or a guarantee about runtime values.

## Item and result contracts

`ExpressionCompletionItem` contains:

| Property | Meaning |
|---|---|
| `Kind` | Field, Operator, Value, Variable, LogicalOperator, or Delimiter |
| `Label` | Display label, separate from the accepted spelling |
| `InsertionText` | Exact source text to insert |
| `Start`, `Length` | Zero-based UTF-16 replacement span in the original input |
| `Description` | Non-null public description (possibly empty) |
| `TypeInfo` | Existing `ExpressionTypeInfo`, or null for an untyped construct |

Type information describes the public field's result or a contextual value,
not the members inside a selector. It can include nullability and collection
element type. All contracts and returned collections are immutable snapshots.

`ExpressionCompletionResult` distinguishes:

- `Available`: proven eligible items are present.
- `NoMatches`: a reliable context has no matching candidates.
- `ContextUnavailable`: context is too ambiguous to offer reliable suggestions.
- `LimitExceeded`: a source, policy, or fixed work budget prevented completion.

`Diagnostics` uses the existing structured diagnostic model. `IsIncomplete`
means eligible results or work were omitted, not that the expression is invalid.
Completion availability is not syntax completeness, semantic validity, or a
promise that an EF Core provider can translate the eventual predicate.

The result-size option has a default of 50 and accepts 1 through 200:

```csharp
using Kkts.Expressions;

var options = new ExpressionCompletionOptions(maxResults: 50);
```

Other result limits throw `ArgumentOutOfRangeException`.

## Application-declared value hints

Hints are owned literal data, not callbacks, runtime objects, or constraints.
Bind them to the exact immutable entity schema snapshot that will be used for
completion. Registered field names, not display labels or internal mapping paths,
are the keys:

```csharp
using System.Collections.Generic;
using Kkts.Expressions;

var schema = new QuerySchema<Product>()
    .Field("Status", product => product.Status)
    .Field("Price", product => product.Price)
    .Build();

var hints = new ExpressionValueSuggestionSchema(schema, new[]
{
    new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>(
        "Status",
        new[]
        {
            new ExpressionValueSuggestion(FilterValue.String("Active")),
            new ExpressionValueSuggestion(FilterValue.String("Pending")),
            new ExpressionValueSuggestion(FilterValue.String("Closed"))
        })
});

public sealed class Product
{
    public string Status { get; set; } = "";
    public decimal Price { get; set; }
}
```

The metadata does not make these choices exhaustive: `Status = 'Unlisted'`
still follows ordinary semantic and runtime rules. Do not use hints as an
authorization boundary or allowed-value validator.

Supported hint representations are `FilterValue.Null`, `Boolean`, `Number`,
and `String`. Enum names, dates, Guids, and durations use explicit compatible
string literals. Arbitrary objects, variable references, collections, and
application formatting functions are not accepted.

Binding throws explicit argument exceptions for null declarations, nonexistent
field names, duplicate case-insensitive field keys, incompatible values, or
literal representations that cannot round-trip through the current expression
grammar. Input collections are copied; later changes cannot alter metadata.
Hints are bound to the original schema snapshot, not a subsequently rebuilt
schema with coincidentally similar field names.

Hints can have optional labels and descriptions. Repeated equivalent hints
retain their declarations; completion deduplicates them and uses the first
declared presentation rather than interpreting repetitions as multiple values.
Restricted fields' hints must be filtered before exposing any item metadata.

## Literal representation limitations

The expression grammar escapes the active quote only; it does not use JSON or
C# backslash escape rules. A hint supplies literal content, not prequoted text.
The codec checks that its encoded representation decodes to the same content
through both source analysis and the runtime string parser.

Exponent-form or otherwise lossy number tokens are rejected rather than
rounded into a different suggestion. A string with a trailing backslash cannot
be represented losslessly by the current grammar and is rejected explicitly.
The feature does not introduce new escape syntax.

Null hints can bind for existing comparison null-lifting semantics even when a
field is a non-nullable CLR value type. Membership slots and actual operator
contexts must independently apply their stricter existing compatibility rules.
A string beginning with `$` is a literal in an ordinary quoted operand, but
is ambiguous in the existing membership grammar and must not be offered there.

## Metadata compatibility

Shared semantic binding now provides completion-only operand and operator
probes without compiling predicates or resolving variables. It preserves the
distinct rules for direct field comparisons, arithmetic, and membership:

- A declared Double variable can be contextually converted for a direct
  comparison with a Decimal field. Overflow or an unavailable actual value
  remains a runtime failure, not a reason to execute a resolver during editing.
- Arithmetic uses natural variable types and existing numeric promotion, so
  that same Decimal/Double combination is not made valid in arithmetic.
- A declared String variable can be a scalar numeric membership-list item;
  whether its actual text converts remains a runtime check. The direct
  comparison runtime path preserves strings rather than converting them to
  numbers.
- Whole collection variables require the established exact element-type
  compatibility. A collection is not a scalar list item.

The implementation observes existing syntax-parser transitions without
finishing or consuming the parser chain. Supported NOT syntax remains `!` or
`not (...)`; completion does not add a bare word-NOT syntax. Groups, comparison
functions, all existing list delimiters, quoted content, and partial tokens
retain their distinct parser states. Cursor replay uses the same recovery
boundaries as syntax diagnostics.

## Supported cursor contexts

Completion tests cover the following states using explicitly configured
field and variable metadata. Recognized slots can have no eligible candidates
after prefix/type/permission filtering. `<caret>` denotes the
cursor and is not part of the input.

| Input | Recognized context |
|---|---|
| `Price <caret>` | Operators for the Decimal operand |
| `Status = <caret>` | A value operand bound to the String field |
| `CreatedAt > $<caret>` | A variable prefix in a DateTime operand slot |
| `Customer.<caret>` | A path prefix with the entire path token replaceable |
| `Price not i<caret>` | A partial compound comparison, not unary NOT |
| `not (Price > 1<caret>` | A complete condition in an open group |
| `Enabled <caret>` | Boolean comparisons and logical continuations when implicit Equal is permitted |
| `Status in ['Active', <caret>]` | A scalar membership item |
| `Status = 'Ac<caret>ZZ'` | A bound quoted value, replacing both quotes and all content |
| `Sta<caret>Tus = 'Active'` | A field edit constrained by the surviving comparison |
| `Status.con<caret>` | An existing comparison-function prefix on a String receiver |

The same membership context supports brackets, braces, and parentheses. A
completed token can switch to a zero-length continuation; partial tokens retain
their entire replacement span, including text after the cursor. Deeper path
segments after the active segment remain constraints rather than being dropped.

Unknown or denied receivers, conflicting adjacent operands, and cursor positions
inside an incomplete escape sequence are suppressed. An independent invalid
clause does not poison a reliable local context. Quoted text does not become a
logical recovery boundary, and arbitrary unbound quoted text does not become a
field/operator slot. These distinctions do not assert whole-expression validity.

An undecidable context returns `ContextUnavailable` and the generic, positioned
`completion-context-unavailable` diagnostic. A reliable context with no eligible
matching candidates returns `NoMatches` without a fabricated syntax error.

## Public fields, legacy paths, and variables

Typed `QuerySchema<T>` fields use exact registered public spellings, display
labels, descriptions, and selector result types. Nonfilterable fields and fields
with no permitted comparison operators are omitted. Selectors are never run or
used to discover input members. Registering `total` does not make `total.Year`
or a selector's internal members queryable.

Legacy `ExpressionSchema.FromType<T>` follows readable-member path rules,
ancestor denials, and nonempty field allowlists. Only the requested member level
is traversed; cyclic object graphs are not recursively flattened. An explicitly
allowed full path such as `Customer.Name` can complete `Customer.`. An exact
mapping `buyer -> Customer.Name` offers `buyer` but never invents `buyer.Name`.
Query-context navigation, collection-access, and canonical operator restrictions
apply to aliases too.

For a permitted legacy nested path:

```csharp
var legacy = ExpressionSchema.FromType<NavigationProduct>(
    validProperties: new[] { "Customer.Name" });
var nested = Interpreter.CompleteExpression("Customer.", 9, legacy);
// The field item inserts Customer.Name, replacing the entire Customer. token.
```

`NavigationProduct` has a readable `Customer` property whose type has a readable
String `Name` property. In typed public mode, register a scalar projection
instead of a dotted field:

```csharp
var projected = new QuerySchema<Product>()
    .Field("total", product => product.Price * 2, displayName: "Total")
    .Build();
var totals = Interpreter.CompleteExpression("tot", 3, projected);
```

The item label is `Total`, insertion is `total`, and its public result type is
Decimal. Neither `Price` nor appended members of `total` are discovered.

Variables are explicit immutable type declarations:

```csharp
var variables = new ExpressionVariableSchema(new[]
{
    new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
    new ExpressionVariableDefinition("startOfMonth", typeof(DateTime))
});
```

No built-ins are implied by omission. References insert the full `$` spelling.
Root names and exact dotted declarations are ordinal case-sensitive. Explicit
or reflected child member matching is ordinal case-insensitive, preserving the
selected binding-valid declaration spelling. Exact dotted declarations take
precedence over traversal. Declare child members explicitly, or opt into
readable-member metadata with `ExpressionVariableDefinition.FromType<T>("user")`.
An incompatible parent can expose a directly compatible child without resolving
the parent value. No getter, resolver, runtime collection enumerator, selector,
or application conversion executes.

Generated values are declared enum names as quoted strings, Boolean literals,
and semantically applicable null. No enum flag combinations, arbitrary numbers,
dates, or unconstrained strings are invented. Hints supplement these values;
their first equivalent presentation wins over generated metadata.
Variable descriptions identify declarations and type compatibility, not runtime
availability. Resolvers may still fail and runtime collections may exceed policy
cardinality limits.

## Ordering and bounded work

Matching is prefix-based, not fuzzy, and uses accepted spellings rather than
display labels. Field/path/literal matching is ordinal case-insensitive;
variable roots retain binding-valid casing. Results are deduplicated before
limiting and ordered by Field, Operator, Value, Variable, LogicalOperator,
Delimiter; exact prefix matches rank first, then accepted spellings use ordinal
case-insensitive order and an ordinal tie-breaker. Comparison operators use
`=`, `!=`, `>`, `>=`, `<`, `<=`, `in`, `not in`, then existing string
comparisons. Logical operators use `and`, `or`, `not`.

| Budget | Ceiling |
|---|---|
| Exact input | 16,384 UTF-16 units |
| Tokens | 8,192 |
| Simultaneously open syntactic scopes | 64 |
| Candidate descriptor visits, including rejected metadata | 4,096 |
| Results | 50 by default, configurable 1 through 200 |
| Policy diagnostics | 32 using the existing truncation marker |

A tighter query-policy source/depth/condition/item limit wins. Already exceeded
limits return no items, `LimitExceeded`, `IsIncomplete`, and a positioned
`completion-work-limit-exceeded` or existing policy diagnostic. Candidates must
also fit the resulting source/policy budget. Token replacements reserve their
existing condition/list item rather than charging twice; at a full condition or
list-item budget, matching closers remain eligible but new continuations/items
are suppressed. Variable-backed list cardinality remains a runtime check.

Descriptor exhaustion returns sorted, proven candidates from the visited
metadata prefix and sets `IsIncomplete`. This is not a guarantee of globally
best matches in unvisited metadata. Extra eligible results also set
`IsIncomplete`. There is no runtime discovery, external suggestion service, or
speculative request-result cache.

## Source offsets

Library offsets count UTF-16 units in the exact untrimmed input. Ranges are
half-open: `[Start, Start + Length)`. Leading whitespace, CRLF pairs, escaped
quotes, and both units of a supplementary Unicode character count. A zero
length denotes insertion at a caret, including end of input.

Identifiers and variables replace their entire token, including right-of-cursor
characters, dots, and `$`. Compound operators include internal whitespace.
Quoted values replace both quotes and their content, preserving the active
quote style; an unfinished quote extends to EOF. Deeper path suffixes constrain
the selection and are not discarded. Whitespace gaps are preserved. Insertions
include only required token-boundary spaces, never optional punctuation or
automatic closing pairs. Existing commas and closing delimiters are not
duplicated.

Apply an item without altering surrounding text:

| Input (`<caret>` is not source text) | Start | Length | InsertionText |
|---|---:|---:|---|
| `Sta<caret>Tus = 'Active'` | 0 | 6 | `Status` |
| `Customer.<caret>` | 0 | 9 | `Customer.Name` |
| `CreatedAt > $<caret>` | 12 | 1 | `$utcnow` |
| `Price ><caret>= 1` | 6 | 2 | `>=` |
| `Status = 'Ac<caret>ZZ'` | 9 | 6 | `'Active'` |
| `Price > 1<caret>` | 9 | 0 | ` and` (one required leading space) |

```csharp
using Kkts.Expressions;

static string Apply(string text, ExpressionCompletionItem item) =>
    text.Substring(0, item.Start) + item.InsertionText +
    text.Substring(item.Start + item.Length);
```

Editor adapters must map their cursor/range coordinates to these exact offsets
and must not normalize the text between the completion request and application
of the edit. No editor or HTTP dependency is required by the core contracts.

JavaScript string indices and CodeMirror document offsets are already UTF-16.
For a one-based line number and zero-based UTF-16 column, use
`doc.line(lineNumber).from + column`; subtract one first if your editor reports
one-based columns. For an editor that counts Unicode code points, convert the
prefix to a UTF-16 length before calling the API. Reject cursor positions inside
a surrogate pair. Preserve the editor's exact newline representation in the
request and snapshot used to apply the result.

## Editor integration

The sample uses `view.state.selection.main.head` and the exact
`view.state.doc.toString()`. Its 150 ms debounce cancels superseded fetches and
checks document, cursor, and monotonic request identity before publishing
suggestions or diagnostics. Cursor-only moves invalidate results too. Accepting
a completion rechecks the snapshot and applies that item's own range, not a
shared frontend word range.

Item kinds map to CodeMirror completion categories; labels and descriptions are
rendered as text, never HTML. Existing diagnostics use the same server-owned
query context and snapshot, including zero-length EOF ranges. The diagnostic
model has syntax/semantic kinds rather than severity; the sample presents
unavailable completion contexts as informational, truncation as a warning, and
other diagnostics as errors.

The .NET host exposes one development-only completion/diagnostics bridge.
Requests contain only text, UTF-16 offset, and snapshot identity. Schemas,
policies, variables, and hints remain on the server. Responses contain explicit
DTOs with kind/status names and safe public type summaries, never schemas,
selector trees, arbitrary CLR `Type` objects, or resolver values. This is not a
production API, editor, authorization mechanism, or query executor.

See the [sample instructions](../examples/Kkts.Examples/Kkts.Examples.Autocomplete/README.md)
for two-terminal startup, frontend/backend tests, and manual browser checks.

## Performance verification

Run from the repository root:

```sh
dotnet run --project examples/Kkts.Examples/Kkts.Examples.Autocomplete/Kkts.Examples.Autocomplete.csproj --configuration Release -- --performance
```

The command refuses Debug runs. It warms 100 requests and measures 1,000 across
12 typing, middle-edit, grouped, and recovery snapshots, with a maximum source
length of 2,803 UTF-16 units and 256 declared field/value/variable descriptors.
It prints environment, p50/p95, and mean thread allocations, then exits nonzero
if p95 exceeds 20 ms or mean allocation exceeds 256 KiB. There is no flaky
absolute-timing unit assertion.

Reference run on 2026-10-10: Apple M4 Pro, macOS 27.0.1, Arm64, 12 logical CPUs, .NET 10.0.12
(SDK 10.0.401): p50 **0.026 ms**, p95 **1.410 ms**, mean **159,740 bytes/request**.
These are measured workload results, not a guarantee for arbitrary hardware.

Separate adversarial checks cover 16,384/16,385 source units, 8,192/8,193 unary
tokens, 64/65 scopes, long unary receivers, incomplete lists/quotes, recovered
clauses, and 5,000 declared fields. They verify refusal, termination, safe
visited-prefix results, and deterministic repeats. Automated core tests assert
the exact source/token/scope/descriptor N/N+1 boundaries and rejected-descriptor
accounting. Adversarial inputs are reported separately, not used to dilute the
representative thresholds; near-ceiling unary workloads can allocate several
MiB and are not covered by the representative 256 KiB mean.
