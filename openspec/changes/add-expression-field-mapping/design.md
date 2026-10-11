# Design

## Context

See [proposal.md](proposal.md) for motivation and the [field mapping](specs/expression-field-mapping/spec.md) and [policy](specs/query-policies/spec.md) deltas for the behavioral contract.

Observed implementation:

- [ExpressionSchema](../../../src/Kkts.Expressions/ExpressionSchema.cs) is sealed and immutable. It reflects members, snapshots case-insensitive exact string mappings/allowlists and canonical `ExpressionPropertyDefinition` overrides, and shares nullability and conversion metadata with analysis. Its `Properties` view represents overrides, not an exhaustive public-field catalog.
- [ExpressionQueryContext](../../../src/Kkts.Expressions/ExpressionQueryContext.cs) already binds a schema, policy, and additional allowlist. It covers string/direct/flat/group/tree predicates, conditions, ordering, and metadata analysis. It currently transfers string mappings to `BuildArgument` and treats filtering/ordering as one query permission.
- [BuildArgument](../../../src/Kkts.Expressions/Internal/BuildArgument.cs), the [property node](../../../src/Kkts.Expressions/Internal/Nodes/Property.cs), and flat/async filter builders turn names into member paths. The [tree builder](../../../src/Kkts.Expressions/Internal/FilterTreePredicateBuilder.cs) and validator separately resolve schema paths. These all need one schema-backed field contract, not text rewriting for computed expressions.
- [ExpressionSemanticAnalyzer](../../../src/Kkts.Expressions/Internal/ExpressionSemanticAnalyzer.cs) binds mapped CLR paths and falls back to declared bare variables. Policy operator keys currently canonicalize aliases to CLR paths. Those behaviors remain correct for legacy mode but not public mode.
- [OrderByParser](../../../src/Kkts.Expressions/Internal/OrderByParser.cs) retains string paths; [OrderByClause](../../../src/Kkts.Expressions/OrderByClause.cs) rebuilds keys from them. Queryable ordering quotes expression keys; enumerable ordering compiles keys when applied.
- [RelationalTestModel](../../../src/Kkts.Expressions.EntityFrameworkCore.Tests/RelationalTestModel.cs) already supplies required Parent navigation, numeric and nullable values. [RelationalDatabaseFixtures](../../../src/Kkts.Expressions.EntityFrameworkCore.Tests/RelationalDatabaseFixtures.cs) seed five deterministic records in SQL Server/MySQL. The existing [assertions](../../../src/Kkts.Expressions.EntityFrameworkCore.Tests/RelationalPredicateIntegrationTests.cs) execute generated and handwritten queries and assert IDs.
- Semantic analysis and nested filter trees are implemented but their OpenSpec changes remain active. Main query policy specs prohibit lambda registration and assume canonical-path permissions. The policy delta explicitly scopes those assumptions to legacy mode; later synchronization of active changes must not overwrite the exception.

## Goals / Non-Goals

**Goals:** One immutable schema and one shared field binding/composition path, unchanged legacy branches, real selector/key types, independently checkable permissions, and public metadata with no accidental internal path disclosure.

**Non-Goals:** A separate query engine/schema hierarchy, text serialization of selector bodies, partial evaluation, universal expression-tree support, provider translation adapters, or changes to existing user expression arithmetic/operators. Multiplication in a trusted C# selector does not add multiplication syntax to the query language.

## Decisions

### 1. Add a typed builder, not a second schema model

Use an additive `QuerySchema<T>` builder that owns registration data and returns an `ExpressionSchema` snapshot through `Build()`. Extend `ExpressionSchema` with an explicit `IsPublicSchema` flag and separate public `Fields` metadata, leaving `Properties`, `PropertyMapping`, and `FromType` semantics unchanged.

Proposed public shape, following existing optional named-argument conventions:

```csharp
public QuerySchema<T> Field<TResult>(
    string name,
    Expression<Func<T, TResult>> selector,
    bool canFilter = true,
    bool canSort = true,
    IEnumerable<ComparisonOperator> allowedOperators = null,
    string displayName = null,
    string description = null,
    ExpressionNullability nullability = ExpressionNullability.Unknown);

public ExpressionSchema Build(
    ExpressionConversionContext conversionContext = null);
```

No implicit conversion, mutable schema inheritance, mapping callback, or parallel query context is needed. Build copies registration/operator data; subsequent builder additions do not change built schemas. Reuse the existing conversion context and scalar/nullability knowledge.

Complete intended application usage:

```csharp
using System;
using System.Linq;
using Kkts.Expressions;

public sealed class Customer
{
    public string Name { get; set; } = "";
    public int Rank { get; set; }
}

public sealed class Order
{
    public Customer Customer { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class PublicOrderQueries
{
    public static IQueryable<Order> Apply(IQueryable<Order> source)
    {
        var schema = new QuerySchema<Order>()
            .Field("customerName",
                x => x.Customer == null ? null : x.Customer.Name,
                displayName: "Customer", description: "Public customer name",
                nullability: ExpressionNullability.Nullable)
            .Field("total", x => x.UnitPrice * x.Quantity,
                canSort: false,
                allowedOperators: new[] {
                    ComparisonOperator.Equal, ComparisonOperator.GreaterThan })
            .Field("createdAt", x => x.CreatedAt)
            .Build();

        var context = new ExpressionQueryContext(schema, new QueryPolicy());
        var predicate = context.ParsePredicate<Order>("total > 100");
        if (!predicate.Succeeded)
            throw new InvalidOperationException(
                "Invalid public query; inspect diagnostics.", predicate.Exception);

        var ordered = context.TryOrderBy(
            source.Where(predicate.Result), "createdAt desc, customerName");
        if (!ordered.Succeeded)
            throw new InvalidOperationException(
                "Invalid public ordering; inspect diagnostics.", ordered.Exception);
        return ordered.Result;
    }
}
```

Alternative: changing the existing reflected schema to restrictive by default would break current callers. Alternative: exposing the builder itself as a second runtime schema would duplicate policies and semantic metadata.

### 2. Resolve fields once, with mode-specific identity

Introduce one internal resolved-field descriptor within existing schema infrastructure. It carries external identity, result CLR type/nullability, operation permissions, declared operator metadata, and either a legacy canonical path or a retained selector. Keep selector/path details internal to the implementation.

`ExpressionSchema` resolves a source name to that descriptor:

- Legacy branch keeps exact mapping, readable-member traversal, canonical ancestor denials, canonical policy identity, and current scalar-member navigation behavior.
- Public branch resolves a case-insensitive registered identifier only. Its identity is the registered name, not its expression body. Empty public schemas deny all bare fields. Internal members are not reflected to decide whether an unregistered query name is "known"; it is simply `unknown-property`.
- For public mode, field suffix traversal is unavailable. Existing string comparison functions still parse against the registered receiver. Validate names through existing grammar/tokenization rather than inventing regex rules; reject reserved/colliding identifiers.
- Public bare-name variable fallback is disabled in both syntax-to-runtime operand classification and semantic binding. Explicit `$` references remain supported. Denied fields are never candidates for fallback.

Use this descriptor in semantic binding, property nodes, direct/flat/async filters, tree validators/builders/scanners, expression/tree exchange, and context conditions. Keep low-level reflection helpers for legacy calls without a schema.

Alternative: translating a computed field to another string cannot preserve multiplication, null guards, conversions, or provider-compatible parameter identity. Alternative: per-entry-point dictionaries would risk policy bypasses and type drift.

### 3. Validate a bounded selector subset without execution

At `Field` registration validate the complete expression structurally before adding it. The public generic signature handles ordinary entity/result mismatches; programmatically constructed trees still require parameter/type/shape validation. Use a visitor with an explicit supported-node whitelist from the spec and existing scalar type classification for the final result.

Keep member receiver and method argument intermediates typed, even when they are nonscalar; only the exposed result must be scalar. Preserve `MethodInfo`, conversion nodes, checked/nullable operator flags, and conditionals. Reject free parameters, invocation, nested lambda/quote, assignments/blocks, dynamic/extension nodes, construction/initialization, and indexers. Validation must not reduce arbitrary extension nodes, obtain captured property values, call methods, or execute user conversions.

Static method/member references and closure member accesses in accepted trees are trusted configuration. Retain them, do not read or snapshot their values. Immutable schema metadata does not imply immutable captured state: application mutation retains ordinary expression-tree semantics at query execution. Document avoiding mutable captures and side-effectful configuration.

Alternative: only accepting member chains would miss the requested business computations. Alternative: accepting every expression shape would imply unsupported composition/translation and complicate safe structural inspection. Accepting method calls is intentional so valid-but-untranslatable selectors can be distinguished from registration errors.

### 4. Substitute parameters while preserving selector semantics

Provide a shared descriptor composition operation that substitutes the selector's root `ParameterExpression` by identity with the operation parameter. Use an `ExpressionVisitor`; do not replace by name, compile delegates, use `Expression.Invoke`, or evaluate constants/closures. For legacy descriptors call the existing member-access construction.

Route the property node, `Interpreter` direct builders, `FilterExtensions`/`FilterAsyncExtensions`, and both tree builder paths through it. Preserve flat group AND/OR, tree short-circuiting/NOT, shared condition parameters, membership, cancellation, and runtime conversion behavior.

The result type comes from `Expression<Func<T,TResult>>`, including C# compiler-inserted promotions for Decimal times Int32. Do not run selector arithmetic through the query language's narrower additive promotion logic. Surrounding query comparisons/arithmetic use existing rules against the result type.

Nullability uses `Nullable<T>` and existing reference-unknown defaults. Optional reference claims are metadata only. Preserve explicit guard/coalesce/conversion semantics. For nullable navigation, recommend `x => x.Customer == null ? (int?)null : x.Customer.Rank`. Do not infer nullable reference annotations using new dependencies and do not silently null-propagate unguarded selectors.

Alternative: injecting null guards would change result/key types and differ from handwritten C#. Alternative: stripping conversions to "find the property" corrupts computations and key types.

### 5. Bind public permissions without looking inside trusted mappings

Add operation-aware admission (filter versus sort) using the shared descriptor. Use `property-not-queryable` with operation-specific messages for registered denials, `unknown-property` for unregistered names, and existing normalized operator/type/value diagnostics.

For public fields, intersect declared operators, policy public-name operators, and additional allowlists; never inspect internal operands for permission keys. Two intentionally registered projections of one member have independent permissions. For legacy fields, retain canonical alias intersections and ancestor denials. Sorting ignores comparison sets and filtering permission.

As confirmed by the user, trusted expression bodies are opaque to navigation/collection budgets. A public scalar projection can be allowed under depth zero and collection denial even if its body reads internal navigation/Count. Document that public field selection is an application design decision, not automatic cost or authorization protection.

Keep existing maximum lengths, condition budgets, memberships, diagnostic bounds, and structured location rules. Budget client input, not selector node count or internal business arithmetic. A public Boolean field consumes its existing standalone predicate/Equal permission identity without counting selector-internal Boolean operations.

### 6. Reject configuration ambiguity instead of merging

Keep public building separate from legacy mapping configuration. `Build()` does not accept string mappings or canonical overrides, and context APIs continue to take the immutable schema rather than introduce combined schema-plus-map overloads. The central schema creation invariant explicitly rejects any nonempty mapping/override input with public mode; test this invariant through the existing unit project's internal access. Any adapter/configuration surface that later combines inputs must use that guard.

Public-mode context additional allowlists accept registered names only, snapshot their inputs, and only narrow either operation. Omitted/empty additional allowlists are no extra restriction, but cannot disable the public-mode boundary. Unknown policy/allowlist names and mismatched context entity types are explicit configuration errors.

No implicit conversion of legacy aliases to registrations and no precedence based on registration order. Document that legacy overloads called without this schema are separate unrestricted operations: selecting a public schema does not globally change all APIs or authorize access.

Alternative: intersecting canonical overrides across arbitrary selector internals requires a second expression interpreter and can produce misleading "deny-wins" guarantees for methods/getters. Rejecting mixtures is both testable and consistent with opaque trusted mappings.

### 7. Retain typed ordering keys through application

Extend the internal ordering representation to carry resolved typed key descriptors/selectors, not just canonical strings. Retain the legacy construction path for existing callers and static/internal sort helpers. Validate every key with sort permission before returning a clause; a later denied key invalidates the whole result.

Build Queryable OrderBy/ThenBy ascending/descending calls from rebound selector bodies, exact `TKey`, and quoted lambdas. Support generic/runtime queryable sources and the existing enumerable context operations without lossy boxing.

User-confirmed exception: enumerable application compiles only the final composed key lambda because LINQ-to-Objects requires a delegate. Clause construction/metadata remain compile-free, and no selector executes until normal enumeration. Queryable paths never use `AsEnumerable`, compilation, or silent client evaluation to bypass provider translation.

Alternative: leaving `OrderByClause` path-only would work for predicates but fail computed ordering. Alternative: compiling Queryable keys would break the requested EF composition.

### 8. Public metadata is a projection, not a dump of schema internals

Add an immutable public field metadata type/view under `ExpressionSchema.Fields` containing name, CLR result type, nullability, filter/sort flags, optional label/description, and declared allowed operators in registration order. Use read-only copies and distinguish omitted operator permissions from an empty set.

Expose a schema-bound context view with effective filter/sort permissions and operator sets for application-owned API documentation. It reflects context narrowing, not provider translation promises. Keep existing legacy `Properties`/`PropertyMapping` views unchanged; public schemas have no legacy map/override entries. Do not add public selector/path properties or include captured values in diagnostic formatting.

Suggestions use only admitted registered public names and the current operation/operator where known. Known-denied metadata can retain names/permissions, but diagnostics do not leak denied types. The library supplies CLR metadata, not a new wire-format schema or autocomplete UI.

### 9. Verify actual relational execution using existing fixtures

Use the shared SQL Server/MySQL assertion pattern, no new database harness. Register:

| Public name | Existing selector | Representative expected result |
|---|---|---|
| customerName | `r => r.Parent.Name` | Filter North: IDs 1, 2, 5 |
| total | `r => r.DecimalValue * r.Integer` | Filter > 8: IDs 3, 4, 5 |
| optionalTotal | `r => r.NullableDecimalValue * r.Integer` | Null: IDs 2, 4; > 8: IDs 3, 5 |
| createdAt | `r => r.CreatedAt` | Descending IDs 5, 4, 3, 2, 1 |

With total sorting enabled, descending totals yield 5, 4, 3, 2, 1. customerName ascending plus Id ascending yields 1, 2, 5, 3, 4 with the fixture collations. Use an explicitly registered Id tie-break field. Cover filter-only total separately to prove its sort denial rather than using it in a successful sort.

Execute comparable string and structured/tree mapped predicates, all-key queryable sorting, and handwritten expressions on each provider with explicit expected IDs/order. The required Parent fixture is not nullable: add an optional relationship and targeted seed data to the existing model only for guarded nullable-navigation coverage; keep all existing five-record expectations unchanged. Test nullable scalar computations on the unmodified data as well.

Document and test `r => ApplicationTotal(r.DecimalValue, r.Integer)`, an application static scalar method with no provider translation mapping: registration and semantic checks succeed, relational filter/order execution throws a provider translation exception, and a counter proves no application execution/client fallback. Avoid testing only top-level projections, where EF client evaluation can be allowed.

Compile-time member guarantees can be verified without new Roslyn dependencies: compile documented selectors in the existing test project, then use a temporary minimal application project referencing core to verify nonexistent member and incompatible operand snippets fail with the expected C# diagnostics; clean up only those specific temporary files during implementation.

## Risks / Trade-offs

- [Trusted selectors can be expensive, impure, or untranslatable] -> Document the trust boundary, prefer pure provider-supported mappings, independently disable sorting, and execute provider tests; do not promise arbitrary SQL translation.
- [Public versus legacy branches drift] -> Central descriptor resolution/composition and a cross-entry-point regression matrix, including unknown/denied variables and alias attempts.
- [Closure objects remain mutable] -> Snapshot registration metadata, never evaluate captures, document live capture semantics and avoidance guidance.
- [Automatic null protection would change C# semantics] -> Preserve selector trees and test explicit guards, nullable lifting, and unguarded execution behavior.
- [Expression shape validation is mistaken for translation validation] -> Separate registration, semantics, and provider execution in diagnostics documentation and tests.
- [Existing active spec changes overwrite public exceptions] -> Reconcile capability deltas at sync/archive; do not edit or archive unrelated changes during this proposal.
- [Database containers unavailable] -> Report provider verification as blocked, not passing; do not replace execution with SQL text or in-memory results. Existing provider prerequisites remain required.

## Migration Plan

1. Ship additive builder/schema metadata/context support while retaining every legacy entry point.
2. Applications replace string alias maps with typed fields, audit exactly which names to expose, and choose filter/sort permissions. Explicitly register scalar projections formerly written as suffix paths.
3. Build a public schema, bind it to the existing query context, use that context consistently for analysis and runtime operations, and switch bare variables to explicit references.
4. Audit null guards, captures, policy rules/public names, and actual translation on application providers before routing untrusted query input through the new context.
5. Update the dedicated guide, README, semantic/policy/nested-filter cross-references, and regression tests. No runtime feature flag, data migration, or provider dependency is required.
6. Rollback is application opt-out to existing reflected/string-mapping calls. Warn that it restores legacy visibility and must retain application-owned access restrictions; no persisted database change is needed beyond test fixtures.
