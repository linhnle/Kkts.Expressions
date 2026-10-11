# Design

## Context

See [proposal.md](proposal.md) for motivation and [the policy specification](specs/query-policies/spec.md) for the behavioral contract.

Observed implementation:

- `Interpreter.AnalyzeExpression(string)` uses `ExpressionAnalyzer`/`ExpressionLexer` on original source. Typed analysis then uses `ExpressionSemanticAnalyzer` with `ExpressionSchema` and declared variable metadata. `ExpressionDiagnostic` already carries codes, UTF-16 spans, types, and safe suggestions.
- Runtime parsing uses `ExpressionParser`, `ExpressionReader`, `BuildArgument`, and executable nodes. `ExpressionReader` currently trims input; its positions cannot be reused as original-source policy spans without translation.
- `BuildArgument` manages runtime allowlists and exact, case-insensitive string mappings. `ExpressionSchema` separately reflects paths, snapshots mappings/allowlists, and propagates explicit ancestor permission denials. Runtime APIs do not currently accept that schema.
- `ExpressionGrammar.NormalizeOperator`, `Interpreter.NormalizeComparisonOperator`, `GetComparisonOperator`, and `ExpressionOperatorRules` already define operator identities and behavior.
- `ArrayList.ParseValues` parses legacy list text, accumulates slots, and allocates a typed array. `Constant.GetVariableExpression` can embed a resolver-provided enumerable without bounding or snapshotting it. Direct/structured membership creates new BuildArgument instances that currently carry only a resolver/cancellation token.
- Filter is a property/operator/string-value leaf. FilterGroup contains `List<Filter>`; Filter sequences are AND, group sequences are OR. IFilter is not a recursive group tree. Some generic group paths delegate differently from runtime-type paths; policy-aware paths must share a canonical implementation, not inherit that divergence.
- ConditionOptions combines Filters, FilterGroups, Where, and order inputs. Invalid conditions already avoid exposing accumulated predicates. Several structured/order paths eagerly call ToArray.
- Ordering uses `OrderByParser`, OrderByInfo extensions, Interpreter clause builders, and IQueryable/IEnumerable extensions. It shares runtime mapping/allowlist infrastructure but has no field comparison operator.
- Existing tests are xUnit under `src/Kkts.Expressions.UnitTest/Units`; relational tests are in `src/Kkts.Expressions.EntityFrameworkCore.Tests`. The library targets netstandard2.0. The on-disk semantic infrastructure belongs to a still-in-progress OpenSpec change; retain its legacy guarantees and coordinate implementation.

## Goals / Non-Goals

**Goals**

- One immutable policy/schema binding reusable across editor and server operations, with operation-local counters and diagnostics.
- Independent runtime enforcement that uses shared metadata/rule helpers, never a success token returned by an editor.
- Early rejection, bounded allocations/enumeration where limits apply, safe diagnostics, and explicit failed outputs.
- Additive source/binary compatibility, including callers passing null positional arguments to legacy APIs.

**Non-Goals**

- A new parser language, recursive filter model, mapping callback API, SQL translator, authorization system, or provider cost estimator.
- Calling full semantic analysis as a runtime prerequisite: runtime variable declarations may be absent, and existing runtime conversions/ambient defaults remain authoritative.
- Introspection into application property getter implementations, or bounds on the work a resolver performs before returning a value.
- Hard protection for callers intentionally choosing unlimited budgets or supplying nonterminating host enumerators. Streaming avoids eager unbounded allocations, but host enumeration itself remains application code and requires application cancellation/time limits.

## Decisions

### 1. Immutable policy and an additive schema-bound API surface

Introduce `QueryPolicy` and one `ExpressionQueryContext`, rather than appending optional parameters to every existing method or adding ambiguous overloads beside existing nullable resolver/schema arguments.

Proposed public shape (names are implementation commitments; examples below are proposed, not existing APIs):

```csharp
public sealed class QueryPolicy
{
    public QueryPolicy(
        int? maxExpressionLength = null,
        int? maxParenthesisDepth = null,
        int? maxAtomicConditions = null,
        int? maxInItems = null,
        int? maxNavigationDepth = null,
        bool allowCollectionAccess = true,
        IDictionary<string, IEnumerable<ComparisonOperator>> allowedOperators = null);

    // Corresponding read-only properties expose snapshotted configuration.
    public static QueryPolicy Recommended { get; }
}

public sealed class ExpressionQueryContext
{
    public ExpressionQueryContext(
        ExpressionSchema schema,
        QueryPolicy policy,
        IEnumerable<string> validProperties = null);

    public ExpressionSemanticAnalysisResult AnalyzeExpression(
        string expression, ExpressionVariableSchema variables = null);

    public EvaluationResult<T, bool> ParsePredicate<T>(
        string expression, VariableResolver variableResolver = null);
    public EvaluationResult ParsePredicate(
        string expression, VariableResolver variableResolver = null);

    public Task<EvaluationResult<T, bool>> ParsePredicateAsync<T>(
        string expression, VariableResolver variableResolver = null,
        CancellationToken cancellationToken = default);
}
```

The runtime-type result uses Schema.EntityType; generic methods require T to match it. Async runtime-type equivalents are required too. Schema and policy are required nonnull constructor arguments. Additional validProperties is an immutable external-name restriction intersecting, not replacing, schema restrictions. Mappings come from the schema only; per-operation alternate mappings cannot silently disagree with analysis.

Complete the context's families by wrapping existing implementation helpers:

| Family | Context operations | Contract |
| --- | --- | --- |
| Text analysis | AnalyzeExpression | Metadata only, declared variables optional |
| Text predicate | ParsePredicate / ParsePredicateAsync | Existing evaluation result types |
| Direct predicate | BuildPredicate / TryBuildPredicate, async equivalents | Property + ComparisonOperator + object value; same supported value representations |
| Structured predicate | BuildPredicate / TryBuildPredicate, async equivalents | Filter, IEnumerable&lt;Filter&gt;, FilterGroup, IEnumerable&lt;FilterGroup&gt; |
| Conditions | BuildCondition / BuildConditionAsync | Existing ConditionOptions and Condition results |
| Ordering clause | TryBuildOrderByClause | String, OrderByInfo, IEnumerable&lt;OrderByInfo&gt; |
| Source ordering | OrderBy / TryOrderBy | Existing generic/runtime IQueryable and generic IEnumerable shapes, string or structured keys |

Where existing APIs have generic and runtime-type forms, provide context forms with identical supported data shapes; schema supplies the runtime entity type, so no redundant Type argument is necessary. Reuse OrderByClause.Sort and existing result/error semantics. No pagination methods are added.

No-policy calls continue down unchanged public signatures. Shared helpers receive an optional internal policy execution context; when absent they preserve legacy behavior, including exceptions, conversions, grouping, and result collections. Do not make existing APIs secretly opt in to Recommended. This facade is intentional indirection to avoid public signature churn and conflicting configuration; it is not a second engine.

Configuration validation happens once at construction/binding. Null numeric limits are unlimited, zero is valid, collection access defaults true, and unconfigured operators remain unrestricted. Undefined ComparisonOperator values, null rule values, duplicate case-insensitive keys, and nonexistent field paths are explicit argument errors. QueryPolicy copies dictionaries/operator sequences; the bound context canonicalizes rules using its schema. Distinct aliases resolving to the same member intersect their sets. Empty sets deny comparison use but do not themselves make the field unsortable.

Recommended is immutable: 4096 UTF-16 units / 16 parentheses / 64 conditions / 100 IN items / 3 navigation levels / collections denied, with no extra field operator restrictions. Applications select it explicitly or construct a custom policy. All state that changes during processing lives in a fresh operation, never on the reusable context or global parser cache.

### 2. Shared rule helpers, separate metadata and execution paths

Extract/reuse narrowly scoped field binding and operator identity helpers from ExpressionSchema/BuildArgument and existing grammar, rather than implementing policy independently in each caller.

A bound field contains its original external spelling/location, canonical CLR member chain, query permission, scalar/collection classification, navigation depth, and effective operator rule. Runtime BuildArgument carries the bound schema, policy operation, and original-source/input-path location. Membership builders must propagate that entire context through nested construction; creating a resolver-only BuildArgument would bypass enforcement.

Check permissions in order: external source-name restrictions, canonical schema/ancestor permissions, navigation/collection permissions, then applicable normalized comparison operator. Known restricted entity paths never fall back to variables. Existing unknown/restricted field codes stay distinct. Runtime metadata binding may defer variable type/value questions without demanding editor declarations; it must still check every decidable field permission and budget. Editor analysis keeps its declaration-based semantic checks.

Comparison identities use ComparisonOperator, including IN versus NOT IN. Normalize legacy symbolic aliases, casing, and accepted NOT IN whitespace using existing helpers before checking. A field-versus-field comparison checks both fields; a computed arithmetic operand checks every contributing field against the enclosing comparison. Logical NOT retains the original comparison identity. A standalone Boolean field uses Equal. Addition/subtraction are not new configurable field operators.

Suggestions are filtered by external allowlists, canonical denial, navigation, collection permission, and the current operator when known. Policy denial diagnostics offer actionable messages, not speculative replacements or restricted-name hints.

Alternative rejected: invoke AnalyzeExpression and trust its result before parsing. That conflates metadata declarations/conversion context with runtime availability/defaults and cannot enforce resolved collection sizes.

### 3. Counting is attached to original input, not rewritten trees

Use the specification's precise counting rules. Important implementation implications:

| Budget | Source/accounting | Earliest enforcement |
| --- | --- | --- |
| Expression length | Untrimmed source.Length; structured leaf strings summed, no JSON/rendering | Before lexer/parser; streaming structured admission |
| Parentheses | Open `(` outside existing quote/escape grammar; group envelopes for structured inputs | Before pushing a new parser scope |
| Conditions | Comparison/membership/function or standalone Boolean predicate; reserve incomplete recognized comparison | Before admitting another predicate node |
| IN items | Parsed slots before conversion/deduplication; resolved variable items at runtime | Before adding excess slot or allocating typed collection |
| Navigation | Canonical member-type transitions; scalar member chains excluded | During incremental member binding |
| Collections | Entity collection member reference/traversal, not strings or variable lists | During member binding |
| Operators | Normalized comparison identity on all contributing fields | Before executable comparison construction |

`Customer.Address.City` has depth two; `CreatedAt.Year` and `Name.Length` have depth zero. A collection member transition consumes one navigation level if permitted; its Count member is scalar. Classify scalar types from existing conversion/type rules, not a dot count, database model, or "all classes are entities" heuristic. Variables are not entity navigation.

Count NOT(Id = 1 OR Id = 2) as two, Price + Discount > 10 as one, and an explicit Boolean field as one. Reserve `Id =` as one even if its right operand is missing. Avoid counting a bare field and its subsequent comparison twice. Quoted commas remain within one membership slot, duplicates and nulls each occupy a slot, and a started unfinished third slot can violate a limit of two. Brackets/braces remain membership syntax but are not parentheses.

A single ConditionOptions build aggregates Where + Filters + FilterGroups conditions and supplied filter text lengths using one operation budget. A group sequence has depth one, not two merely because of an outer enumerable. Structured Property/Operator are counted as stored by current DTO setters; Value is counted unchanged. Standalone Filter/direct construction is depth zero. Ordering has its own length/field checks and never spends comparison/condition/IN budgets.

For direct builder input, count one condition, property text plus normalized operator spelling plus a supplied string value for length; do not call an arbitrary object's ToString to obtain a policy length. Supported nonstring values use runtime membership/type checks, not a invented serialized-text length. The enum operator's canonical text is the direct API representation; text inputs keep their original spellings.

### 4. Staged enforcement protects work before it happens

1. **Binding/configuration:** Validate immutable policy/schema metadata without application execution.
2. **Admission:** Check original string length immediately. Stream structured fields to admit bounded text/leaf counts before ToArray, aggregate nodes, or generated expression strings. A static failure returns before runtime resolver initialization.
3. **Lexical/structural processing:** Reuse quote/escape grammar in a streaming scanner/lexer. Check opening depth before a scope push and membership slots before list storage. Maintain bounded diagnostic collection during recovery. A policy-aware parser must use explicit stacks/iterative work for long NOT, additive, and Boolean chains so shallow parentheses are not mistaken for safe recursive depth.
4. **Metadata binding:** Check canonical field permissions and operator rules without property getters. Apply the same pure budget/rule functions in analysis and runtime. Stop fatal resource-limit processing at the first excess; independent permission diagnostics can continue up to the diagnostic ceiling.
5. **Runtime value resolution:** Only after static admission succeeds, use the existing sync/async resolver contract. Validate membership size immediately after resolution, before constants/arrays/conversions/calls. Preserve cancellation and explicit value errors.
6. **Expression emission:** Construct native trees with existing conversion/precedence/null semantics. Carry policy context through all nested builders. No result escapes until the full operation is successful.
7. **Result finalization:** Map policy diagnostics to failed evaluation/try results, exceptions for throwing builders, and invalid conditions; discard any partial expression or ordering output.

Do not perform a full parse/token allocation and then check complexity, nor use regex or naive Split as a second language implementation. Share item scanning between ArrayList parsing, editor counting, and structured membership where practical; do not duplicate grammar. Cache policy-independent parser metadata only, or include immutable schema/policy identity in a cache key. Mutable budgets/results must never be cached or shared.

Structured input enumerators are traversed once into admitted bounded data as far as finite budgets permit. Do not use Any followed by unbounded ToArray as validation. Empty groups contribute no atomic leaves and still receive the existing empty/invalid-group semantics; they are not charged as fake predicates. Arbitrarily nonterminating host enumerators cannot be made terminating by a leaf count alone; they are outside the permitted-input guarantee, and async cancellation must remain available.

### 5. Membership runtime checks use bounded, single-use snapshots

For a variable collection with finite MaxInItems N, take at most N + 1 elements with checked/wide counters, disposing its enumerator. Accepted values are retained in a bounded typed snapshot and used for expression emission. Reject on the first excess; do not continue to compute an exact count, deduplicate, or stringify values. Known built-in arrays/collections can provide an exact safe count; do not read arbitrary custom Count getters just to optimize validation.

Validate before both Constant's ordinary and UseNaturalType paths; otherwise arithmetic-left membership could bypass the check. Preserve declared/runtime element-type and conversion behavior. List-element variables remain scalar slots under existing syntax; do not flatten collections or add syntax. A successfully accepted enumerable is not re-enumerated at query execution, preventing lazy/mutable sources from changing the validated item count. This opt-in snapshot behavior is documented; no-policy variables retain legacy behavior.

Metadata can know literal slot count and variable element type, not a collection variable's cardinality, availability, conversion success, enumeration cost, or resolver side effects. A declared collection variable is not rejected merely because runtime size validation is deferred.

When an already-supported membership form references an entity collection rather than an application variable, its per-row size cannot be checked without reading/evaluating an entity or query. With a finite MaxInItems, fail closed using `query-policy-in-items-unverifiable`; with an unlimited item budget and collection access allowed, retain only legacy-supported behavior. This is a deliberate compatibility decision for opt-in policies, not new entity collection syntax.

Resolvers can run database/external work before returning an enumerable; the library cannot bound that work, a blocking MoveNext, or application getters. Recommend bounded application resolver implementations plus application cancellation/timeouts. Do not enumerate IQueryable to validate user expression membership if doing so would execute a database query: reject a query-backed unresolved collection with `query-policy-in-items-unverifiable` when a finite size bound requires materialization. An application can explicitly resolve to a bounded in-memory collection first.

Alternative rejected: Count()/ToArray() followed by a size check. It is unsafe for infinite/lazy sequences and allocates before enforcing the limit.

### 6. Diagnostics extend existing results rather than replace them

Add optional immutable metadata to ExpressionDiagnostic: `long? ConfiguredLimit`, `long? ObservedValue`, `bool ObservedValueIsLowerBound`, and `string InputPath`. Policy violations remain Semantic kind; preserve existing enum values. Add an immutable Diagnostics view to EvaluationResultBase and propagate it through ToGeneric, direct/filter try wrappers, order results, and Condition.Error.EvaluationResult. Throwing policy builders use a specific QueryPolicyException containing the same diagnostic list; do not reclassify it as an invalid variable/value inside node catches.

Add `IsTruncated` to ExpressionSemanticAnalysisResult (false on legacy calls), describing intentionally incomplete safe-prefix analysis. Policy-aware fatal early exits do not claim syntax completeness for an unprocessed suffix. Ordinary permission failures leave independently established IsComplete true and IsSemanticallyValid false. No-policy syntax snapshots/tokens/diagnostics remain untouched.

| Code | Text span | Structured location | Message action |
| --- | --- | --- | --- |
| query-policy-expression-length-exceeded | Excess suffix `[limit, source.Length)` | First leaf string crossing cumulative budget; `.Value`/`.Property`/`.Operator`, or Where | Shorten input |
| query-policy-parenthesis-depth-exceeded | First excessive `(` | Group envelope, or Value for list fragment | Flatten groups/calls |
| query-policy-condition-count-exceeded | Excess predicate's operator; bare Boolean token if no operator | Excess leaf's `.Operator` | Remove conditions |
| query-policy-in-items-exceeded | First excess item; whole variable token for runtime collections | Membership leaf `.Value` | Shorten collection |
| query-policy-navigation-depth-exceeded | Full external path | `.Property` | Use an exposed permitted shallower field |
| query-policy-collection-access-denied | Full external path | `.Property` | Remove collection access |
| query-policy-operator-denied | Operator token; bare Boolean field for implicit Equal | `.Operator` | Use a permitted comparison |
| query-policy-in-items-unverifiable | Collection field/variable token | `.Value` | Supply an explicitly bounded in-memory variable, or omit that use |
| query-policy-diagnostics-truncated | End-of-input caret | Operation root | Fix reported errors before retrying |

InputPath examples: `Filters[2].Value`, `FilterGroups[1].Filters[0].Operator`, `OrderBys[0].Property`, `Where`. For standalone Filter use `Property`, `Operator`, `Value`; standalone text has no InputPath. Where uses actual text offsets plus InputPath. Structured DTO fields have no source snapshot, so Start/Length are zero; do not pretend offsets into a rendered query or pretrimmed Property are exact.

Use a shared capped collector: at most 31 ordinary combined diagnostics and one truncation marker if a further distinct report is suppressed (up to 32 ordinary reports if none are suppressed). Collection is capped during production, not after building an unbounded list. Deduplicate by code and full input location; retain source order for text and submission order for structured input, with the truncation marker last. A fatal limit normally emits only its violation. Observed values are exact when known, otherwise the first excess/lower bound, using long to represent Int32.MaxValue + 1 safely.

Messages contain supplied external names only, not canonical restricted names, values, SQL, or reflected restricted types. Denial suggestions stay empty. Existing known-field schema/allowlist denials retain property-not-queryable; policy-specific navigation/collection/operator failures use the new codes.

### 7. API examples and application-owned tenant scope

Proposed configuration shared by editor and server:

```csharp
var schema = ExpressionSchema.FromType<Product>(
    validProperties: new[] { "Id", "Price", "Name", "CreatedAt", "buyer" },
    propertyMapping: new Dictionary<string, string>
    {
        ["buyer"] = "Customer.Name"
    });
var policy = new QueryPolicy(
    maxExpressionLength: 4096,
    maxParenthesisDepth: 16,
    maxAtomicConditions: 64,
    maxInItems: 100,
    maxNavigationDepth: 1,
    allowCollectionAccess: false,
    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
    {
        ["Price"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
        ["Name"] = new[] { ComparisonOperator.Equal, ComparisonOperator.StartsWith }
    });
var queries = new ExpressionQueryContext(schema, policy);
var analysis = queries.AnalyzeExpression("Price > $minimum", variableMetadata);
var evaluation = await queries.ParsePredicateAsync<Product>(
    request.Filter, resolver, cancellationToken);
```

The server constructs/owns queries; do not deserialize policy or schema from user input. Client analysis is advisory. A runtime expression is checked even when analysis was never called. Callers wanting the preset instead use `new ExpressionQueryContext(schema, QueryPolicy.Recommended)`.

Structured, direct, and ordering examples:

```csharp
var filters = new[]
{
    new Filter { Property = "Id", Operator = "not in", Value = "1, 1, 2" },
    new Filter { Property = "Name", Operator = "startsWith", Value = "A" }
};
var structured = queries.TryBuildPredicate<Product>(filters, resolver);
var direct = queries.TryBuildPredicate<Product>(
    "Price", ComparisonOperator.GreaterThan, "10", resolver);
var condition = queries.BuildCondition<Product>(
    new ConditionOptions { Filters = filters, Where = "Price > 10" }, resolver);
var ordering = queries.TryBuildOrderByClause("buyer asc");
```

The complete guide will define Product/Customer/request/variable/resolver context, not leave placeholders masquerading as library APIs. Examples will compile in documentation tests using the repository's existing fixture pattern.

Tenant isolation stays outside the user expression:

```csharp
var evaluation = queries.ParsePredicate<Product>(request.Filter, resolver);
if (!evaluation.Succeeded)
    throw new InvalidOperationException(
        "Invalid user filter. Report evaluation.Diagnostics to the caller.",
        evaluation.Exception);

var records = db.Products
    .Where(product => product.TenantId == applicationTenantId)
    .Where(evaluation.Result);
```

applicationTenantId comes from application authorization context, never the filter/request's tenant claim. Even if a user predicate contains OR, it cannot remove the preceding application-owned tenant predicate. Do not include TenantId in exposed query fields just to build the mandatory predicate. Policies neither prove nor replace authorization, provider SQL translation, tenant scope, database timeout, nor execution cost.

### 8. Computed mappings use the actual supported surface

Existing mappings are exact string-to-member-path aliases, not arbitrary expressions. Check all visible mapped paths and canonical permissions. An exposed computed CLR scalar property is opaque trusted application code; do not inspect its IL, evaluate its getter, or enforce hidden internal member accesses. Its outer field permission and operator rules still apply. No lambda mapping registration is introduced.

If an application projects database entities to a queryable DTO with computed properties, the application owns that projection and any hidden joins/accesses. Query the DTO's exposed metadata; do not promise underlying entity access restrictions. Document these boundaries explicitly rather than fabricating computed mapping support.

## Risks / Trade-offs

- [Facade surface spans many existing entry points] -> Keep one context and shared execution helpers, enumerate all families above, and test parity; do not create per-input policy types.
- [Parser/metadata drift or policy bypass through nested BuildArgument] -> Reuse normalization/binding/item grammar, propagate one operation budget, and test aliases, arithmetic membership, direct/structured async paths, conditions, and ordering.
- [Legacy runtime offsets refer to trimmed text] -> Maintain original-source locations or an explicit leading-offset map on policy-aware paths; test whitespace and supplementary characters.
- [Semantic infrastructure is not archived and is still evolving] -> Integrate the on-disk contract, coordinate before changing shared result/schema code, and preserve the active change's no-policy tests.
- [Early limits reduce editor highlighting] -> Return IsTruncated and safe-prefix tokens; prioritize resource bounds over full highlighting only on opted-in paths.
- [Accepted variable collections become snapshots] -> Document opt-in snapshot semantics, preserve duplicate/null/conversion behavior, and test single-pass/mutable sequences.
- [A cardinality bound cannot validate entity or query-backed collections safely] -> Explicit unverifiable failure with finite limits; applications can provide bounded in-memory variable values.
- [Recommended limits are not workload-specific cost guarantees] -> Label them starting limits, not authorization or SQL-cost controls.
- [Application computed getters/resolvers hide expensive or restricted work] -> Treat them as trusted application code; metadata never calls them, and policy documentation makes their boundary explicit.
- [Unlimited settings and nonterminating application enumerators remain risky] -> Make unlimited defaults transparent and recommend explicit preset plus application cancellation/timeouts.

## Migration Plan

1. Deliver immutable policy/context/diagnostic contracts and pure counting/binding helpers with targeted tests, keeping old signatures intact.
2. Wire policy-aware analysis and independently enforce string/direct predicate construction, including runtime bounded membership.
3. Wire structured, condition, and ordering paths through shared helpers; verify no nested context reset, aggregate bypass, or partial result.
4. Publish the guide/README quick start and compile/link-check examples; run core build, targeted policy regressions, then existing unit and relational suites.
5. Applications opt in by creating a server-owned ExpressionQueryContext and using it for all user query entry points. Reuse the same configuration for editor analysis; do not trust client results.
6. Rollback in an application means reverting its explicit context use to the old APIs, acknowledging removal of policy protections. No stored schema/data migration or automatic behavior switch is involved.
