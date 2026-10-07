# Kkts.Expressions

Convert string expressions and structured filters into strongly typed LINQ
expression trees. Kkts.Expressions helps you build dynamic queries from UI
filters, including predicates, sorting, variables, and pagination.

The library targets `netstandard2.0` and is available on
[NuGet](https://www.nuget.org/packages/Kkts.Expressions).

## Contents

- [Installation](#installation)
- [Quick start](#quick-start)
- [Structured filters](#structured-filters)
- [Sorting](#sorting)
- [Variables](#variables)
- [Property validation and mapping](#property-validation-and-mapping)
- [Conditions and pagination](#conditions-and-pagination)
- [Supported operators](#supported-operators)
- [Parsing and validation behavior](#parsing-and-validation-behavior)
- [TimeSpan durations](#timespan-durations)
- [Binary plus in predicates (v3.0 only)](#binary-plus-in-predicates-v30-only)
- [Development notes](#development-notes)
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
| In | `Id in [1, 2, 3, 4]` or `Name in ['String1', 'String2']` | Number, string, Guid, DateTime, DateTimeOffset, TimeSpan, enum, nullable |
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
