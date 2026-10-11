# Expression-based public query fields

Use a typed `QuerySchema<T>` when an API should expose a deliberate set of
query fields, including computed business values, without making every entity
member addressable. Building this schema opts into restrictive public-field
mode; it extends the existing `ExpressionSchema` used by semantic analysis and
`ExpressionQueryContext`.

The examples use an application `Order` model with these relevant members:

```csharp
using System;

public sealed class Order
{
    public int Id { get; set; }
    public Customer Customer { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Customer
{
    public string Name { get; set; }
}
```

## Quick start

```csharp
using Kkts.Expressions;

var schema = new QuerySchema<Order>()
    .Field("customerName",
        order => order.Customer == null ? null : order.Customer.Name,
        displayName: "Customer",
        description: "The customer's public name",
        nullability: ExpressionNullability.Nullable)
    .Field("total", order => order.UnitPrice * order.Quantity,
        canSort: false)
    .Field("createdAt", order => order.CreatedAt)
    .Build();

var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());
```

The selector's C# type determines the field result type: `customerName` is
`string`, `total` is `decimal`, and `createdAt` is `DateTime`. Selectors remain
expression trees; registration does not compile or execute them.

## Registration and metadata

`Field<TResult>` accepts a strongly typed `Expression<Func<T, TResult>>`.
Direct property access, nested access, and supported computed expressions
therefore receive compiler checking for entity members and operand types. For
example, changing `Order.UnitPrice` breaks compilation of the total selector
instead of leaving a stale string mapping.

Supported selector nodes include readable member access, constants, the
entity parameter, scalar-returning method calls, arithmetic/comparison/logical
binary expressions, coalesce, conversions, unary operations, conditionals,
and default values. The resulting field must be a scalar type supported by the
library. Invocation nodes, nested lambdas, blocks, assignments, dynamic or
extension nodes, object construction/initialization, indexers, free parameters,
and collection/object result fields are rejected during registration.

Application method calls in a selector are accepted as trusted configuration;
the library never invokes them to inspect metadata. This does not imply that
an EF Core provider can translate them. Avoid selectors that capture mutable
state or have side effects: captures keep normal expression-tree execution
semantics and are not evaluated or snapshotted by schema creation.

Inspect public metadata in registration order through `schema.Fields`. It
contains each public name, result CLR type, nullability, filter/sort
permissions, declared allowed operators, display name, and description. Its
collections are read-only snapshots. Selector bodies, canonical member paths,
and captured values are not exposed by default. A bound
`ExpressionQueryContext` exposes `PublicFields` with effective permissions and
the schema/policy operator intersection; it is empty for legacy schemas. This
is suitable for application-generated documentation without exposing mapping
expressions or internal member names.

## Permissions and query policies

Filtering and sorting permissions are independent and default to enabled.
Disable sorting for an expensive computation with `canSort: false`, or disable
filtering with `canFilter: false`. An omitted `allowedOperators` set permits
the existing applicable filter operators; an empty set denies comparison
filtering but does not disable sorting. Query-policy operator restrictions
intersect with field restrictions, while policy navigation/collection limits
apply to client-authored paths and do not inspect trusted mapping bodies.

Additional public-name restrictions and operator policy keys must refer to
registered names and can only narrow the schema. Public schemas expose only
their explicitly registered names, even when empty. Names are
case-insensitive, and member paths cannot be appended: register
`createdYear` explicitly rather than querying `createdAt.Year`. Internal
members used by a selector do not become independently queryable. Use `$name`
for variables in public mode; bare identifiers are fields and never fall back
to variable resolution.

Keep authorization, tenant predicates, and query-cost controls in application
code. A trusted selector's hidden navigation, collection access, or method
cost is the application's responsibility.

## Use one context across query surfaces

Use the same schema-bound context for semantic analysis and runtime
construction. String, direct, flat, grouped, nested-tree, and condition
operations all resolve the same registered names and permissions:

```csharp
var analysis = queryContext.AnalyzeExpression("total > 100");
var textPredicate = queryContext.ParsePredicate<Order>("total > 100");
if (!analysis.IsSemanticallyValid || !textPredicate.Succeeded)
{
    throw new InvalidOperationException("The public query is invalid.");
}

var directPredicate = queryContext.TryBuildPredicate<Order>(
    "total",
    ComparisonOperator.GreaterThan,
    100m);
var filtersPredicate = queryContext.TryBuildPredicate<Order>(new[]
{
    new Filter { Property = "total", Operator = ">", Value = "100" },
    new Filter { Property = "customerName", Operator = "contains", Value = "North" }
});
var groupPredicate = queryContext.TryBuildPredicate<Order>(new FilterGroup
{
    Filters = new List<Filter>
    {
        new Filter { Property = "total", Operator = ">", Value = "100" },
        new Filter { Property = "createdAt", Operator = ">=", Value = "2024-01-01" }
    }
});
var treePredicate = queryContext.TryBuildPredicate<Order>(FilterNode.And(new[]
{
    FilterNode.Condition("total", ">", FilterValue.Number("100")),
    FilterNode.Condition("customerName", "contains", FilterValue.String("North"))
}));

var condition = queryContext.BuildCondition<Order>(new ConditionOptions
{
    Where = "total > 100",
    OrderBy = "createdAt desc"
});
if (!condition.IsValid)
{
    throw new InvalidOperationException("The condition is invalid.");
}
```

Check each evaluation's `Succeeded` value before consuming its `Result`;
failed construction returns no predicate. Sorting uses `canSort`, not
`canFilter`, so the filter-only `total` field above remains unavailable to
ordering while `createdAt` can be sorted. The
[nested-filter guide](nested-filters.md) describes the JSON tree contract and
its public-field binding rules.

Multi-key ordering also uses registered selectors and keeps each key's
inferred CLR type:

```csharp
var orderingSchema = new QuerySchema<Order>()
    .Field("createdAt", order => order.CreatedAt)
    .Field("id", order => order.Id)
    .Build();
var orderingContext = new ExpressionQueryContext(orderingSchema, new QueryPolicy());
var orderBy = orderingContext.TryBuildOrderByClause("createdAt desc, id asc");
if (!orderBy.Succeeded)
{
    throw orderBy.Exception;
}

IOrderedQueryable<Order> databaseOrder = orderBy.Result.Sort(queryableOrders);
IOrderedEnumerable<Order> memoryOrder = orderBy.Result.Sort(orders);
```

Queryable composition quotes typed lambdas and does not compile or invoke the
selector. Enumerable ordering compiles the final key delegate when `Sort` is
applied, but the key is not executed until enumeration.

## Nullability and C# semantics

Nullable value-type results remain nullable; nonnullable value types remain
nonnullable. Reference results default to unknown nullability unless a
registration explicitly declares metadata. This metadata does not add runtime
guards. An unguarded `order.Customer.Name` selector retains ordinary C#
null-reference behavior. Use a conditional or coalesce when the public result
must be null-safe, for example:

```csharp
.Field("customerName",
    order => order.Customer == null ? null : order.Customer.Name,
    nullability: ExpressionNullability.Nullable)
```

The selector's operations, conversions, numeric promotions, checked behavior,
and conditional branches are preserved. Query values are converted against
the selector's result type. Multiplication inside `total` does not add
multiplication syntax to the user's query language.

## Compatibility and migration

Existing `ExpressionSchema.FromType`, string-to-string `propertyMapping`,
property restrictions, legacy APIs, and their reflected-member behavior are
unchanged unless the application builds and supplies a `QuerySchema<T>`.
Public mode rejects mixed legacy mappings and canonical-member overrides
rather than guessing their precedence. Replace each intended external alias
with a typed registration; register explicit scalar projections for paths
that clients previously appended. Use one schema-bound `ExpressionQueryContext`
for analysis and runtime construction so field metadata and permissions agree.

## SQL translation limitations

Strong typing proves that a selector references valid C# members and has a
valid C# result type. Registration validates supported expression-tree shapes.
Semantic analysis checks query fields, operators, and values from metadata.
None of those guarantees that SQL Server, MySQL, or another provider can
translate the expression.

For example, this is a valid typed selector and query configuration:

```csharp
using System;
using System.Linq.Expressions;
using Kkts.Expressions;

public static class BusinessCalculations
{
    public static decimal CalculateBusinessTotal(decimal unitPrice, int quantity)
        => unitPrice * quantity;
}

public static class PublicOrderQueryExample
{
    public static Expression<Func<Order, bool>> CreatePredicate()
    {
        var schema = new QuerySchema<Order>()
            .Field(
                "businessTotal",
                order => BusinessCalculations.CalculateBusinessTotal(
                    order.UnitPrice,
                    order.Quantity))
            .Build();
        var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());
        var analysis = queryContext.AnalyzeExpression("businessTotal > 100");
        var predicate = queryContext.ParsePredicate<Order>("businessTotal > 100");
        if (!analysis.IsSemanticallyValid || !predicate.Succeeded)
            throw new InvalidOperationException("Invalid public query.");
        return predicate.Result;
    }
}
```

Registration validity (the selector compiles and passes the supported-shape
validator), semantic validity (`analysis.IsSemanticallyValid`), successful
predicate construction (`predicate.Succeeded`), and relational translation
are separate checks. A provider without a translation for
`CalculateBusinessTotal` rejects a query filtering or sorting on this field.
The integration suite verifies construction and semantic validity, then
expects provider execution to fail without invoking the application method.
The library does not add provider-specific translation, execute selectors as
a client-side fallback for `IQueryable`, or promise translation of arbitrary
mappings. Validate representative expressions against every provider and
version your application supports.

The SQL Server and MySQL integration suite exercises nested `customerName`,
computed decimal `total`, nullable `optionalTotal`, guarded nullable
`optionalParentName`, filtering, multi-key sorting, and filter-plus-sort
conditions. It compares generated results to handwritten LINQ and asserts
expected IDs. Those cases demonstrate the tested selector subset, not a
translation guarantee for all expressions on either provider.

`IQueryable` ordering composes the selector into a typed expression tree.
`IEnumerable` ordering must compile the final key selector when sorting is
applied because LINQ-to-Objects consumes delegates; registration,
metadata-only analysis, and ordering-clause construction do not compile or
execute the selector.
