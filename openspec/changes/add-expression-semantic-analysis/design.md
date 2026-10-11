# Design

## Context

See [proposal.md](proposal.md) for motivation and the [semantic delta](specs/expression-semantic-analysis/spec.md) for the behavioral contract.

Observed implementation:

- `Interpreter.AnalyzeExpression(string)` returns sealed `ExpressionAnalysisResult`, containing read-only token/syntax-diagnostic snapshots and syntax-only `IsComplete`. `ExpressionSyntaxDiagnostic` is also sealed; changing its meaning or collection type would break consumers.
- `Internal/ExpressionAnalyzer` lexes original text, drives `ExpressionSyntaxParser(syntaxOnly: true)`, recovers at logical boundaries, and validates parser chains. It currently returns no positioned tree or reusable recovery fragments. `ExpressionSyntaxParser` already tracks recovered scopes.
- `ExpressionParser` reduces chains in precedence order: operands; negation/groups/functions; additive operations; comparisons; AND; OR. Its `BuildNode` path consults `BuildArgument` and creates runtime nodes; analysis must not call it.
- Runtime properties use case-insensitive `PropertyMetadata` reflection, exact full-path `BuildArgument.PartialMap`, then `Expression.PropertyOrField`. With a nonempty `validProperties` collection, source names are checked exactly before mapping; empty/omitted lists allow reflected members and valid nested paths.
- Unrecognized bare property tokens become variable nodes in `ExpressionParser`. Variables can be registered by exact dotted name or resolved through root object members; `VariableResolver` reads getters and may invoke `TryResolveCore`. `VariableInfo` contains a runtime value and is unsuitable as a declaration.
- `Comparison.BuildOperands` performs context-dependent literal conversions, with separate arithmetic, constant/property, list, and fallback branches. Grouping and operand direction can affect these branches. `Constant` and `ArrayList` build paths invoke resolvers. No analyzer should reuse these build paths.
- `NumericOperands` implements natural literal types, numeric/char promotion, nullable lifting, unsigned constant exceptions, and decimal/floating rejection. `Addition` also supports mixed string concatenation; `Subtraction` is numeric only.
- `StringExtensions.Cast` supplies built-in conversions; date/time parsing consults `CurrentCulture` and mutable `DateTimeFormats`. Object conversion can execute `IConvertible`. Metadata-only checks cannot call it on application objects.
- Existing `ExpressionAnalysisTest` checks read-only snapshots, UTF-16 offsets, recovery, incomplete paths, culture independence, and grammar parity. Nearby Interpreter, arithmetic, TimeSpan, mapping/filter, resolver, and culture tests supply runtime baselines. Core targets `netstandard2.0`; xUnit tests target `net10.0`.
- Documentation currently lives primarily in the root README with relative example links and tested editor snippets. Introduce `docs/semantic-analysis.md` rather than expanding the README into a second reference guide.

## Goals / Non-Goals

**Goals:**

- Separate recoverable positioned syntax, metadata binding, and pure type checking from executable nodes.
- Reuse runtime rule decisions, not runtime execution; retain exact branch behavior and legacy error reporting.
- Provide immutable, deterministic snapshots with conservative corrections and explicit conversion-context boundaries.

**Non-Goals:**

- No new operator grammar, autocomplete, editor UI/LSP, provider-specific behavior, query execution, or SQL validation.
- No resolver-type inference, resolver initialization, custom conversion invocation, or runtime-value availability checking.
- No changes to syntax-only completeness, runtime conversion defaults, null traversal, authorization policy, or legacy predicate semantics.

## Decisions

### 1. Add overloads and a separate semantic result

Proposed public signatures (additive to the existing overload):

```csharp
public static ExpressionSemanticAnalysisResult AnalyzeExpression<T>(
    string expression,
    ExpressionSchema schema,
    ExpressionVariableSchema variables = null);

public static ExpressionSemanticAnalysisResult AnalyzeExpression(
    string expression,
    Type entityType,
    ExpressionSchema schema,
    ExpressionVariableSchema variables = null);
```

Require the schema entity type to equal `typeof(T)`/`entityType`; explicit mismatch is an argument error, not a user-expression diagnostic. Omitted variables mean an empty declaration set. No resolver parameter and no async counterpart: the operation performs no I/O. Do not reinterpret `AnalyzeExpression(string)` as an opt-in semantic call.

`ExpressionSemanticAnalysisResult` exposes:

| Property | Contract |
|---|---|
| `SyntaxAnalysis : ExpressionAnalysisResult` | Owned existing syntax snapshot |
| `Tokens : IReadOnlyList<ExpressionToken>` | Forwards the identical syntax classifications |
| `SyntaxDiagnostics : IReadOnlyList<ExpressionSyntaxDiagnostic>` | Unchanged syntax diagnostics |
| `SemanticDiagnostics : IReadOnlyList<ExpressionDiagnostic>` | Semantic-only diagnostics |
| `Diagnostics : IReadOnlyList<ExpressionDiagnostic>` | Combined wrappers, ordered by Start, Length, ordinal Code |
| `IsComplete : bool` | Forwards syntax-only completeness |
| `IsSemanticallyValid : bool` | Complete syntax, no semantic errors, and a Boolean root |

New `ExpressionDiagnostic` is an immutable common view with `Kind` (`Syntax`/`Semantic`), `Code`, `Message`, `Start`, `Length`, `ExpectedTypes`, `ActualTypes`, and `Suggestions`. Syntax wrappers carry empty type/suggestion lists. Keep existing sealed classes, properties, and constructor use in internal tests intact. Arrays/dictionaries supplied by callers are copied; earlier results cannot mutate after later edits.

**Alternative rejected:** widen `ExpressionAnalysisResult.Diagnostics` or subclass sealed syntax diagnostics. Both damage existing source/binary contracts and falsely suggest syntax-only callers now validate predicates.

### 2. Immutable reflection-backed schemas, with selective overrides

Proposed factories:

```csharp
public static ExpressionSchema FromType<T>(
    IEnumerable<string> validProperties = null,
    IDictionary<string, string> propertyMapping = null,
    IEnumerable<ExpressionPropertyDefinition> properties = null,
    ExpressionConversionContext conversionContext = null);

public static ExpressionSchema FromType(
    Type entityType,
    IEnumerable<string> validProperties = null,
    IDictionary<string, string> propertyMapping = null,
    IEnumerable<ExpressionPropertyDefinition> properties = null,
    ExpressionConversionContext conversionContext = null);
```

`ExpressionPropertyDefinition(path, clrType, nullability = Unknown, canQuery = true)` is an immutable selective override. The CLR type must match the actual reflected member; overrides describe metadata, not fictional runtime properties. Schema exposes `EntityType`, read-only definitions, mappings, allowlist, and conversion context. Value types derive nullability from their CLR type; contradictory overrides are rejected. Reference nullability defaults to `Unknown` for portable netstandard support and can be explicitly supplied. Nullability informs diagnostics, but must not impose stricter comparisons than runtime construction.

Extend existing member metadata to include `MemberInfo`/CLR type alongside names. Traverse referenced paths lazily, segment by segment, rather than recursively enumerate entire CLR graphs; cyclic entity types remain safe. Reject indexers, static/unreadable members, conflicting case-insensitive definitions, nonexistent override paths, and invalid mapping targets with targeted argument exceptions. Reflection can reuse `Expression.PropertyOrField`-compatible member selection or a shared descriptor selector, but does not read values. Cache only immutable type facts, never caller permissions or mutable schema instances.

Binding order:

1. Normalize lexical path spelling for lookup only; retain the original lexer range (including spaces around dots).
2. Apply an exact full-path mapping; no implicit alias-prefix rewrite.
3. Determine whether the canonical CLR path exists.
4. If it exists, check the legacy nonempty allowlist against the original external spelling and explicit canonical denials against every prefix and terminal member. A denied ancestor denies descendants.
5. If nonexistent and not mapped, allow a matching explicitly declared bare-variable fallback; otherwise report unknown property. A denied existing member never falls back to a variable.

An alias does not hide the canonical spelling by itself. A nonempty allowlist can permit just aliases, exactly as runtime callers already do. Unknown paths are still unknown even when a restrictive allowlist exists; this gives the required distinction without revealing internal names. Restriction messages mention only the user-entered source spelling, not canonical names or CLR type details. An invalid mapping is caller configuration failure, not a guess about user text.

**Alternative rejected:** require every property/type in a hand-maintained schema, or import an EF model. Reflection already provides member facts and the library must remain provider-independent.

### 3. Declare variables without values

Proposed metadata surface:

```csharp
public ExpressionVariableSchema(
    IEnumerable<ExpressionVariableDefinition> variables);

public ExpressionVariableDefinition(
    string name,
    Type clrType,
    ExpressionNullability nullability = ExpressionNullability.Unknown,
    IEnumerable<ExpressionVariableDefinition> members = null);

public static ExpressionVariableDefinition FromType(
    string name,
    Type clrType,
    ExpressionNullability nullability = ExpressionNullability.Unknown);
```

Declarations omit `$`; reject invalid/duplicate names. Explicit constructor members form a declared member tree (child names are individual segments); absent members on an explicit complex declaration mean no member paths are declared. `FromType` opts into lazy reflection of readable instance properties/fields. Arrays and generic enumerable interfaces contribute element *types*, not values or enumeration. Exact full dotted declarations bind before root traversal, mirroring direct runtime registration. Declaring `user.department` alone does not declare `user` as an object or other siblings.

Member/root traversal matches existing case-insensitive reflected lookup. Exact direct registrations retain the runtime registry's ordinal name matching; do not silently make its direct dictionary case-insensitive. Ambiguous declarations across matching modes are argument errors. Built-in resolver variables are not implicit: a caller declares the ones its environment provides. Declaration and runtime resolution remain separate; a valid declaration can have no currently available value.

`$x` always means a variable. Bare names first use entity binding and only then explicit declarations; retain bare fallback compatibility without speculative resolver calls. If no typed root or exact dotted declaration exists, report `undeclared-variable`; if a typed root exists but its path does not, report `unknown-variable-member`.

**Alternative rejected:** inspect resolver instances or reuse `VariableInfo`. Both contain runtime state, and resolver properties can trigger getter side effects.

### 4. Export positioned recovery data instead of invoking the runtime builder

Introduce an internal syntax snapshot produced by the existing lexer/analyzer/parser flow: original tokens, syntax diagnostics, positioned syntax nodes or recoverable chains, and per-scope/per-node syntax validity. Keep it internal; no public AST is needed for P0. The syntax-only API projects its existing result from this snapshot.

Factor precedence/operator normalization from `ExpressionParser` into shared pure facts used by both reducers. The semantic reducer creates positioned non-executable expression nodes for properties, variables, constants, lists, unary/binary operators, groups, and functions. It never calls `ExpressionParser.Parse`, `BuildNode`, `Node.Build`, `BuildAsync`, `ParseValues` on runtime array nodes, or `ParsePredicate`.

For lists, use positioned lexer elements and preserve existing quote/escape/unquoted-element rules rather than inventing a richer list expression language. The current runtime list parser has constraints beyond highlighting; parity cases must include quoted versus unquoted Boolean/null, negative elements, escapes, empty lists, and all three delimiter forms. Position every list element separately.

Represent missing/invalid syntax as explicit error nodes, not fake null constants. Preserve root and child recovery provenance, including failures before/after a synchronization boundary. Type visitor results carry `Known`, `Unbound`, or `SyntaxInvalid` status. Check complete independently recoverable children even when the full expression is incomplete; do not globally disable semantic analysis on any syntax error.

Recovery examples:

| Input | Output policy |
|---|---|
| `Price > ` | Existing EOI missing operand only; no comparison/root cascade |
| `Customer.` / `$user.` | Existing incomplete identifier only |
| `Price = ) and UnknownField = 1` | Syntax failure plus complete suffix unknown-property |
| `UnknownField + 1 = 2 and Price > 'abc'` | Unknown field plus invalid literal; no dependent arithmetic/root errors |
| `Price > 'abc' and Name = '` | Retain first semantic error; do not convert unfinished string |
| blank | Empty collections; both flags false |

**Alternative rejected:** strict runtime parse or regex/token adjacency checking. Strict parse stops recovery and can reach runtime resolution; adjacency loses precedence, grouping, and contextual type behavior.

### 5. Extract pure rule decisions; retain runtime execution and legacy reporting

Create one internal type/conversion rule layer consumed by the semantic visitor and runtime nodes. Inputs contain operator, operand CLR types/nullability, operand origin (literal, variable, property, computed), syntactic shape/grouping, literal text or safe built-in literal facts, and conversion context. Outputs describe applicable conversions, promoted/result types, or a specific failure. Runtime nodes still construct expressions and record their existing `InvalidValues`/`InvalidOperators`/exception behavior; semantic analysis maps failures to positioned diagnostic codes.

Extract incrementally from:

- `NumericOperands`: natural number parsing, promotion, nullable lift, unsigned/signed constant compatibility. Keep char numeric behavior, decimal/floating rejection, and safe built-in integral constant arithmetic for the existing unsigned exceptions. Never compile an expression to discover a constant.
- `Comparison`: contextual conversion direction and branch distinctions; operator signatures for equality/relational/string functions; arithmetic counterpart preparation and result rules.
- `Addition`/`Subtraction`: result/type decisions; string concatenation remains type-only in analysis, never `ToString` or object conversion.
- Membership: scalar/collection element compatibility, contextual list conversion, null handling, collection variables, and `not in` complement rules. Do not enumerate a declared collection.
- `StringExtensions`/`ObjectExtensions`: built-in literal conversion capability and validation; custom `IConvertible`, `TypeConverter`, getters, and application conversions are never called by analysis.
- Logical/negation and root checks: retain actual CLR Boolean applicability, including nullable distinctions.

The layer does not wrap runtime construction and does not use a second handwritten operator table. Known custom comparison signatures can be inspected without execution where runtime already supports them; do not broaden arithmetic to user-defined operators. Distinguish type-level conversion feasibility from value-dependent success: a declared String can potentially convert to Decimal, but an unavailable string cannot be diagnosed as containing `abc`. Known literal text can be checked immediately. Similarly, variable nullability or object subtypes can make runtime conversion success value-dependent; semantic validity does not promise success for every future value.

Freeze observed construction semantics with characterization tests before extracting rules, especially grouped literals, constant-left versus property-left, property-to-property mismatches, membership collection strings, nullable variable boxing, and non-arithmetic versus arithmetic comparisons. Fix no pre-existing runtime inconsistency in this feature; document value-dependent limits and keep parity fixtures explicit.

**Alternative rejected:** validate by constructing a predicate with dummy values. Dummy values misrepresent conversion behavior and still risk calling a resolver or user code.

### 6. Freeze conversion context; diagnose clock/time-zone-dependent forms

User decisions: use an explicit immutable context, preserve runtime defaults, and report temporal forms requiring the current date or local time zone separately rather than guessing.

```csharp
public ExpressionConversionContext(
    CultureInfo culture,
    IEnumerable<string> dateTimeFormats);
```

The context clones culture (including calendar/date formatting state) into a read-only snapshot and copies formats. The schema's omitted context uses invariant culture and a fixed copy of the library's standard six date formats, not `CurrentCulture` or the mutable public list. Callers needing their runtime culture pass it explicitly and snapshot the configured formats. Numeric language literals remain invariant regardless of date context.

Parameterize the pure date conversion check with the snapshot. Retain the runtime `StringExtensions` facade's ambient/default behavior; shared rules receive that runtime context when constructing. Analysis checks valid full calendar dates under the supplied culture and explicit formats. Never accept machine-supplied year/day/offset as known metadata. Detect insufficient-date and local-offset-dependent forms before calling culture-sensitive parsing that supplies those defaults; return `context-dependent-conversion`. A DateTime full-date literal without a zone is not automatically an error if parsing does not require local-zone conversion; DateTimeOffset without an explicit offset is context-dependent. Explicit offsets and full dates can be checked without local conversions.

The temporal checker must classify invalid text separately from text accepted only with date/zone defaults. Cover supported culture patterns, calendar leap days, time-only forms, yearless dates, explicit `Z`/offsets, and time-zone-bearing DateTime forms in characterization/parity tests. It must not read `Now`, `Today`, or `TimeZoneInfo.Local`. Implement the shared check from built-in parsing rules and format metadata, not by appending a fabricated date or changing runtime interpretation.

Document the narrow difference: context-dependent-conversion is an editor analysis limitation/error requiring explicit source information, not a claim that legacy runtime parsing rejects the text. No reference-date/time-zone configuration is added in P0.

**Alternative rejected:** silently use current culture/time, or change runtime parsing to invariant. The former violates determinism and the latter changes established behavior.

### 7. Structured diagnostics and deterministic suggestions

`ExpressionTypeInfo` exposes `Kind` (`Clr`, `Null`, `Unknown`), `ClrType` (only for `Clr`), `Nullability` (`NonNullable`, `Nullable`, `Unknown`), and optional `ElementType`. Types use CLR `Type`, not message-string parsing; public messages can use familiar C# names. Expected/actual collections are empty when not meaningful; missing operands are not reported as actual `Unknown` types. For binary incompatibility, preserve operand order in actual types. Never include restricted canonical types or paths in diagnostic details.

`ExpressionCorrectionSuggestion` exposes `Message`, `ReplacementText`, `Start`, and `Length`. Replacement spans use the same original-input convention as diagnostics. Source tokens include quotes and `$`; path diagnostics cover the full source token, including internal whitespace. Operator diagnostics cover the exact operator token, not the call punctuation. A non-Boolean root covers its complete expression range without leading/trailing whitespace.

Stable P0 code table:

| Code | Meaning / primary span | Type details | Suggestion policy |
|---|---|---|---|
| `unknown-property` | Unknown full source property path | Empty | Unique permitted sibling typo only |
| `property-not-queryable` | Known denied full source path | Empty | None |
| `incompatible-operand` | Literal/operand that cannot match contextual type | Expected target; actual source | No invented values |
| `operator-not-applicable` | Operator token with known incompatible types | Required receiver/operand types; actual types | No speculative operator replacement |
| `undeclared-variable` | Full variable reference | Empty | None in P0 |
| `unknown-variable-member` | Full reference below a declared root | Empty | None in P0 |
| `predicate-result-not-boolean` | Complete root expression | Expected Boolean; actual root | None |
| `context-dependent-conversion` | Literal requiring date/zone defaults | Target temporal type; source String | Actionable message, no fabricated date/offset |

Examples for `Product.Price : decimal`:

```text
Price > 'abc' and UnknownField = 1
incompatible-operand  Start=8  Length=5
  ExpectedTypes=[Clr(System.Decimal, NonNullable)]
  ActualTypes=[Clr(System.String, NonNullable)] Suggestions=[]
unknown-property      Start=18 Length=12
  ExpectedTypes=[] ActualTypes=[] Suggestions=[]

Price.contains('text')
operator-not-applicable Start=6 Length=8
  ExpectedTypes=[Clr(System.String, Unknown)]
  ActualTypes=[Clr(System.Decimal, NonNullable)] Suggestions=[]

$user.department = 'Sales'
undeclared-variable OR unknown-variable-member Start=0 Length=16
  Code depends on whether a typed user root is declared.

Pric = 1
unknown-property Start=0 Length=4
  Suggestions=[ReplacementText="Price", Start=0, Length=4]
```

For property corrections, first derive only permission-allowed public spellings in the current parent scope. Compute ordinal-ignore-case Levenshtein distance on the failing terminal segment with threshold 1 (no candidates for a one-character segment). Suggest only a unique minimum; tie means no suggestion. Preserve canonical external casing, replace the whole property token, and use an external alias instead of exposing its canonical target. Validate the complete replacement against the same mapping/permission binder. Do not rank restricted candidates first then hide their names: exclude them before scoring.

Diagnostic priority: binding error on a leaf suppresses its type-dependent ancestors; invalid receiver/operator gets one operator diagnostic instead of unnecessary conversion cascades; independent operands/list elements/clauses continue. Sort by Start, Length, ordinal Code and deduplicate `(Code, Start, Length)`. Messages must be invariant and deterministic; they do not embed ambient culture or runtime values.

**Alternative rejected:** fuzzy autocomplete or multiple speculative fixes. One conservative typo correction satisfies editor diagnostics without adding the out-of-scope autocomplete feature.

### 8. Concrete usage and documentation approach

These are proposed, internally consistent API examples, not claims that the feature is already implemented. Compile the final guide and README against the actual API during apply.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Kkts.Expressions;

public class Product
{
    public decimal Price { get; set; }
    public decimal? Discount { get; set; }
    public string Name { get; set; } = "";
    public decimal InternalCost { get; set; }
    public Customer Customer { get; set; } = new Customer();
}

public class Customer
{
    public string Name { get; set; } = "";
}

public class UserMetadata
{
    public string Department { get; set; } = "";
}

public static class SemanticAnalysisExample
{
    public static void Run()
    {
        var context = new ExpressionConversionContext(
            CultureInfo.InvariantCulture,
            new[] { "yyyy-M-d", "yyyy/M/d", "d/M/yyyy" });

        var productSchema = ExpressionSchema.FromType<Product>(
            validProperties: new[] { "Price", "Discount", "Name", "buyer" },
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
            },
            conversionContext: context);

        var variables = new ExpressionVariableSchema(new[]
        {
            ExpressionVariableDefinition.FromType(
                "user", typeof(UserMetadata)),
            new ExpressionVariableDefinition("minimum", typeof(decimal)),
            new ExpressionVariableDefinition("prices", typeof(decimal[]))
        });

        var analysis = Interpreter.AnalyzeExpression<Product>(
            "Price > 'abc' and UnknownField = 1",
            schema: productSchema,
            variables: variables);

        foreach (var diagnostic in analysis.Diagnostics)
        {
            Console.WriteLine(
                $"{diagnostic.Kind}: {diagnostic.Code} " +
                $"at {diagnostic.Start}+{diagnostic.Length}: " +
                diagnostic.Message);
            foreach (var expected in diagnostic.ExpectedTypes)
                Console.WriteLine($"Expected: {expected.ClrType}");
            foreach (var actual in diagnostic.ActualTypes)
                Console.WriteLine($"Actual: {actual.ClrType}");
            foreach (var correction in diagnostic.Suggestions)
                Console.WriteLine(
                    $"Replace {correction.Start}+{correction.Length} " +
                    $"with {correction.ReplacementText}");
        }

        var valid = Interpreter.AnalyzeExpression<Product>(
            "Price > $minimum and buyer = $user.department " +
            "and Discount = null and Price in $prices",
            schema: productSchema,
            variables: variables);
        Console.WriteLine(valid.IsSemanticallyValid);
    }
}
```

The guide also shows explicit member declarations (instead of reflecting every variable member), denied alias examples, runtime-type analysis, full type/suggestion contracts, and consuming each immutable text snapshot. Preserve safe text rendering and zero-length EOI caret handling from the existing README editor guidance. Explicitly explain that runtime predicates still need matching allowlists/mappings and a separate resolver; analysis is not authorization enforcement at execution.

README gets a short semantic introduction/quick start and `[Semantic analysis guide](docs/semantic-analysis.md)`, with its contents entry updated. Keep the existing syntax-only editor section accurate and link to semantic analysis rather than rewriting its promises. Check repository links and packaged README rendering behavior; no packaging changes are required for P0.

## Risks / Trade-offs

- [Rule drift during extraction] -> Characterize branch-specific outcomes first, use a shared pure layer, and compare with all runtime entry points without analysis invoking those entry points.
- [Recovery hides useful clauses or produces cascades] -> Per-node/scope validity, complete-sibling tests, iterative forward progress, and exact diagnostics on incremental text snapshots.
- [Permission leaks via aliases or types] -> Filter candidates before scoring; deny canonical ancestors; restriction diagnostics contain only entered text and empty type/suggestion lists.
- [Culture/date behavior conflicts with determinism] -> Immutable explicit context, context-dependent-conversion for clock/zone defaults, and a documented runtime-context boundary.
- [Unavailability or value-dependent variable conversion] -> Do not pretend to resolve values; validate static feasibility and document runtime failures independently.
- [Cyclic/nested metadata or shared mutable caches] -> Lazy path traversal, immutable weakly cached type facts, copied caller metadata, and concurrent-analysis tests.
- [Too much metadata API] -> Keep only schema overrides, variable declarations, conversion context, and immutable diagnostics; no configurable fuzzy engine, editor framework, or AST API.
- [Known runtime quirks accidentally corrected] -> Freeze legacy tests and expression shapes; no unrelated runtime repairs as part of semantic analysis.

## Migration Plan

1. Freeze public contracts and runtime characterization tests; add immutable schema/variable/context types.
2. Extract positioned syntax snapshots while proving syntax-only result equivalence.
3. Extract pure runtime rule decisions and run focused sync/async/generic/runtime-type regressions.
4. Add semantic binding, type checking, deterministic recovery, and permission-safe suggestions.
5. Complete adversarial purity/parity tests and the guide/README with compiled examples and resolved links.
6. Release additively on the existing target. Callers opt into typed analysis; existing callers need no migration. Rollback is to stop calling the new overloads; runtime parsing requires no behavior/data rollback.

P0 is complete only after the specification's diagnostics, purity, recovery, documentation, and compatibility criteria pass. All subsequent operator work remains separate.
