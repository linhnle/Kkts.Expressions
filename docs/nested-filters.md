# Nested JSON filters

`FilterNode` models an immutable logical filter tree independently of EF Core
and UI frameworks. `FilterTreeJson` reads and writes the tree's strict JSON
contract. The model and codec preserve child order and repeated conditions.

The codec validates JSON shape and values only. It does not bind fields to an
entity schema, enforce query policies, resolve variables, or build a predicate.
Do not treat successful deserialization as authorization or as proof that a
query is safe to execute.

## JSON contract

Each node has exactly one shape:

```json
{ "and": [ { "field": "Status", "op": "=", "value": "Active" } ] }
```

```json
{ "or": [ { "field": "Priority", "op": ">=", "value": 3 }, { "not": { "field": "Deleted", "op": "=", "value": true } } ] }
```

```json
{ "not": { "field": "Department", "op": "in", "value": ["Archived", "External"] } }
```

```json
{ "field": "Name", "op": "contains", "value": "sample" }
```

Groups require at least one child; single-child groups are valid. `not`
contains one node object, not a list. Empty membership arrays are valid.
Condition keys are `field`, `op`, and `value`; JSON member names are
case-sensitive. Unknown and duplicate members, mixed node shapes, null group
children, missing values, unknown operators, and nested arrays are rejected.
An explicit `"value": null` is a valid null literal and differs from a missing
`value` member.

Here is the requested nested example:

```json
{
  "and": [
    {
      "or": [
        { "field": "Status", "op": "=", "value": "Active" },
        { "field": "Priority", "op": ">=", "value": 3 }
      ]
    },
    {
      "not": {
        "field": "Department",
        "op": "in",
        "value": ["Archived", "External"]
      }
    }
  ]
}
```

It represents `(Status = 'Active' OR Priority >= 3) AND NOT (Department IN
['Archived', 'External'])`. It is not the same predicate as
`(A OR B) AND NOT (C OR (D AND E))`; that expression requires a `not` whose
child is a nested `or` group.

## Values and variable references

| JSON form | `FilterValueKind` | Meaning |
| --- | --- | --- |
| `null` | `Null` | Explicit null literal |
| `true` / `false` | `Boolean` | Boolean literal |
| `3`, `-0.5`, `1e20` | `Number` | Exact JSON number token |
| `"text"` | `String` | Literal string |
| `["A", null, 3]` | `Collection` | Ordered flat membership values |
| `{"variable":"user.ids"}` | `Variable` | Explicit, unprefixed reference |

A string such as `"$user.name"` is always literal text. References use a
separate object and a non-empty dotted identifier path. An object with any
other shape is not a filter value. Arrays are allowed only for `in` and
`not in`, and each item must be a scalar literal or scalar reference. Nested
arrays and collection references inside an array are not supported. A
collection reference can be the whole value of a membership condition.

Numbers are retained as their original valid JSON token, so values such as
`18446744073709551615` and high-precision decimals do not pass through
`Double`. The tree does not carry CLR type tags: the eventual entity schema
determines whether a string is a string, character, enum, Guid, date/time, or
duration value and whether a number fits the target type.

The built-in `FilterValue.FromObject` factory explicitly encodes supported CLR
scalars and rejects arbitrary objects without calling their `ToString()`:

| CLR value | JSON-compatible representation |
| --- | --- |
| `string`, `char` | JSON string (`char` becomes one character) |
| `bool` | JSON Boolean |
| Integral and finite floating-point numbers, `decimal` | JSON number |
| `enum` | Its general enum format as a JSON string |
| `Guid` | `D`-format JSON string |
| `DateTime`, `DateTimeOffset` | Round-trip `O`-format JSON string |
| `TimeSpan` | Invariant constant-format JSON string |

These are wire encodings, not conversion guarantees for every entity type.
Schema-directed conversion and deterministic temporal rules belong to the
predicate/validation layer.

## Model factories

The factories make invalid or ambiguous in-memory shapes unavailable:

```csharp
using Kkts.Expressions;

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
```

`FilterNode.And`, `Or`, `Not`, and `Condition` snapshot their child inputs.
Groups reject empty and null children, retain order and duplicates, and accept
one child. `FilterValue.Number` accepts a valid JSON number token;
`FilterValue.Variable` takes an unprefixed path; `FilterValue.Collection`
accepts a flat list of `FilterValue` instances. `FilterNode.Condition` rejects
unknown operators and collection values for non-membership operators.

## Codec APIs

Use `TryDeserialize` when JSON comes from a caller so errors remain structured:

```csharp
var decoded = FilterTreeJson.TryDeserialize(
    "{\"field\":\"Id\",\"op\":\"=\",\"value\":3}");

if (!decoded.Succeeded)
{
    foreach (var diagnostic in decoded.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.InputPath}: {diagnostic.Message}");
    }
}
else
{
    string canonicalJson = FilterTreeJson.Serialize(decoded.Result);
}
```

`FilterTreeJson.Deserialize` throws `JsonException` for invalid input.
`FilterTreeJson.Serialize` writes deterministic member ordering and preserves
number tokens, references, array order, duplicates, and nulls. `FilterNode`
also has a dedicated `System.Text.Json` converter, so
`JsonSerializer.Serialize(tree)` and `JsonSerializer.Deserialize<FilterNode>(json)`
use this contract.

The facade reads and writes nested trees iteratively and does not interpret
the serializer's default depth as a filter-tree policy. When using
`JsonSerializer` directly, configure its transport depth explicitly:

```csharp
using System.Text.Json;

var jsonOptions = new JsonSerializerOptions { MaxDepth = 256 };
var json = JsonSerializer.Serialize(tree, jsonOptions);
var treeCopy = JsonSerializer.Deserialize<FilterNode>(json, jsonOptions);
```

`JsonSerializerOptions.MaxDepth` is a transport/serialization limit. It is not
the query-policy logical tree-depth limit. The no-context facade performs
strict shape checks only. Use the context-aware overload for server-side
schema and policy validation:

```csharp
var decoded = FilterTreeJson.TryDeserialize(json, queryContext, variableSchema);
if (!decoded.Succeeded)
{
    foreach (var diagnostic in decoded.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.InputPath}");
}
```

Context-aware decoding checks logical depth before descending, membership
cardinality before materializing excess items, and text/condition budgets as
conditions complete; it then validates the entire decoded tree against the
schema and policy. Policy failure returns no partial tree. Direct
`JsonSerializer` use and the no-context facade do not enforce an
`ExpressionQueryContext` policy.

Applications must still configure an appropriate transport depth for direct
`JsonSerializer` use and enforce their own HTTP body-size and request-time
limits. These transport limits are separate from logical tree depth.

## Diagnostics and JSON Pointers

Codec failures return no partial tree. Each `ExpressionDiagnostic` has a
stable `Code`, a human-readable `Message`, and an RFC 6901 `InputPath`. A root
error uses the empty pointer; a member such as `and` is `/and`; array items
include their numeric index. `/` and `~` in member names are escaped as `~1`
and `~0`.

Common structural/value codes include:

| Code | Example |
| --- | --- |
| `filter-tree-invalid-shape` | Mixed shapes, wrong member kind, or a missing `value` |
| `filter-tree-empty-group` | `{"and":[]}` or `{"or":[]}` |
| `filter-tree-null-child` | A null node inside a logical group |
| `filter-tree-unknown-member` | An unknown or incorrectly cased node key |
| `filter-tree-duplicate-member` | A repeated key in a JSON object |
| `filter-tree-invalid-value` | Nested arrays or an invalid value object |
| `filter-tree-invalid-json` | Malformed JSON, comments, or trailing root content |
| `filter-tree-unknown-operator` | An unsupported `op` string |

For example, a nested-array error in the first condition of an AND points to
`/and/0/value/1`. Tree diagnostics use zero text offsets because JSON pointer
locations, rather than expression-string spans, identify the offending input.
When a tree diagnostic is embedded in a condition object, the pointer prefix
is `/FilterTree`.

Restricted-field diagnostics must identify the external `field` location
without disclosing canonical member paths, CLR types, variable values, or
suggestions for denied fields. The codec itself does not have an entity schema
and therefore cannot determine field permissions.

## Predicate construction and validation

The `FilterNode` extensions bind fields against the entity type and emit
typed expression nodes directly. They do not convert the tree to expression
text or pass membership arrays through the legacy CSV parser:

```csharp
var resolver = new VariableResolver();
resolver.TryAdd("userId", 42);

var tree = FilterNode.And(new[]
{
    FilterNode.Condition("Status", "=", FilterValue.String("Active")),
    FilterNode.Condition("OwnerId", "=", FilterValue.Variable("userId"))
});

var attempt = tree.TryBuildPredicate<Order>(resolver);
if (!attempt.Succeeded)
{
    foreach (var diagnostic in attempt.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.InputPath}");
}
else
{
    Func<Order, bool> predicate = attempt.Result.Compile();
}
```

`BuildPredicate<T>` is the throwing form; runtime `Type` overloads and
asynchronous `BuildPredicateAsync` / `TryBuildPredicateAsync` variants are
also available. Failed evaluation results have no predicate. Metadata checks
run over the entire tree before any variable is resolved, so an unknown,
denied, or incompatible field prevents resolver execution. Runtime resolution
and conversion failures are reported with a value pointer and retain their
exception on the evaluation result.

Only `FilterValue.Variable` is resolved. `FilterValue.String("$userId")`
remains literal text even if the resolver contains `userId`. A reference
inside a membership array resolves to one scalar slot; a reference as the
whole membership value must resolve to an enumerable. Literal numbers,
Booleans, nulls, and collections are converted directly using the entity
field type. In particular, numeric JSON tokens are converted exactly, and
date/time strings with omitted defaults or offsets are rejected rather than
depending on the current machine clock or local time zone.

For a server-owned policy, validate and build through the bound
`ExpressionQueryContext`:

```csharp
var validation = queryContext.ValidateFilterTree(tree, variableSchema);
if (!validation.Succeeded)
{
    foreach (var diagnostic in validation.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.InputPath}");
}

var result = queryContext.TryBuildPredicate<Order>(tree, variableResolver);
if (!result.Succeeded)
{
    // Result is null; inspect Diagnostics and Exception.
}
```

Context construction independently enforces the configured policy on the
supplied tree before resolving values. Its generic and runtime-type
synchronous/asynchronous overloads return no partial predicate on failure;
throwing overloads carry policy diagnostics in `QueryPolicyException`.
Standalone tree extensions bind to the supplied entity type, optional
`validProperties`, and `propertyMapping`, but do not apply an
`ExpressionQueryContext` query policy.

### Query-policy accounting for trees

`MaxFilterTreeDepth` is independent of `MaxParenthesisDepth`. A condition leaf
has depth zero; each AND, OR, or NOT wrapper adds one to descendant depth.
Single-child groups and repeated NOT still count. For example,
`AND -> NOT -> leaf` has depth two. A parenthesis limit of zero does not reject
this tree when its independent tree-depth policy admits it.

For a tree, `MaxExpressionLength` sums each leaf's supplied field and canonical
operator text, literal string text, exact number token, variable path, and
canonical Boolean/null token lengths. JSON member names, array delimiters,
escaping, and logical wrappers do not contribute. Every condition leaf counts
once against `MaxAtomicConditions`; logical containers do not. Every
membership-array slot counts against `MaxInItems` before conversion or
deduplication, including duplicate, null, and scalar-reference slots.
Whole-collection references are bounded after resolution; query-backed
collections are rejected when a finite item cap is configured.

The default `QueryPolicy` leaves tree depth unlimited. The explicit
`QueryPolicy.Recommended` preset sets `MaxFilterTreeDepth` to 16. Use
`WithMaxFilterTreeDepth(int?)` to make an immutable copy without changing the
existing constructor signature. Tree diagnostics use RFC 6901 pointers such
as `/and/1/not/field`; when embedded in `ConditionOptions`, the prefix is
`/FilterTree`.

## Legacy filters and migration

`Filter`, `FilterGroup`, and their existing builders remain available and keep
their existing serialized property names (`Property`, `Operator`, `Value`,
and `Filters`). The nested-tree converter is attached only to `FilterNode`;
it does not change serialization of legacy filter DTOs.

Legacy filters represent AND within each `FilterGroup` and OR across a
sequence of groups. The tree can represent these predicates, as well as
arbitrary deeper nesting and NOT. There is no automatic conversion from a
legacy string-valued `Filter` to a typed tree: callers must decide whether a
legacy string is a literal, a variable, or a membership list and construct
the corresponding `FilterValue`. In particular, do not split or reinterpret
legacy values by guessing from their text.

The existing `ConditionOptions` inputs continue to operate as before. The
tree model and codec are additive. `ConditionOptions.FilterTree` is optional
and omitted from JSON when null. When supplied, its predicate is appended
after that surface's existing predicates; all populated inputs, including
`Where`, legacy filters, groups, and the tree, are combined with AND. The
non-policy `ConditionOptions` order remains Filters, FilterGroups, Where,
then FilterTree. `ExpressionQueryContext` keeps its established order of
Where, Filters, FilterGroups, then FilterTree. Policy-context conditions
preflight all inputs together and expose neither predicates nor ordering if
any supplied input fails.

```csharp
var options = new ConditionOptions
{
    Where = "Id > 0",
    Filters = new[] { new Filter { Property = "Name", Operator = "=", Value = "A" } },
    FilterTree = tree
};

var condition = queryContext.BuildCondition<Order>(options, variableResolver);
if (!condition.IsValid)
{
    foreach (var diagnostic in condition.Error.EvaluationResult.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.InputPath}");
}
```

Legacy inputs intentionally retain their entry-point-specific behavior.
Empty filter sequences, empty groups, null lists/groups/leaves, invalid values,
and failures are not normalized to the new tree's stricter rules. In
particular, legacy string values may be interpreted by the existing
type-dependent variable and membership parsing rules; a string that looks
like a variable remains ambiguous under those legacy APIs. New tree values
avoid that ambiguity by choosing `FilterValue.String(...)` or
`FilterValue.Variable(...)` explicitly.

To migrate a known legacy OR-of-AND predicate, construct the equivalent tree
with typed values rather than copying legacy strings blindly:

```csharp
var migratedGroups = FilterNode.Or(new[]
{
    FilterNode.And(new[]
    {
        FilterNode.Condition("Status", "=", FilterValue.String("Active")),
        FilterNode.Condition("Priority", ">=", FilterValue.Number("3"))
    }),
    FilterNode.And(new[]
    {
        FilterNode.Condition("OwnerId", "=", FilterValue.Variable("user.id"))
    })
});
```

This is an equivalent to legacy `FilterGroups` only when each old value is
mapped to the same typed literal or reference it previously meant. It is not
an automatic DTO conversion: membership CSV parsing, prefix-looking strings,
and schema-dependent legacy conversion must be handled deliberately.

## Expression editor exchange

Use `FilterExpression.Parse` and `FilterExpression.Format` for the
schema-aware, metadata-only conversion subset. `ExpressionQueryContext`
provides `ParseFilterTree` and `FormatFilterTree` when query-policy checks are
also required:

```csharp
var parsed = queryContext.ParseFilterTree(
    "(Status = 'Active' or Priority >= 3) and not (Department in ['Archived', 'External'])",
    variableSchema);
if (!parsed.Succeeded)
{
    foreach (var diagnostic in parsed.Diagnostics)
        Console.WriteLine($"{diagnostic.Code} at {diagnostic.Start}+{diagnostic.Length}");
}
else
{
    var formatted = queryContext.FormatFilterTree(parsed.Result, variableSchema);
    if (formatted.Succeeded)
    {
        string canonicalExpression = formatted.Result;
    }
}
```

Supported forms are ordered AND/OR/NOT, field-left comparisons with literal
or explicit variable values, flat literal/reference membership lists, a
whole collection reference for membership, existing field string functions,
and permitted bare Boolean entity fields. Parse results retain source UTF-16
spans for `filter-conversion-unsupported`; format diagnostics identify the
tree location with a JSON Pointer. Both APIs return no usable result on
failure. Formatting is deterministic and preserves child order and
precedence, but does not preserve whitespace or operator aliases.

Arithmetic, concatenated/computed operands, field-to-field or reversed
comparisons, entity-member membership sources, standalone Boolean variables
or constants, and unsupported function/value forms are rejected explicitly.
Formatting also rejects exponent-form numbers and prefix-looking literal
strings inside membership lists because the existing expression grammar
cannot guarantee identity preservation for them. Direct tree construction
still supports these values when its own schema/policy rules allow them.
Conversion never initializes a variable resolver, resolves references,
executes field getters, enumerates runtime values, or builds a predicate.
Declared variable metadata is used only for validation; references remain
references in both directions.

For semantic round trips, use the same entity schema, declared variable
metadata, immutable conversion context, and query policy. The existing text
grammar is not a universal encoding for every typed tree value; when spelling
cannot preserve the value or literal/reference distinction, formatting
returns an explicit unsupported diagnostic rather than changing meaning.
