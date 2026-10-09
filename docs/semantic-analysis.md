# Expression semantic analysis

Semantic analysis checks a syntactically parsed expression against an entity
schema and declared variable types. It is synchronous, metadata-only, and
independent of editor frameworks and EF Core.

## Three separate operations

- `Interpreter.AnalyzeExpression(text)` performs **syntax analysis** only. It
  returns source tokens, recoverable syntax diagnostics, and `IsComplete`.
- `Interpreter.AnalyzeExpression<T>(text, schema, variables)` performs syntax
  and **semantic analysis**. It also binds entity paths and declared variables,
  checks operand/operator types, and returns structured diagnostics.
- `ParsePredicate` and `ParsePredicateAsync` perform **runtime predicate
  construction**. They may resolve actual variable values and return an
  expression tree for application to LINQ. They remain the final runtime check.

Semantic analysis never builds or executes a predicate. A semantically valid
expression is not guaranteed to have available runtime values, avoid
value-dependent conversion failures, execute without application exceptions,
or translate to SQL for a particular EF Core provider.

## Entity schema, query permissions, and mappings

The schema reflects public readable instance properties and fields. Add
selective overrides for nullability and query permission; callers do not need
to duplicate every CLR member:

```csharp
using System;
using System.Collections.Generic;
using Kkts.Expressions;

public sealed class Product
{
    public decimal Price { get; set; }
    public decimal? Discount { get; set; }
    public string Name { get; set; } = "";
    public decimal InternalCost { get; set; }
    public Customer Customer { get; set; } = new Customer();
}

public sealed class Customer
{
    public string Name { get; set; } = "";
}

public static class ProductSchemaExample
{
    public static ExpressionSchema Create()
    {
        return ExpressionSchema.FromType<Product>(
            validProperties: new[] { "Price", "Discount", "Name", "InternalCost", "buyer" },
            propertyMapping: new Dictionary<string, string>
            {
                ["buyer"] = "Customer.Name"
            },
            properties: new[]
            {
                new ExpressionPropertyDefinition(
                    "InternalCost", typeof(decimal), canQuery: false),
                new ExpressionPropertyDefinition(
                    "Name", typeof(string),
                    nullability: ExpressionNullability.NonNullable)
            });
    }
}
```

Path lookup is case-insensitive. Nested paths use readable properties or
fields. Mapping is exact by full external path; it does not implicitly rewrite
path prefixes. A nonempty `validProperties` list limits external source names;
an omitted or empty list preserves the existing unrestricted-member behavior.
An explicit `canQuery: false` denial applies through aliases and nested paths.
Unknown paths and known restricted paths have distinct diagnostic codes.
Restriction diagnostics and suggestions do not disclose restricted canonical
names or types.

Value types and `Nullable<T>` have known nullability. Reference members default
to `ExpressionNullability.Unknown`; callers may provide an override. Schema
metadata must describe actual CLR members and matching CLR types. Reflection
reads member/type descriptors only and never calls property getters.

## Declared variable metadata

Variable declarations contain types, not values. Use reflected members when
appropriate, or declare a member tree explicitly:

```csharp
using System;
using Kkts.Expressions;

public sealed class UserMetadata
{
    public string Department { get; set; } = "";
}

public static class VariableSchemaExample
{
    public static ExpressionVariableSchema Create()
    {
        return new ExpressionVariableSchema(new[]
        {
            ExpressionVariableDefinition.FromType("user", typeof(UserMetadata)),
            new ExpressionVariableDefinition("minimum", typeof(decimal)),
            new ExpressionVariableDefinition("prices", typeof(decimal[])),
            new ExpressionVariableDefinition(
                "explicitUser",
                typeof(object),
                members: new[]
                {
                    new ExpressionVariableDefinition("department", typeof(string))
                })
        });
    }
}
```

`FromType` describes public instance members without invoking getters.
Explicit `$` references bind only to declared variables; a missing root is
`undeclared-variable`, while a missing member below a declared root is
`unknown-variable-member`. An exact dotted declaration can describe that path
without declaring a traversable root. Array and generic enumerable types
provide element-type metadata without enumerating runtime values.

## Conversion context

Analysis copies the supplied culture and date-format strings into an immutable
snapshot. Without an explicit context, it uses invariant culture and the
library's standard six explicit date formats. Numeric language literals remain
invariant-culture:

```csharp
using System.Globalization;
using Kkts.Expressions;

public static class ConversionContextExample
{
    public static ExpressionSchema Create()
    {
        var context = new ExpressionConversionContext(
            CultureInfo.InvariantCulture,
            new[] { "yyyy-M-d", "yyyy/M/d", "d/M/yyyy" });

        return ExpressionSchema.FromType<Product>(
            conversionContext: context);
    }
}
```

Runtime conversion defaults remain unchanged. A temporal literal that requires
the current date or local time zone receives
`context-dependent-conversion`; analysis does not fabricate an implicit date
or offset. Use a complete explicit date and, where relevant, an explicit
offset. The analysis conversion context is not a guarantee that runtime
construction will use the same ambient defaults unless the application
configures them accordingly.

Common extended ISO and compact calendar forms are also validated using
invariant machine formats, independently of the custom-format snapshot.
For example, `Created = '20261009'` is a complete date, not a value requiring
today's date; `CreatedOffset = '20261009T152646+0700'` includes a complete date
and an explicit offset. `CreatedOffset = '20261009T152646'` still receives
`context-dependent-conversion`. Zoned machine-format validation does not
convert the instant to local `DateTime`. See the
[runtime format and timezone rules](../README.md#datetime-and-datetimeoffset-strings)
for supported precision and suffixes; runtime culture precedence remains
different from the fixed schema context.

## Analyze an expression

The generic and runtime-type overloads are:

```csharp
ExpressionSemanticAnalysisResult Interpreter.AnalyzeExpression<T>(
    string expression,
    ExpressionSchema schema,
    ExpressionVariableSchema variables = null);

ExpressionSemanticAnalysisResult Interpreter.AnalyzeExpression(
    string expression,
    Type entityType,
    ExpressionSchema schema,
    ExpressionVariableSchema variables = null);
```

The runtime type must match `schema.EntityType`. An omitted variable schema
means no declared variables. The API has no resolver argument and performs no
I/O.

```csharp
var schema = ProductSchemaExample.Create();
var variables = VariableSchemaExample.Create();
var analysis = Interpreter.AnalyzeExpression<Product>(
    "Price > 'abc' and UnknownField = 1",
    schema,
    variables);

foreach (var diagnostic in analysis.Diagnostics)
{
    Console.WriteLine(
        $"{diagnostic.Code} at {diagnostic.Start}+{diagnostic.Length}: " +
        diagnostic.Message);
    foreach (var type in diagnostic.ExpectedTypes)
        Console.WriteLine($"Expected: {type.ClrType}");
    foreach (var suggestion in diagnostic.Suggestions)
        Console.WriteLine(
            $"Replace {suggestion.Start}+{suggestion.Length} " +
            $"with {suggestion.ReplacementText}");
}
```

For decimal `Price`, the example reports `incompatible-operand` on `'abc'`
with expected CLR type `Decimal` and actual type `String`, then
`unknown-property` on `UnknownField`. It does not guess a value correction for
`'abc'`.

The result exposes read-only snapshots:

| Member | Meaning |
| --- | --- |
| `SyntaxAnalysis` | Existing syntax-analysis snapshot |
| `Tokens` | Original source classifications, identical to `SyntaxAnalysis.Tokens` |
| `SyntaxDiagnostics` | Existing positioned syntax errors, unchanged |
| `SemanticDiagnostics` | Metadata-binding and type-checking diagnostics |
| `Diagnostics` | Combined source-ordered syntax and semantic diagnostics |
| `IsComplete` | Syntax completeness only; exactly the syntax contract |
| `IsSemanticallyValid` | Complete syntax, no semantic errors, Boolean predicate result |

Each combined `ExpressionDiagnostic` exposes `Kind`, `Code`, `Message`,
`Start`, `Length`, `ExpectedTypes`, `ActualTypes`, and `Suggestions`. A
`ExpressionTypeInfo` identifies `Clr`, `Null`, or `Unknown` values and carries
the CLR type when known, nullability, and optional collection element type.
Type collections are empty where a type is not safe to disclose or is not
applicable. A correction suggestion carries explanatory text, replacement
text, and its original-input replacement span.

## Stable diagnostic codes

Semantic codes are stable machine-readable identifiers. Messages are for
people; do not parse their wording.

| Code | Meaning | Type information / correction |
| --- | --- | --- |
| `unknown-property` | Entity path is not declared by the CLR type or mapping | May provide one unique, permitted close-name correction |
| `property-not-queryable` | Known entity path is not permitted by schema policy | No restricted type details or correction |
| `incompatible-operand` | Literal/operand cannot convert to the contextual type | Expected and actual types when known |
| `operator-not-applicable` | Known operand types do not support this operator | Expected and actual types when known |
| `undeclared-variable` | Variable root/reference was not declared | No runtime resolver lookup |
| `unknown-variable-member` | Member path is absent below a declared typed root | No runtime member getter |
| `predicate-result-not-boolean` | Complete expression does not produce Boolean | Expected Boolean and actual result type |
| `context-dependent-conversion` | Temporal text depends on date/time-zone machine defaults | Expected target and actual String type; message asks for explicit source information |

Suggestion spans and diagnostics use zero-based UTF-16 offsets into the exact,
unmodified .NET input string, with half-open ranges
`[Start, Start + Length)`. Slicing with `Substring(Start, Length)` identifies
the original text. A missing-token syntax diagnostic at end-of-input has
`Start == expression.Length` and `Length == 0`. Semantic corrections are
offered only for a unique, conservative, permitted property-name match;
ambiguous matches and speculative value/operator changes receive no
suggestion.

Syntax codes remain those returned by syntax analysis, including
`unknown-text`, `unexpected-token`, `missing-operand`,
`unmatched-delimiter`, `unterminated-string`, and
`incomplete-identifier`. Syntax and semantic codes remain separate in
`SyntaxDiagnostics` and `SemanticDiagnostics`.

## Incomplete input and editor consumption

Analyze each text snapshot; results are immutable and do not retain editor
state. `IsComplete` remains syntax-only. On incomplete or malformed text,
syntax diagnostics are preserved and semantic checks avoid treating a missing
operand, unfinished path, or unterminated literal as a valid value. Complete
independent clauses separated by top-level logical operators can still report
semantic diagnostics when their token range has no syntax error. A clause
overlapping a syntax error is skipped; an incomplete nested scope can therefore
suppress analysis of its containing clause. A failed child suppresses
diagnostics that depend on its type, while independent operands and clauses
remain diagnosable.
Blank input has no tokens or diagnostics and both validity flags are false.

```csharp
var text = editor.Text;
var result = Interpreter.AnalyzeExpression<Product>(
    text,
    schema,
    variables);

foreach (var token in result.Tokens)
{
    var tokenText = text.Substring(token.Start, token.Length);
    RenderEscapedText(tokenText, token.Kind);
}

foreach (var diagnostic in result.Diagnostics)
{
    ShowMarker(diagnostic.Start, diagnostic.Length, diagnostic.Message);
    foreach (var suggestion in diagnostic.Suggestions)
        OfferReplacement(
            suggestion.Start,
            suggestion.Length,
            suggestion.ReplacementText);
}
```

Render the original expression as escaped text, never raw HTML. Use a caret for
zero-length end positions rather than underlining a character past the input.
Only apply an explicit suggestion replacement; do not normalize or reconstruct
the editor buffer from tokens.

The editor object and rendering/replacement functions in the snippet are
supplied by the host application; they are not library APIs.

## Purity and query-provider boundary

Schema and variable metadata describe CLR types and member paths without
runtime values. Analysis does not execute variable resolvers or their
initialization hooks, entity/variable getters, compiled expressions, custom
conversions/operators, database queries, collection enumeration, or external
service calls. A declared variable can be semantically valid even when no
runtime value is currently available. Runtime predicate construction must
still perform runtime resolution and can fail for unavailable or
value-incompatible values.

Semantic validity is provider-independent. It does not validate whether an
EF Core provider can translate a predicate to SQL, account for database
collation/provider behavior, or replace application authorization when the
predicate is later executed.
