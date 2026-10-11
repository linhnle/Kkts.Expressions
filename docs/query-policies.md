# Query policies

`QueryPolicy` is an explicit, immutable configuration for controlling the
complexity and field permissions of user-supplied queries. Bind it to an
`ExpressionSchema` with `ExpressionQueryContext`, then use that same
server-owned context for editor analysis and runtime construction. Editor
analysis is useful feedback, not an enforcement boundary: the server must
check the actual input it receives.

## Configure and opt in

The policy constructor accepts optional nonnegative integer limits. An
omitted limit is unlimited, and zero is a valid limit. Collection access is
allowed by default for compatibility with supported operations. An omitted
field operator rule leaves existing comparison operators available. An
explicitly empty operator set denies comparison use of that field.

```csharp
using System.Collections.Generic;
using Kkts.Expressions;

var schema = ExpressionSchema.FromType<Product>(
    validProperties: new[] { "Id", "Name", "Price" });

var policy = new QueryPolicy(
    maxExpressionLength: 4096,
    maxParenthesisDepth: 16,
    maxAtomicConditions: 64,
    maxInItems: 100,
    maxNavigationDepth: 3,
    allowCollectionAccess: false,
    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
    {
        ["Price"] = new[]
        {
            ComparisonOperator.Equal,
            ComparisonOperator.GreaterThan
        },
        ["Name"] = new[]
        {
            ComparisonOperator.Equal,
            ComparisonOperator.StartsWith
        }
    });

var queryContext = new ExpressionQueryContext(
    schema,
    policy,
    validProperties: new[] { "Id", "Name", "Price" });
```

`ExpressionQueryContext` snapshots its additional `validProperties` restriction
and combines it with the schema's restrictions. It does not grant access
forbidden by the schema or allowlist. Policy operator rules are snapshotted
when `QueryPolicy` is created; field paths are validated and resolved against
the schema when the context is created. In legacy schemas, rules supplied
through aliases of the same mapped member are intersected. In a
`QuerySchema<T>` public schema, policy keys and additional `validProperties`
entries use registered public names; internal member names are rejected, and
field and policy operator sets intersect on that public name.

Configuration with negative limits, null/whitespace field paths, null operator
sets, undefined `ComparisonOperator` values, duplicate paths, or unknown
schema members fails explicitly with an argument exception. Construct policy
and schema on the server; do not accept either from an untrusted request.

## Recommended starting limits

Opt in explicitly to `QueryPolicy.Recommended` for these starting limits:

| Setting | Recommended value |
| --- | ---: |
| Maximum expression length | 4096 UTF-16 units |
| Maximum parenthesis depth | 16 |
| Maximum filter-tree depth | 16 |
| Maximum atomic conditions | 64 |
| Maximum membership items | 100 |
| Maximum entity navigation depth | 3 |
| Entity collection access | Denied |
| Per-field operators | Unrestricted unless configured |

These are starting limits, not database cost guarantees. Existing APIs do not
silently use the recommended policy. Without an explicitly bound
`ExpressionQueryContext`, their documented behavior remains unchanged.

## API and compatibility

The context binds one entity schema, one query policy, and an optional
additional external-name allowlist for reuse across operations. Generic
context operations require the generic entity type to match
`ExpressionSchema.EntityType`; a mismatch is an argument error. Existing
`Interpreter`, filter, condition, and ordering overloads remain available and
do not inherit this policy implicitly. Applications opt in by retaining a
server-owned `ExpressionQueryContext` and using its policy-aware operations
consistently. The policy does not add query operators or mapping callback
support. It does enforce nested JSON filter trees and policy-aware
expression-to-tree conversion. See the
[nested-filter guide](nested-filters.md) for the JSON contract, condition
integration, and conversion boundaries.

## Static analysis, counting, and field binding

`ExpressionQueryContext.AnalyzeExpression` uses the context schema and policy
without constructing a predicate. It does not execute a `VariableResolver`,
read entity or variable getters, enumerate values, or call application
conversions/operators. Declare variable types with `ExpressionVariableSchema`
when editor diagnostics should include them; the declaration contains types
and member metadata, not runtime values. See the
[semantic-analysis guide](semantic-analysis.md) for schema, variable, and
diagnostic fundamentals.

With an expression-based public schema, navigation-depth and collection
policies inspect only fields written by the query author. A registered
projection is trusted application configuration: its internal navigation,
collection access, and method calls are opaque to policy traversal checks.
Apply appropriate restrictions when registering public fields; these limits do
not estimate the cost or provider translation of a selector. See the
[expression-field guide](expression-field-mapping.md) for the trust boundary
and registration example.

The text analyzer checks the original UTF-16 source length first. It then
streams the source using the existing quote and escape rules to reject
excessive open parentheses, comparison predicates, and literal membership
slots before tokenization. Parentheses inside quoted text do not count;
brackets and braces do not consume parenthesis depth. A quote that is not
closed remains active to end of input. Semantic binding then checks fields,
navigation, collection traversal, and normalized operator permissions.
Nested JSON trees are traversed iteratively and use their independent
`MaxFilterTreeDepth`; logical containers are not counted as source
parentheses.

| Limit | Counting rule |
| --- | --- |
| Expression length | Exact untrimmed .NET `string.Length`; whitespace, quotes, and escapes count, and supplementary characters use two UTF-16 units. |
| Parenthesis depth | Maximum simultaneous `(` outside quoted text, including grouping, NOT/function calls, and parenthesized membership lists. |
| Filter-tree depth | A condition leaf has depth zero; each supplied AND, OR, or NOT container adds one along its descendant path. Single-child groups and repeated NOT count; wrappers are not flattened. |
| Atomic conditions | One per comparison, membership operation, supported Boolean comparison-function call, standalone Boolean predicate, or nested-tree condition leaf. AND, OR, NOT, parentheses, and arithmetic add none. |
| Membership items | Each parsed list slot or tree collection slot before conversion or deduplication. Duplicates, nulls, and scalar references count; quoted commas remain within one text-list slot. A whole collection reference is checked after resolution. |
| Navigation depth | Canonical entity member-type transitions. `Customer.Address.City` is depth 2; `CreatedAt.Year` and `Name.Length` are depth 0. A collection member transition adds one level. |
| Collection access | Whether an entity collection member is referenced or traversed. Strings and variable collections are not entity collection navigation. |

For example, `NOT (Id = 1 OR Id = 2)` has two conditions, while
`Price + Discount > 10` has one. A standalone Boolean field counts once and
uses `ComparisonOperator.Equal` for its field permission. An incomplete
recognized comparison such as `Id =` reserves one condition; a missing
operand is not counted separately. A bare Boolean value followed by an
explicit comparison is counted only once.

Tree length accounting sums each leaf's field and canonical operator string,
literal string, exact JSON number token, variable path, and canonical `true`,
`false`, or `null` token. It does not charge JSON keys, delimiters, escaping,
or logical containers. Tree text and leaf counts participate in the same
condition budget as Where and legacy structured inputs; ordering retains its
separate length budget. A standalone Filter/FilterGroup keeps its documented
legacy group-depth behavior; internal adapter containers do not add tree
depth.

`MaxFilterTreeDepth` is independent from `MaxParenthesisDepth`. A tree
`AND -> NOT -> leaf` has depth two; adding another logical container makes
depth three. Setting parenthesis depth to zero does not reject a tree whose
tree-depth limit admits it.

Use `WithMaxFilterTreeDepth` to copy a policy without changing the existing
constructor signature:

```csharp
var treePolicy = QueryPolicy.Recommended.WithMaxFilterTreeDepth(8);
var unlimitedTreePolicy = new QueryPolicy().WithMaxFilterTreeDepth(null);
```

The default policy remains unlimited for tree depth; the recommended preset
sets it to 16. Zero admits a leaf but rejects every logical container.

Field paths are resolved through the schema's exact external-name mapping
before navigation and operator checks. Operator aliases and casing are
normalized to the existing `ComparisonOperator` identity (`= / ==`,
`<> / !=`, the existing string operators, and `IN / NOT IN`) before checking
permissions. Alias rules resolving to the same CLR member are intersected.
For field-to-field comparisons, both fields are checked; arithmetic operands
carry every contributing field to the enclosing comparison. NOT keeps the
underlying comparison identity. Schema `validProperties`, `canQuery` and
ancestor denials, the context's additional `validProperties`, navigation
limits, and collection restrictions are intersected: a policy never grants
access denied by another restriction.

`ExpressionSchema.PropertyMapping` supports exact external-name-to-entity-
path mappings, not computed mapping callbacks or prefix rewrites. The target
entity path is the canonical path used for policy checks. Internal member
access under a mapped scalar path must be explicitly represented and resolve
through the existing schema; mapping a source name does not make arbitrary
computed members queryable. Nested tree fields use the same mapping,
allowlist, navigation, collection, and normalized operator checks.

Computed CLR properties are application-owned opaque members. Analysis reads
their member metadata but never invokes their getter, and policy checks cover
the exposed property and visible path only; a getter may contain hidden
navigation, collection access, external calls, or provider-incompatible work.
Do not expose a computed member whose hidden behavior defeats the application's
query restrictions. Policies do not inspect or constrain implementation
details inside that getter.

## Diagnostics and incomplete editor input

Policy diagnostics use the existing UTF-16 source offsets. The first
diagnostic for each static budget stops further source analysis and returns
no tokens, with `IsTruncated` set, rather than returning a success-shaped
partial analysis. Field, operator, navigation, and collection diagnostics
are reported during semantic analysis; tokens and syntax completeness remain
available, but `IsSemanticallyValid` is false. Incomplete expressions still
receive any applicable policy checks that can be decided from the source and
declared metadata.

| Code | Location | Caller action |
| --- | --- | --- |
| `query-policy-expression-length-exceeded` | First excess UTF-16 suffix, beginning at the configured limit. | Shorten the input. |
| `query-policy-parenthesis-depth-exceeded` | The first `(` that exceeds the depth limit. | Flatten nested groups or calls. |
| `query-policy-condition-count-exceeded` | Excess comparison operator, or standalone Boolean token when applicable. | Remove conditions. |
| `query-policy-in-items-exceeded` | The first membership item beyond the limit. | Shorten the collection. |
| `query-policy-filter-tree-depth-exceeded` | The first AND/OR/NOT container beyond the tree-depth limit; `InputPath` is a JSON Pointer. | Reduce logical nesting or deliberately raise the tree-depth limit. |
| `query-policy-navigation-depth-exceeded` | The source field token that exceeds the configured entity depth. | Use a shallower permitted field. |
| `query-policy-collection-access-denied` | The source field token that traverses an entity collection. | Use a scalar field or explicitly allow supported collection access. |
| `query-policy-operator-denied` | The operator token whose normalized operator is not allowed. | Choose a permitted operator. |
| `query-policy-in-items-unverifiable` | The collection variable or structured Value whose runtime size would require query-backed enumeration. | Resolve to a bounded in-memory collection or change the finite item cap deliberately. |
| `query-policy-diagnostics-truncated` | A zero-length caret at the next omitted diagnostic. | Fix the visible issues and analyze again. |

Budget diagnostics include the configured limit and observed value where
available. Membership over-limit observations are lower bounds because
analysis stops at the first excessive item. Policy-aware analysis returns at
most 32 combined diagnostics; its truncation marker indicates that additional
reports were omitted. Policy denials do not suggest restricted field names.

Structured diagnostics use `Start = 0` and `Length = 0` and identify their
source with `InputPath`, for example `Filter.Operator`, `Filters[2].Value`,
`FilterGroup.Filters[0].Property`, `FilterGroups[1].Filters[0].Value`,
`/and/1/not/field`, `OrderBy`, or `OrderBys[0].Property`. Tree pointers are
RFC 6901 escaped and are relative to the tree root; a tree nested in
`ConditionOptions` is prefixed with `/FilterTree`. Limit diagnostics include the configured
limit and observed value when available; runtime enumeration failures also
indicate whether the observed value is only a lower bound. Inspect diagnostics
before consuming a predicate, ordered source, or condition result.

Example editor integration:

```csharp
var analysis = queryContext.AnalyzeExpression(
    "Price >= 10 AND Name = 'a = b'");

foreach (var diagnostic in analysis.Diagnostics)
{
    // Display diagnostic.Code, diagnostic.Message, and its source span.
}

if (!analysis.IsSemanticallyValid)
{
    // Do not treat editor analysis as authorization or server enforcement.
}
```

For variable-backed membership such as `Id in $ids`, metadata-only analysis
can validate the declared variable type and its element type, but cannot know
the resolved collection's runtime cardinality. Runtime policy enforcement
must check that value after resolution and before constructing the expression.
No variable resolver or collection is invoked by editor analysis.

## Runtime and structured enforcement

Use the same context for server construction. Its string entry points inspect
the actual input before tokenization or resolver work:

```csharp
var analysis = queryContext.AnalyzeExpression(userText);
var result = queryContext.ParsePredicate<Product>(userText, variableResolver);
if (!result.Succeeded)
{
    // Result is null on policy failure; consume diagnostics on the server.
    throw result.Exception;
}
var predicate = result.Result;
```

`BuildPredicate` is the throwing counterpart; `TryBuildPredicate` returns an
evaluation result whose policy failure has a null `Result`, stable diagnostics,
and a `QueryPolicyException`. Generic overloads require the requested type to
match the context schema. Direct property/operator/value calls account for the
property text, canonical operator spelling, and string value. Non-string
objects are never converted with `ToString()` for policy length accounting.

The context also accepts the existing structured shapes and keeps their
composition rules: one `Filter` is one predicate; a `Filter` sequence is
combined with AND; each `FilterGroup` combines its leaves with AND; a sequence
of groups is combined with OR. No recursive filter DTO is introduced.
For example:

```csharp
var filters = queryContext.TryBuildPredicate<Product>(
    new[]
    {
        new Filter { Property = "Price", Operator = ">=", Value = "10" },
        new Filter { Property = "Name", Operator = "startswith", Value = "A" }
    });
if (!filters.Succeeded) throw filters.Exception;

var groups = queryContext.TryBuildPredicate<Product>(
    new[]
    {
        new FilterGroup
        {
            Filters = new List<Filter>
            {
                new Filter { Property = "Price", Operator = ">", Value = "100" }
            }
        }
    });
if (!groups.Succeeded) throw groups.Exception;
```

A standalone Filter/flat sequence is at group depth zero. A `FilterGroup`
consumes one depth, independent of how many leaves it contains. Structured
length is the cumulative UTF-16 lengths of Property, Operator, and Value
strings, with no generated separators; condition count is one per leaf.
Membership text uses the existing list grammar, so quoted commas are part of
one item and duplicates are still counted before conversion.

Structured diagnostics identify their source rather than inventing text
offsets. Examples include `Filter.Value`, `Filters[2].Operator`,
`FilterGroup.Filters[0].Property`, and
`FilterGroups[1].Filters[0].Value`. Structured failures also return no usable
predicate. The async `TryBuildPredicateAsync` and `BuildPredicateAsync`
overloads preserve cancellation and use async variable resolution.

`ConditionOptions` can be processed through `queryContext.BuildCondition` or
`BuildConditionAsync`. These operations preflight Where, Filters, FilterGroups,
and ordering before resolving variables. Where plus all structured leaves
share the filter length and atomic-condition budgets; ordering has its own
length budget. An invalid condition exposes no partial predicates or
OrderByClause. Existing `ConditionOptions.BuildCondition` calls remain
policy-free.

For JSON trees, use context-aware decoding and construction rather than the
transport-only codec:

```csharp
var decodedTree = FilterTreeJson.TryDeserialize(json, queryContext, variables);
if (!decodedTree.Succeeded) throw new QueryPolicyException(decodedTree.Diagnostics);

var treeResult = queryContext.TryBuildPredicate<Product>(decodedTree.Result, variableResolver);
if (!treeResult.Succeeded) throw treeResult.Exception;
```

Policy-aware decoding checks logical depth before descending, counts
membership slots before materializing them, applies text/condition budgets as
leaves complete, and then validates all fields and typed values. Construction
revalidates the supplied tree independently before resolver execution. The
standalone `FilterTreeJson` codec and `FilterNode` extension methods have no
`QueryPolicy`; use the bound context for server-side policy enforcement.

Ordering clauses can be checked with `TryBuildOrderByClause` from string or
`OrderByInfo` entries, and `TryOrderBy` can apply them to the existing
`IQueryable`, `IEnumerable`, or runtime `IQueryable` source forms. Ordering
uses mapped field permissions, navigation depth, collection access, and its
applicable length budget. Comparison operator rules are not sorting
permissions. A failed sort result has no sorted-source `Result`.

```csharp
var condition = queryContext.BuildCondition<Product>(
    new ConditionOptions
    {
        Where = "Price >= 10",
        Filters = new[]
        {
            new Filter { Property = "Name", Operator = "startswith", Value = "A" }
        },
        OrderBys = new List<OrderByInfo>
        {
            new OrderByInfo { Property = "Price", Descending = true }
        }
    },
    variableResolver);
if (!condition.IsValid)
{
    // Inspect condition.Error.EvaluationResult.Diagnostics and do not use partial output.
}

var sorted = queryContext.TryOrderBy(source, "Price desc");
if (!sorted.Succeeded) throw sorted.Exception;
```

### Variable-dependent membership bounds

At runtime, a finite `MaxInItems` is checked immediately after a membership
variable is resolved and before collection expression construction. Supported
in-memory enumerables are read once into a bounded snapshot. At most N+1
elements are consumed: the N+1st rejects the input with observed value N+1
marked as a lower bound, and enumeration stops immediately. The enumerator is
disposed on success and failure; accepted snapshots are used by the resulting
predicate without re-enumerating the application sequence. Duplicate and null
values each consume one slot. Empty collections consume zero.

When a finite item cap would require enumerating an `IQueryable`, policy
enforcement fails with `query-policy-in-items-unverifiable`; it does not issue
a database query to discover the count. A caller may resolve a query into an
already bounded in-memory collection before passing it to the expression
builder. Without a finite `MaxInItems`, legacy supported collection behavior
is retained.

The context-aware JSON facade and tree traversal do not replace HTTP body,
request time, or application transport limits. Direct `JsonSerializer`
consumers must configure `JsonSerializerOptions.MaxDepth` as an explicit
transport cap; that setting is distinct from `MaxFilterTreeDepth`.

Metadata-only analysis can determine literal list slots and declared variable
types, but cannot know resolver availability, runtime values, collection size,
or whether a declaration will resolve to a query-backed object. These are
runtime checks, not editor errors. Editor analysis never invokes resolvers or
enumerates host-provided values.

## Server enforcement and security boundary

Apply the same server-owned `ExpressionQueryContext` to the actual expression
or structured filters received by the server. Never rely on editor
diagnostics, client-provided analysis results, or a predicate previously
analyzed from different input. A policy failure must not yield a usable
predicate.

Query policies limit which user-supplied expressions may be constructed and
how much supported expression processing they request. They do not provide
authorization, tenant isolation, database command timeouts, or database query
cost guarantees. Apply mandatory tenant and authorization predicates as
application-owned conditions outside the user expression. For example:

```csharp
var schema = ExpressionSchema.FromType<Data>(
    validProperties: new[] { "Id", "Name", "Price" });
var queryContext = new ExpressionQueryContext(schema, QueryPolicy.Recommended);
var evaluation = queryContext.ParsePredicate<Data>(userExpression, variableResolver);
if (!evaluation.Succeeded) throw evaluation.Exception;

var tenantScoped = db.Entities
    .Where(item => item.TenantId == currentTenantId)
    .Where(evaluation.Result);
```

Keep `TenantId` out of the user schema unless users independently need it.
The tenant condition remains application-owned and is not concatenated into
the user expression; a broad user `OR` cannot remove the earlier tenant
predicate. Query policies still do not replace authorization checks or
database/provider-level timeouts and cost controls. Existing calls without a
policy keep their documented behavior.
