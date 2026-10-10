# Kkts.Expressions
## Use it to build search screens, data grids, and API filtering.

Build dynamic filters for .NET applications.

Kkts.Expressions converts string expressions and structured filter objects into LINQ expression trees. Use it to build search screens, data grids, and API filtering with filtering, sorting, and pagination.

Map public filter names to model properties and restrict which properties clients can filter.

The library targets `netstandard2.0` and is available on
[NuGet](https://www.nuget.org/packages/Kkts.Expressions).

## Contents

- [Kkts.Expressions](#kktsexpressions)
  - [Use it to build search screens, data grids, and API filtering.](#use-it-to-build-search-screens-data-grids-and-api-filtering)
  - [Contents](#contents)
  - [Installation](#installation)
  - [Quick start](#quick-start)
    - [Query policy quick start](#query-policy-quick-start)
    - [Typed public-field quick start](#typed-public-field-quick-start)
  - [Structured filters](#structured-filters)
    - [Filter groups](#filter-groups)
  - [Nested JSON filter trees](#nested-json-filter-trees)
  - [Sorting](#sorting)
  - [Variables](#variables)
    - [Built-in variables](#built-in-variables)
    - [Custom variables](#custom-variables)
    - [Variables with the `in` operator (v2)](#variables-with-the-in-operator-v2)
  - [Property validation and mapping](#property-validation-and-mapping)
    - [Restrict allowed properties](#restrict-allowed-properties)
    - [Map external names to entity properties](#map-external-names-to-entity-properties)
  - [Conditions and pagination](#conditions-and-pagination)
    - [Retrieve a page](#retrieve-a-page)
  - [Supported operators](#supported-operators)
  - [Parsing and validation behavior](#parsing-and-validation-behavior)
  - [Expression semantic analysis](#expression-semantic-analysis)
  - [Expression editor integration (v3.0 only)](#expression-editor-integration-v30-only)
    - [Building editor highlight runs](#building-editor-highlight-runs)
    - [Displaying syntax diagnostics while typing](#displaying-syntax-diagnostics-while-typing)
  - [TimeSpan durations](#timespan-durations)
  - [DateTime and DateTimeOffset strings](#datetime-and-datetimeoffset-strings)
  - [Binary addition and subtraction in predicates (v3.0 only)](#binary-addition-and-subtraction-in-predicates-v30-only)
    - [Arithmetic and concatenation rules](#arithmetic-and-concatenation-rules)
    - [Limitations](#limitations)
  - [Development notes](#development-notes)
    - [Running tests](#running-tests)
    - [Relational Entity Framework Core tests](#relational-entity-framework-core-tests)
      - [Character arithmetic limitation](#character-arithmetic-limitation)
    - [Parser performance](#parser-performance)
    - [Code quality and compatibility](#code-quality-and-compatibility)
  - [Contact](#contact)

## Installation

```sh
dotnet add package Kkts.Expressions
```

Import the library namespace:

```csharp
using Kkts.Expressions;
```

Examples below also use types from `System`, `System.Collections.Generic`,
`System.Linq`, and `System.Linq.Expressions`. Database examples assume an
Entity Framework Core context with an `Entities` property of type `DbSet<Data>`.
Configure that context and its provider in your application.

## Quick start

The examples use this entity:

```csharp
public class Data
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime CreationDate { get; set; }
}
```

Parse a predicate and check the result before applying it to a query:

```csharp
var evaluation = Interpreter.ParsePredicate<Data>(
    "id = 1 and name = 'Test'");

if (!evaluation.Succeeded)
{
    throw new InvalidOperationException(
        "Invalid predicate. Inspect the evaluation diagnostics.",
        evaluation.Exception);
}

Expression<Func<Data, bool>> predicate = evaluation.Result;
var records = context.Entities.Where(predicate).ToList();
```

Property names are case-insensitive: `id` and `Id` refer to the same property.
The predicate above is equivalent to:

```csharp
Expression<Func<Data, bool>> predicate =
    item => item.Id == 1 && item.Name == "Test";
```

For user-supplied input, report validation diagnostics to the caller rather
than using `Result` when parsing fails. See
[Parsing and validation behavior](#parsing-and-validation-behavior).

### Query policy quick start

Opt in to a server-owned context for consistent editor analysis and runtime
enforcement. The recommended preset is an explicit starting point; adjust its
limits and field rules to fit the application:

```csharp
var schema = ExpressionSchema.FromType<Data>(
    validProperties: new[] { "Id", "Name", "IsEnabled" });
var queryContext = new ExpressionQueryContext(schema, QueryPolicy.Recommended);

var editorAnalysis = queryContext.AnalyzeExpression(userText);
var checkedPredicate = queryContext.ParsePredicate<Data>(userText);
if (!checkedPredicate.Succeeded)
{
    throw checkedPredicate.Exception;
}

var results = context.Entities
    .Where(item => item.TenantId == currentTenantId)
    .Where(checkedPredicate.Result);
```

The tenant predicate is application-owned and must not be derived from user
input. Query policies control permitted expression construction and
complexity; they are not authorization, tenant isolation, or database cost
controls. See the [query policy guide](docs/query-policies.md) for configuration,
counting rules, structured APIs, diagnostics, and security boundaries.

### Typed public-field quick start

Register only the names an API should expose. Selectors are strongly typed,
and computed expressions remain in the resulting expression tree:

```csharp
var publicSchema = new QuerySchema<Data>()
    .Field("displayName", item => item.Name)
    .Field("enabled", item => item.IsEnabled)
    .Field("createdAt", item => item.CreationDate, canFilter: false)
    .Build();
var publicContext = new ExpressionQueryContext(
    publicSchema,
    new QueryPolicy());

var predicate = publicContext.ParsePredicate<Data>(
    "displayName = 'Test' and enabled = true");
if (!predicate.Succeeded)
{
    throw predicate.Exception;
}
var results = context.Entities.Where(predicate.Result).ToList();
```

Public mode exposes exactly the registered names and keeps filter/sort
permissions independent. See the
[expression-field mapping guide](docs/expression-field-mapping.md) for
computed selectors, metadata, policy integration, migration, and SQL
translation limits.

## Structured filters

Use `Filter` objects when your UI supplies structured data instead of a
predicate string. Filters in a collection are combined with logical AND:

```csharp
var filters = new[]
{
    new Filter { Property = "Id", Operator = "=", Value = "1" },
    new Filter { Property = "Name", Operator = "=", Value = "Test" }
};

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
```

`BuildPredicate` throws if the filters cannot be built. Use
`filters.TryBuildPredicate<Data>()` to obtain an evaluation result and inspect
its `Succeeded` flag and diagnostics instead.

In v3.0 only, exclude a set of values with the same comma-separated list
representation as `in`:

```csharp
var filter = new Filter { Property = "Id", Operator = "not in", Value = "1, 2" };
Expression<Func<Data, bool>> predicate = filter.BuildPredicate<Data>();
```

Direct builders in v3.0 expose the equivalent additive enum member,
`ComparisonOperator.NotIn` (v3.0 only):

```csharp
var predicate = Interpreter.BuildPredicate<Data>(
    "Id", ComparisonOperator.NotIn, "1, 2");
```

### Filter groups

Filters within each group are combined with AND; groups are combined with OR.
For example:

```text
(Id = 1 and Name = 'Test1') or (Id = 2 and Name = 'Test2')
```

The equivalent structured representation is:

```csharp
var groups = new[]
{
    new FilterGroup
    {
        Filters = new List<Filter>
        {
            new Filter { Property = "Id", Operator = "=", Value = "1" },
            new Filter { Property = "Name", Operator = "=", Value = "Test1" }
        }
    },
    new FilterGroup
    {
        Filters = new List<Filter>
        {
            new Filter { Property = "Id", Operator = "=", Value = "2" },
            new Filter { Property = "Name", Operator = "=", Value = "Test2" }
        }
    }
};

Expression<Func<Data, bool>> predicate = groups.BuildPredicate<Data>();
```

Use `groups.TryBuildPredicate<Data>()` when you need validation details.

## Nested JSON filter trees

For arbitrarily nested AND/OR/NOT conditions with typed values and explicit
variable references, use `FilterNode` and `FilterTreeJson`:

```csharp
var tree = FilterNode.And(new[]
{
    FilterNode.Or(new[]
    {
        FilterNode.Condition("Status", "=", FilterValue.String("Active")),
        FilterNode.Condition("Priority", ">=", FilterValue.Number("3"))
    }),
    FilterNode.Not(FilterNode.Condition(
        "Department",
        "in",
        FilterValue.Collection(new[]
        {
            FilterValue.String("Archived"),
            FilterValue.String("External")
        })))
});

var json = FilterTreeJson.Serialize(tree);
var queryContext = new ExpressionQueryContext(
    ExpressionSchema.FromType<Data>(),
    QueryPolicy.Recommended);
var decoded = FilterTreeJson.TryDeserialize(json, queryContext);
if (!decoded.Succeeded) throw new QueryPolicyException(decoded.Diagnostics);
var predicate = queryContext.BuildPredicate<Data>(decoded.Result);
```

The context-aware codec validates the tree against entity permissions and
query policy before returning it. The no-context codec validates JSON shape
only. See the [nested filter guide](docs/nested-filters.md) for JSON shapes,
typed values, diagnostics, depth limits, serializer transport settings, and
migration from `FilterGroup`, plus the supported
[expression editor conversions](docs/nested-filters.md#expression-editor-exchange);
see the [query policy guide](docs/query-policies.md)
for policy accounting and server-side boundaries.

## Sorting

Supply a comma-separated list of properties and optional directions:

```csharp
var ordered = context.Entities.OrderBy("Id, Name desc");
```

The default direction is ascending. Both an omitted direction and `asc` mean
ascending order; `desc` means descending order. Property names are
case-insensitive.

The equivalent structured form is:

```csharp
var ordered = context.Entities.OrderBy(new[]
{
    new OrderByInfo { Property = "Id" },
    new OrderByInfo { Property = "Name", Descending = true }
});
```

`OrderBy` throws for invalid sorting input. Use `TryOrderBy` to inspect the
evaluation result:

```csharp
var evaluation = context.Entities.TryOrderBy("Id, Name desc");

if (!evaluation.Succeeded)
{
    throw new InvalidOperationException(
        "Invalid sorting expression. Inspect the evaluation diagnostics.",
        evaluation.Exception);
}

var ordered = evaluation.Result;
```

## Variables

Version 2 introduced explicit query variables using the `$` prefix, along with
`ParsePredicateAsync`. Use the prefixed syntax in v2 and later.

### Built-in variables

The default resolver provides two case-insensitive variables:

- `$now`: the resolver's local date and time.
- `$utcnow`: the resolver's UTC date and time.

```csharp
var evaluation = Interpreter.ParsePredicate<Data>(
    "CreationDate = $now or CreationDate = $utcnow");

var sameYear = Interpreter.ParsePredicate<Data>(
    "CreationDate.year = $now.year");
```

Earlier versions used unprefixed names such as `now` and `utcnow`. These
examples use the v2 query-variable syntax.

### Custom variables

Register values directly on a resolver. Names are registered without the `$`
prefix and referenced with it in predicates:

```csharp
var resolver = new VariableResolver();
resolver.TryAdd("user.id", 1);
resolver.TryAdd("user.username", "linhle");

var evaluation = Interpreter.ParsePredicate<Data>(
    "Name = $user.username and Id = $user.id",
    variableResolver: resolver);
```

You can also expose objects as properties of a custom resolver:

```csharp
public class UserInfo
{
    public string UserName { get; set; } = "";
    public int Id { get; set; }
}

public class CustomVariableResolver : VariableResolver
{
    public UserInfo User { get; } =
        new UserInfo { Id = 1, UserName = "linhle" };
}
```

Both synchronous and asynchronous parsing accept a resolver:

```csharp
var resolver = new CustomVariableResolver();

var evaluation = Interpreter.ParsePredicate<Data>(
    "Name = $user.username and Id = $user.id",
    variableResolver: resolver);

EvaluationResult<Data, bool> asyncEvaluation =
    await Interpreter.ParsePredicateAsync<Data>(
        "Name = $user.username and Id = $user.id",
        variableResolver: resolver);
```

For values that require asynchronous work, override `TryResolveCore` in your
resolver. Use `InitializeVariablesAsync` to initialize application-specific
state before parsing when needed. The
[custom resolver example](examples/Kkts.Examples/Kkts.Examples.VariableResolver/CustomVariableResolver.cs)
demonstrates direct registration, object properties, and asynchronous resolution.
See also the
[variable usage example](examples/Kkts.Examples/Kkts.Examples.VariableResolver/Program.cs).

### Variables with the `in` operator (v2)

Use a variable as an array element or as the collection itself:

```csharp
var resolver = new VariableResolver();
resolver.TryAdd("user.id", 1);
resolver.TryAdd("userIds", new[] { 1, 2, 3 });

var singleValue = await Interpreter.ParsePredicateAsync<Data>(
    "Id in [$user.id]", variableResolver: resolver);

var collection = await Interpreter.ParsePredicateAsync<Data>(
    "Id in $userIds", variableResolver: resolver);
```

See the
[`in` operator example](examples/Kkts.Examples/Kkts.Examples.VariablesAndInOperator/Program.cs)
for database-backed usage.

## Property validation and mapping

### Restrict allowed properties

Use `validProperties` to limit which entity properties a query may reference:

```csharp
var evaluation = Interpreter.ParsePredicate<Data>(
    "Id = 1 and Name = 'Test'",
    validProperties: new[] { "Id" });
```

This evaluation fails because `Name` is not allowed. Check `Succeeded`,
`InvalidProperties`, and `Exception` before using the result.

For UI-driven queries, explicitly allow only properties you intend to expose.
Property validation does not replace application authorization.

### Map external names to entity properties

Use `propertyMapping` when the UI uses different field names:

```csharp
var mapping = new Dictionary<string, string>
{
    ["entityId"] = "Id"
};

var evaluation = Interpreter.ParsePredicate<Data>(
    "(entityId = 1 and Name = 'Test1') or " +
    "(entityId = 2 and Name = 'Test2')",
    propertyMapping: mapping);
```

The same mapping works with structured filters and filter groups:

```csharp
var filters = new[]
{
    new Filter { Property = "entityId", Operator = "=", Value = "1" },
    new Filter { Property = "Name", Operator = "=", Value = "Test1" }
};

var predicate = filters.BuildPredicate<Data>(propertyMapping: mapping);
var groupedPredicate = groups.BuildPredicate<Data>(propertyMapping: mapping);
```

## Conditions and pagination

`ConditionOptions` combines filtering and sorting in a single object that can
be deserialized from JSON. It accepts:

- `Where`: a predicate string.
- `Filters`: a collection of structured filters.
- `FilterGroups`: a collection of structured filter groups.
- `OrderBy`: a sorting string.
- `OrderBys`: a list of structured sorting instructions.

When supplied together, `Where`, `Filters`, and `FilterGroups` contribute
predicates combined with AND. A nonblank `OrderBy` takes precedence over
`OrderBys`.

```csharp
var options = new ConditionOptions
{
    Where = "IsEnabled",
    Filters = new[]
    {
        new Filter { Property = "Id", Operator = ">", Value = "0" }
    },
    OrderBy = "Id, Name desc"
};

var condition = options.BuildCondition<Data>();

if (!condition.IsValid)
{
    throw new InvalidOperationException(
        "Invalid condition. Inspect condition.Error for details.");
}

var records = context.Entities.Where(condition).ToList();
```

`Where(condition)` applies both the predicates and the condition's sorting.
Always check `IsValid` first; `Error` contains exceptions and evaluation
diagnostics for an invalid condition.

In v3.0 only, exclusion also works in `Where`, using the same validation pattern:

```csharp
var condition = new ConditionOptions { Where = "Id not in [1, 2]" }
    .BuildCondition<Data>();

if (!condition.IsValid)
{
    throw new InvalidOperationException(
        "Invalid condition. Inspect condition.Error for details.");
}

var records = context.Entities.Where(condition).ToList();
```

### Retrieve a page

Use either offset/limit or page/page-size notation:

```csharp
var byOffset = context.Entities.Take(
    condition, new Pagination { Offset = 10, Limit = 10 });

var byPage = context.Entities.Take(
    condition, new Pagination { Page = 2, PageSize = 10 });
```

To include the total number of matching records, use `TakePage`:

```csharp
var pageByOffset = context.Entities.TakePage(
    condition, new Pagination { Offset = 10, Limit = 10 });

var pageByNumber = context.Entities.TakePage(
    condition, new Pagination { Page = 2, PageSize = 10 });
```

`TakePage` returns a paged result containing `Records` and `TotalRecords`.
Use an explicit, stable sort order for predictable pagination.

## Supported operators

| Operator | Examples | Supported data types |
| --- | --- | --- |
| Equal | `Id = 1` or `Id == 1` | Number, string, Guid, Boolean, DateTime, DateTimeOffset, TimeSpan, enum, nullable |
| Not equal | `Id != 1` or `Id <> 1` | Number, string, Guid, Boolean, DateTime, DateTimeOffset, TimeSpan, enum, nullable |
| Less than | `Id < 1` | Number, DateTime, DateTimeOffset, TimeSpan, nullable |
| Less than or equal | `Id <= 1` | Number, DateTime, DateTimeOffset, TimeSpan, nullable |
| Greater than | `Id > 1` | Number, DateTime, DateTimeOffset, TimeSpan, nullable |
| Greater than or equal | `Id >= 1` | Number, DateTime, DateTimeOffset, TimeSpan, nullable |
| In | `Id in [1, 2, 3, 4]` or `Name in ['String1', 'String2']` | Number, string, Guid, Boolean, DateTime, DateTimeOffset, TimeSpan, enum, nullable |
| Not in (v3.0 only) | `Id not in [1, 2]` or `Name not in ['String1', 'String2']` | Number, string, Guid, Boolean, DateTime, DateTimeOffset, TimeSpan, enum, nullable |
| Contains | `Name.contains('Text')` or `Name @ 'Text'` | String |
| Starts with | `Name.startsWith('Text')` or `Name @* 'Text'` | String |
| Ends with | `Name.endsWith('Text')` or `Name *@ 'Text'` | String |
| Not | `!IsEnabled`, `not(IsEnabled)`, `not(Id = 1)`, or `!(Id = 1)` | Boolean |
| Logical AND (`and` or `&&`) | `Id = 1 and Name = "Text"` | Boolean |
| Logical OR (`or` or `\|\|`) | `Id = 1 or Name = "Text"` | Boolean |
| Plus (v3.0 only) | `Id + 1 > 5` or `Name + '!' = 'Test!'` | Number, nullable number, string (including mixed operands) |

Numeric literals use invariant culture and a period as the decimal separator
(for example, `8.3`), regardless of the current culture. When building queries
with interpolated numeric values, use `FormattableString.Invariant`. Commas
separate elements in `in` arrays; they are not decimal separators.

**The `not in` operator and `ComparisonOperator.NotIn` apply only to v3.0**
across predicate parsing, direct builders, structured filters, and conditions.

`not in` is case-insensitive and requires one or more spaces, tabs, carriage
returns, or newlines between its keywords. Structured filters also accept
leading/trailing whitespace. `notin` and `!in` are not aliases.

Exclusion is exactly the Boolean complement of `in`, emitted as a native
`Not(Contains(...))` expression tree. It supports the same list-element and
collection variables and computed left operands. A null nullable value satisfies
`NullableInteger not in [1, 2]`, but not `NullableInteger not in [null, 1]`.
Unquoted `null` is a null element in either membership operator; quoted `'null'`
remains text. Boolean list values use quoted forms such as `['true', 'false']`.
An empty list always yields true for `not in`, and duplicate elements do not
change the result. There is no SQL-style unknown result or implicit null
filtering. Relational translation and null behavior depend on the query
provider; compiled LINQ and EF Core InMemory tests do not guarantee translation
by every relational provider.

## Parsing and validation behavior

- Every predicate must return `bool`. Numeric or string-only expressions
  produce an unsuccessful evaluation result with a `FormatException`, including
  through generic overloads.
- Operators and keywords are case-insensitive and independent of the current
  culture. String values are not normalized.
- Each `!` is a separate negation: `!!IsEnabled` is equivalent to
  `!(!IsEnabled)`.
- Generic and runtime-type parsing results retain the same validation
  diagnostics, including `InvalidValues`.
- Evaluation results expose `Succeeded`, `Exception`, `InvalidProperties`,
  `InvalidOperators`, `InvalidVariables`, `InvalidValues`, and
  `InvalidOrderByDirections`. Inspect the relevant diagnostics when an
  evaluation fails.
- Argument validation can still throw, for example when a predicate string is
  empty. An evaluation result is not a guarantee that every API call is
  exception-free.
- Async parsing awaits variable resolution for all expression shapes, not
  only expressions containing addition or subtraction. Cancellation produces
  an unsuccessful
  evaluation result whose `Exception` is an `OperationCanceledException` or a
  derived exception; it is not reported as an invalid value.
- Nested variable paths can traverse properties and fields at multiple levels.
  A missing member or null intermediate value is unresolved. Exceptions thrown
  by getters are surfaced rather than silently treated as missing variables.
- Async filter collections forward the caller's cancellation token through
  generic and runtime-type overloads.

## Expression editor integration (v3.0 only)

**The expression analysis API and the integration examples in this section
apply only to v3.0.** `Interpreter.AnalyzeExpression` and its token, diagnostic,
and result types are not available in v2.x.

Use `Interpreter.AnalyzeExpression(text)` on each editor text snapshot to obtain
syntax highlighting and positioned syntax errors. This is a UI-independent API:
it needs no entity type, variable resolver, frontend package, or HTTP service.
It does not build or execute LINQ expressions.

```csharp
var text = "  Id = $x  ";
var analysis = Interpreter.AnalyzeExpression(text);

foreach (var token in analysis.Tokens)
{
    var tokenText = text.Substring(token.Start, token.Length);
    Console.WriteLine($"{token.Kind}: {tokenText} at {token.Start}");
}

foreach (var diagnostic in analysis.Diagnostics)
{
    Console.WriteLine(
        $"{diagnostic.Code}: {diagnostic.Message} " +
        $"at {diagnostic.Start}, length {diagnostic.Length}");
}
```

This example returns property `(2, 2)`, operator `(5, 1)`, and variable `(7, 2)`
tokens. `Start` and `Length` are zero-based **UTF-16** offsets into the exact
input, not a trimmed or normalized copy. Ranges are half-open:
`[Start, Start + Length)`. They match .NET string and JavaScript string offsets,
not UTF-8 byte offsets or displayed character counts. Tokens do not overlap;
whitespace outside tokens is preserved as unclassified gaps.

| Kind | Suggested style | Examples |
| --- | --- | --- |
| `Property` | `property` | `Customer.Name`, `Id` |
| `Variable` | `variable` | `$user.name`, `$x` (including `$`) |
| `Operator` | `operator` | `=`, `and`, `not in`, `-`, `contains` |
| `Constant` | `constant` | `-5`, `'a+b'`, `true`, `null` |
| `Punctuation` | `punctuation` | Function dots, parentheses, list delimiters and commas |
| `Unknown` | `unknown` | Unrecognized text such as `#` outside a literal |

Compound `not in` tokens include the whitespace between their keywords.
Nested property/variable paths are single tokens. Function names are operators
separate from their dots and parentheses. Strings include their quotes and
escapes; operators inside quoted text are not separate tokens. Membership lists
expose individual items and punctuation, while retaining the existing list
syntax and leaving value conversion to predicate validation.

### Building editor highlight runs

The following helper produces text/style pairs, including whitespace gaps,
without generating HTML. It uses `System.Collections.Generic` and
`Kkts.Expressions`:

```csharp
public static IReadOnlyList<(string Text, string Style)> BuildHighlightRuns(
    string text, out ExpressionAnalysisResult analysis)
{
    analysis = Interpreter.AnalyzeExpression(text);
    var runs = new List<(string Text, string Style)>();
    var cursor = 0;
    foreach (var token in analysis.Tokens)
    {
        if (token.Start > cursor)
            runs.Add((text.Substring(cursor, token.Start - cursor), "plain"));

        string style;
        switch (token.Kind)
        {
            case ExpressionTokenKind.Property: style = "property"; break;
            case ExpressionTokenKind.Variable: style = "variable"; break;
            case ExpressionTokenKind.Operator: style = "operator"; break;
            case ExpressionTokenKind.Constant: style = "constant"; break;
            case ExpressionTokenKind.Punctuation: style = "punctuation"; break;
            default: style = "unknown"; break;
        }
        runs.Add((text.Substring(token.Start, token.Length), style));
        cursor = token.Start + token.Length;
    }
    if (cursor < text.Length)
        runs.Add((text.Substring(cursor), "plain"));
    return runs.AsReadOnly();
}
```

Call this helper from your text-change handler and render each run as text with
the corresponding client-defined style. Use text nodes or your framework's
escaped text rendering; **never insert user expression text as raw HTML**.
Preserve whitespace and the original text rather than replacing the editor's
value with reconstructed or normalized tokens.

### Displaying syntax diagnostics while typing

Analysis continues highlighting after recoverable errors and reports multiple
independent errors. For example,
`Id = ) and Name = ] and IsEnabled = true` reports errors at `(5, 1)` and
`(18, 1)` while keeping all subsequent tokens highlighted. Recovery does not
attempt to fix the expression or guarantee one diagnostic per arbitrary mistake.

An unfinished `Name = 'abc` retains constant token `(7, 4)` and reports an
`unterminated-string` error at `(11, 0)`. Missing operands, quotes, and scope
delimiters at end of input use `(text.Length, 0)`: draw a caret marker there,
not an out-of-range character underline. Other diagnostics identify the
offending source range. Display `Message` to the user; use `Code` for programmatic
handling:

| Code | Meaning |
| --- | --- |
| `unknown-text` | Unrecognized source character |
| `unexpected-token` | Token is not accepted in this syntax position |
| `missing-operand` | An operand or completed expression is required |
| `unmatched-delimiter` | A closing scope delimiter is required |
| `unterminated-string` | A closing quote is required |
| `incomplete-identifier` | A property/variable path needs an identifier |

Results and their collections are read-only snapshots. Empty/whitespace-only
input produces no tokens or diagnostics and `IsComplete == false`; null input
throws `ArgumentNullException`. Expected syntax errors return diagnostics
rather than throw.

`IsComplete` means **nonblank input with valid syntax only**. Unknown properties
and unresolved variables can be syntactically complete; so can a non-Boolean
expression such as `1 + 2`. Continue using `ParsePredicate` or
`ParsePredicateAsync` and checking `Succeeded` before executing a query.

The library does not debounce or retain editor state. Consumers may debounce
or schedule analysis for long inputs. If work is scheduled asynchronously, apply
the result only if its input snapshot is still the current editor text, so an
older result cannot overwrite newer highlighting or diagnostics.

## Expression semantic analysis

For editor feedback that depends on entity members, variable declarations, and
operand types, use semantic analysis in addition to syntax highlighting. Supply
metadata rather than runtime entity or variable values:

```csharp
var schema = ExpressionSchema.FromType<Data>(
    validProperties: new[] { "Id", "Name", "IsEnabled" });

var variables = new ExpressionVariableSchema(new[]
{
    ExpressionVariableDefinition.FromType("minimum", typeof(int))
});

var analysis = Interpreter.AnalyzeExpression<Data>(
    "Id > 'abc' and UnknownField = $minimum",
    schema,
    variables);

foreach (var diagnostic in analysis.SemanticDiagnostics)
{
    Console.WriteLine(
        $"{diagnostic.Code} at {diagnostic.Start}+{diagnostic.Length}: " +
        diagnostic.Message);
}
```

This reports the incompatible operand and unknown property separately while
the user is typing. Analysis never resolves variable values or invokes entity
getters, and semantic validity does not guarantee EF Core SQL translation.
For query permissions, aliases, nested paths, stable diagnostic codes,
correction suggestions, and incomplete-input behavior, see the
[semantic analysis guide](docs/semantic-analysis.md).

Temporal editor validation recognizes compact literals such as
`Created = '20261009'` and `CreatedOffset = '20261009T152646+0700'`
as well as extended ISO forms. It uses an immutable schema conversion context
and invariant machine formats, not ambient culture or the mutable runtime
format list. `CreatedOffset = '20261009T152646'` still reports
`context-dependent-conversion` because no offset was supplied. Semantic
acceptance does not guarantee identical runtime values across machine timezones
or EF Core SQL translation.

## DateTime and DateTimeOffset strings

`ToDateTime`, `ToDateTimeOffset`, their try counterparts, and temporal `Cast`
operations accept the following machine-readable calendar forms. Predicate
values use the same forms inside quoted literals.

| Form | Example |
|---|---|
| Extended calendar date | `2026-10-09` |
| Compact calendar date | `20261009` |
| Extended full timestamp | `2026-10-09T15:26:46` |
| Compact full timestamp | `20261009T152646` |
| UTC timestamp | `2026-10-09T08:26:46.427Z` or `20261009T082646Z` |
| Timestamp with numeric offset | `2026-10-09T15:26:46.1234567+07:00` or `20261009T152646.427+0700` |

Full timestamps allow no fraction or 1-7 fractional-second digits. Use uppercase
`T` and `Z`, four-digit years, zero-padded components, and numeric offsets within
+/-14 hours. Extended offsets use `+HH:mm` / `-HH:mm`; compact offsets use
`+HHmm` / `-HHmm`. Date-only values represent midnight.

Parsing preserves legacy precedence: current-culture .NET parsing first, then
the mutable `StringExtensions.DateTimeFormats` list using the supplied provider
(or invariant culture), then the invariant machine-format fallback. The provider
does not override the first attempt. Existing accepted inputs retain their
interpretation, including culture/calendar-specific interpretations; the new
fallback uses the Gregorian calendar.

`ToDateTime` converts timestamps with `Z` or a numeric offset to machine-local
time with `DateTimeKind.Local`. Without a suffix, the kind is `Unspecified`.
It does not preserve UTC kind. Choose `ToDateTimeOffset` to retain an explicit
offset (`Z` becomes zero offset); without a suffix it uses the local timezone
for the represented date.

For JSON, pass the extracted string value, not the JSON quotes or document:

```csharp
var local = "20261009T082646Z".ToDateTime();
var withOffset = "20261009T152646.427+0700".ToDateTimeOffset();
```

This is a bounded supported subset, not a strict all-ISO-8601 parser. Legacy .NET
parsing may also accept other representations. No Unix epoch, ISO week/ordinal
date, raw JSON, or legacy Microsoft JSON `/Date(milliseconds)/` parser is added.
Invalid temporal text throws `FormatException`; temporal try methods return
false with the respective minimum value. Blank strings cast to nullable temporal
types remain null.

## TimeSpan durations

`TimeSpan` and `TimeSpan?` properties support equality, inequality, ordering,
and `in`. Quote duration literals in predicates and `in` lists:

```text
Duration > '02:30:00'
Duration in ['02:30:00', '1.02:30:00']
```

Filter values use the same duration text, without outer quotes except in `in`
lists. Variables may contain duration strings or typed `TimeSpan` values.

`StringExtensions.Cast` parses durations with `TimeSpan.Parse`, using the
supplied format provider or invariant culture by default. Negative durations
and fractional seconds are supported. Empty or whitespace input becomes null
for `TimeSpan?`; invalid input throws through `Cast` and returns false through
`TryCast`.

Duration arithmetic with `+` or `-` is not supported.

## Binary addition and subtraction in predicates (v3.0 only)

**The binary `+` and `-` operators apply only to v3.0.**

Binary `+` and numeric-only `-` support properties, literals, variables, and
parenthesized values. These are predicate arithmetic operators, not members
of `ComparisonOperator` for structured filters:

```csharp
var numeric = Interpreter.ParsePredicate<Data>("Id + 1 = 5");
var grouped = Interpreter.ParsePredicate<Data>("(Id + 1) = (2 + 3)");
var concatenated = Interpreter.ParsePredicate<Data>("Name + '!' = 'Test!'");
var mixed = Interpreter.ParsePredicate<Data>("'ID: ' + Id = 'ID: 7'");
var propertyPair = Interpreter.ParsePredicate<Data>("Id + Id = 8");
var difference = Interpreter.ParsePredicate<Data>("Id - 1 = 3");
var negativeLiteral = Interpreter.ParsePredicate<Data>("Id - -5 = 9");
var additive = Interpreter.ParsePredicate<Data>("(Id + 2) - 1 >= 5");

var variables = new VariableResolver();
variables.TryAdd("increment", 2);
variables.TryAdd("target", 6);
var variableSum = await Interpreter.ParsePredicateAsync<Data>(
    "Id + $increment = $target", variableResolver: variables);
var variableDifference = await Interpreter.ParsePredicateAsync<Data>(
    "Id - $increment > 0", variableResolver: variables);

var condition = new ConditionOptions { Where = "Id + 1 = 5" }
    .BuildCondition<Data>();
var differenceCondition = new ConditionOptions { Where = "Id - 1 = 3" }
    .BuildCondition<Data>();
```

### Arithmetic and concatenation rules

- Binary `+` and `-` have equal precedence and associate left to right, before
  comparisons and logical operators: `10 - 3 + 2 = 9` and `10 - 3 - 2 = 5`.
  Parentheses override grouping: `1 + 2 + 'x'` produces `"3x"`, while
  `1 + (2 + 'x')` produces `"12x"`.
- Numeric operands follow C# numeric promotion, including small integer
  promotion to `int` and mixed integer/floating-point results. Whole-number
  arithmetic literals use the first fitting `int`, `uint`, `long`, or `ulong`;
  fractional literals use `double`. Decimal operands cannot be mixed with
  `float` or `double`.
- Nullable numeric addition and subtraction propagate null rather than
  replacing it with zero. For example, `NullableId - 1 = null` is true when
  `NullableId` is null. A bare null paired with a number also produces null;
  `null - null` is rejected because no numeric type can be inferred.
  Integral overflow/underflow wraps as in unchecked C# arithmetic; decimal
  overflow still
  throws when the predicate is evaluated.
- If either operand is a string, `+` concatenates, converting other operands
  using ordinary .NET/current-culture formatting. Null contributes an empty
  string. Quoted numeric text stays a string: `'1' + 2` produces `"12"`.
  Plus and minus characters inside quoted strings are literal text.
- Subtraction rejects strings (including quoted numeric text), Boolean, enum,
  date/time, duration, and user-defined arithmetic operands. Mixed chains
  remain left associative: `10 - 3 + 'x'` produces `"7x"`, but
  `10 + 'x' - 3` fails because the left subtraction operand is a string.
- Negative numeric literals are accepted where a value is expected. The sign
  must be adjacent to the number: `Id - -5`, `Id--5`, and `Id - (-5)` all
  subtract negative five. Negative fractional literals use the invariant
  decimal point. This does not enable `-Id`, `-$value`, or `-(Id + 1)`.
- Computed strings work in infix comparisons and existing string-function
  arguments, such as `Name + '!' contains 'Test!'` and
  `Name.contains('Te' + 'st')`.
- Both synchronous and asynchronous predicate APIs, including runtime-type
  overloads and condition `Where`, support both operators in v3.0. Async APIs
  retain asynchronous variable resolution and cancellation. Property mappings,
  allowlists, and variable diagnostics apply to both operands.
- A predicate must return Boolean: `Id + 1` or `Id - 1` alone is not a predicate.
  Check
  `Succeeded` and `Exception`, or `IsValid` and `Error` for a condition.
  Missing operands and unsupported operand pairs fail explicitly.

### Limitations

Unary plus, general unary negation, multiplication/division, date/time
arithmetic, user-defined arithmetic, arithmetic inside `in` array literals,
and computed ordering are not supported. A computed membership operand such
as `Id - 1 in [3,5]` is supported. No exponent notation or suffix syntax is
added.

The library emits native expression trees. Relational query translation,
especially mixed string concatenation, depends on the query provider.

## Development notes

### Running tests

The unit test project targets .NET 10 and requires the .NET 10 SDK:

```sh
dotnet test src/Kkts.Expressions.UnitTest/Kkts.Expressions.UnitTest.csproj
```

### Relational Entity Framework Core tests

The separate `Kkts.Expressions.EntityFrameworkCore.Tests` project verifies a
selected set of generated predicates against real relational databases. It
targets .NET 10 and pins EF Core 10.0.9, `Microsoft.EntityFrameworkCore.SqlServer`
10.0.9, Oracle's `MySql.EntityFrameworkCore` 10.0.9, and Testcontainers for .NET
4.16.0. Its database containers use SQL Server 2022 build 16.0.4255.1, pinned
to image digest
`mcr.microsoft.com/mssql/server@sha256:e07b9699a2b749969f19d86563ceeea22bd3a69f7f1db85a8d1ac4bdaf0c6f56`,
and MySQL 8.4 (`mysql:8.4.4`). These versions define the tested baseline, not
a compatibility promise for every release in either family.

Docker must be running locally. Run one provider or both:

```sh
dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj --filter "Provider=SqlServer"
dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj --filter "Provider=MySql"
dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj
```

The SQL Server fixture configures `Latin1_General_100_BIN2`; MySQL fixture
startup explicitly alters its pre-created test database to
`utf8mb4_0900_bin` before creating tables. Metadata assertions verify MySQL's
effective column collation. These binary collations make the tested string
cases deterministic and case-sensitive within each database. Date/time columns
use millisecond precision (`datetime2(3)` on SQL Server and `datetime(3)` on
MySQL through EF Core's precision mapping), also verified against database
metadata. Seeded fractional seconds therefore use millisecond values.
Nullable comparisons are tested as SQL queries with EF Core's normal null
semantics; generated predicates are compared with handwritten LINQ predicates
on the same seeded database. Results are not assumed identical where a
provider's configured semantics differ.

The shared suite directly applies generated expression trees to
`IQueryable.Where` and materializes database results. It does not compile
predicates or permit client-side evaluation. Parser validation tests run
separately from translation/execution tests. The suite covers the listed
operators and representative scalar/navigation cases only; it does not
guarantee translation for every expression, provider, database version,
collation, or provider configuration. Translation gaps discovered by these
tests are compatibility findings, not silently worked-around behavior.

#### Character arithmetic limitation

Although `char` is treated as a numeric type by the predicate arithmetic
implementation, arithmetic on character properties is not portable across
relational providers. For example, with a character property containing `'2'`,
the expression `Character + 1 = 51` follows C# character-code arithmetic when
evaluated in memory, but SQL Server and MySQL do not produce the same result
when translating and executing it as a database query. The expression is
therefore intentionally excluded from the passing cross-provider integration
suite; its behavior is not a compatibility guarantee. Avoid arithmetic on
`char` properties in relational predicates. Use a numeric property if numeric
arithmetic is required, or compare the character directly.

See the [EF Core relational provider test report (2026-10-09)](./EFCORE-RELATIONAL-TEST-REPORT-2026-10-09.md)
for the latest SQL Server and MySQL run results.

The library continues to target `netstandard2.0`; the example projects retain
their existing target frameworks.

### Parser performance

Synchronous and asynchronous predicate parsing reuse per-call candidate
buffers and build token chains directly in lists. Each precedence pass uses
read/write cursors to replace consumed operands with the operator's built node,
then trims the unused tail once. Parser entries are never marked as consumed
with `null`, and long operator chains do not require repeated list shifts.

This avoids per-character candidate-list allocations and intermediate
collections without changing operator precedence, validation, or variable
resolution.

Buffers and token-text caches are local to each parse. Read-only property-name
metadata is shared by entity type, while property mappings, allowlists, nested
path validation, and diagnostics remain local to the call. The parser does not
cache expressions or resolved variable values across calls; a supplied
`VariableResolver` retains its own value cache.

### Code quality and compatibility

SonarQube cleanup preserves the existing public API:

- `Pagination.DefaultLimit` and `Pagination.MaxLimit` remain mutable public
  fields. Replacing them with properties or constants would break existing
  consumers.
- The deprecated virtual `VariableResolver.IsVariable` method is retained for
  compatibility. Use `TryResolve` or `TryResolveAsync` instead.
- Resolving the public-field and deprecated-code findings requires a future
  breaking release.

Parity regression tests intentionally exercise synchronous APIs from async
tests, and reflected test fixtures require instance getters. Only those
specific analyzer rules are suppressed, with justifications in the test code.
Production findings are not hidden or excluded.

## Contact

[Linh Le on LinkedIn](https://www.linkedin.com/in/linh-le-258417105/)
