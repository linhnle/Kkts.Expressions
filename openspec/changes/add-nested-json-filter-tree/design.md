# Design

## Context

See [proposal.md](proposal.md) for motivation and scope.

Observed implementation:

- `Filter` is a sealed mutable DTO with trimmed Property/Operator strings and a string Value. `FilterGroup.Filters` is a mutable List. Their extension builders use ordered `Expression.AndAlso`/`OrElse`.
- Legacy empty behavior is not uniform. The throwing group-sequence builder removes empty groups and returns an always-true predicate when none remain. The evaluation builder does not perform that same removal; its empty-group body path can fail. Null checks and validation/exception behavior also differ. These are compatibility constraints, not cleanup opportunities.
- `ConditionOptions` processes Filters, FilterGroups, then Where and combines accumulated predicates. The policy context performs its own preparation and processes Where, Filters, then groups. Neither order should change.
- `Interpreter.BuildBody` may resolve nonstring-target string values heuristically. Membership can parse `value.ToString()` through `Nodes.ArrayList`; that parser removes list quotes before deciding whether a value starts with the configured variable prefix. Calling those wrappers directly would violate literal/reference separation and the no-string-reparse requirement.
- `StringExtensions.Cast` supports existing scalar families, while legacy temporal parsing uses CurrentCulture, mutable global formats, and local/date defaults. `ObjectExtensions.Cast` uses Convert.ChangeType and has type-specific limitations. Reuse the decisions and core expression primitives, not every wrapper indiscriminately.
- Semantic metadata and policies already exist on disk. `ExpressionSemanticAnalyzer` has a private non-executable reducer whose values lose original operand/operator structure. `ExpressionSyntaxParser` and runtime nodes are not an interchangeable public JSON model.
- `ExpressionDiagnostic.InputPath` already supports structured locations. Policy execution includes bounded membership snapshots, condition counters, 32-diagnostic caps, field-path checks, and failure isolation.
- Main query-policy specs explicitly say no recursive DTO and no recursive structured input. The delta intentionally changes only those exclusions and adds separate tree accounting.
- Core has no PackageReference and targets netstandard2.0. Unit tests use xUnit on net10.0; relational tests have SQL Server/MySQL Testcontainers fixtures and compare generated versus handwritten result IDs.
- `add-expression-semantic-analysis` is still in progress. Shared conversion/recovery work must be integrated with the current source and not assumed complete because public types exist.

## Goals / Non-Goals

**Goals**

- Make the public value/tree model immutable and exclusive by construction; strict decoding must still diagnose invalid external input.
- Keep predicate construction, metadata validation, and conversion as distinct operations sharing binding and operator/conversion decisions.
- Preserve ordered short-circuit expression semantics and explicitly bound every representation transition.
- Guarantee semantic round trips only where existing grammar/runtime behavior can represent the same values under a matching conversion context.

**Non-Goals**

- General expression serialization, public arithmetic/function ASTs, preservation of text trivia, optimization/reordering, or fixing unrelated legacy defects.
- New provider translation behavior, arbitrary object values, user conversion execution during analysis, collection-variable spreading, or accepting untrusted policy configuration.

## Decisions

### 1. Exclusive immutable public model, separate positioned internal syntax

Proposed additive public types:

- `FilterNode`: sealed immutable root/node type; factories `And(IEnumerable<FilterNode>)`, `Or(...)`, `Not(FilterNode)`, `Condition(string field, string op, FilterValue value)`. A public kind discriminant and read-only relevant properties expose structure, but there is no discriminator in JSON. Lists are snapshotted and child order retained.
- `FilterValue`: sealed immutable value union with Null, Boolean, Number, String, Variable, and Collection kinds. Number retains validated JSON token text, not a pre-rounded Double. Variable stores the unprefixed path. Collection stores literal/scalar-reference slots.
- `FilterOperationResult<T>`: immutable Succeeded, Result, Diagnostics, IsTruncated; failure has null Result. Used for decoded trees, metadata validation results, and conversion output. Construction continues using existing EvaluationResult and its diagnostic projection.

Factories throw explicit argument errors for invalid programmatic shapes; external codecs return positioned validation failures. A cycle is structurally prevented by immutable factories but traversal guards still reject cycles if encountered through an invalid integration. Reused child instances are permitted and counted per occurrence. No exposed arbitrary object conversion hook is added.

Alternatives rejected: one mutable DTO with nullable And/Or/Not/Field fields (ambiguity-prone); inheritance plus a serialized discriminator (changes the requested shape); exposing runtime `Node`/private semantic nodes (mixes executable syntax, recovery, and JSON responsibilities).

### 2. Exact JSON contract and codec

Keep the desired contract unchanged for its example:

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

This JSON is one representative nested predicate; it is not the full symbolic `(A OR B) AND NOT (C OR (D AND E))` example. Test both separately rather than asserting they are identical.

The only added value shape is an explicit reference:

```json
{ "field": "Id", "op": "in", "value": { "variable": "user.ids" } }
```

`"$user.ids"` remains a literal string. Keys are lowercase and case-sensitive, unknown keys and duplicate keys fail, and comments/trailing commas are rejected. Missing value differs from explicit null. Empty logical lists fail; one-child groups remain valid. Membership lists may be empty; empty IN/NOT IN retains existing false/true semantics.

Use System.Text.Json, approved by the user, selecting a maintained package release that supports netstandard2.0 during implementation. Add one core dependency, not a companion package or hand-written JSON parser. Provide a strict `FilterTreeJson` facade (`Serialize`, `Deserialize`, `TryDeserialize`) and a dedicated converter for normal System.Text.Json use. The facade is the policy-aware ingress and supplies controlled reader/writer settings.

Decode/encode iteratively with Utf8JsonReader/Utf8JsonWriter and explicit frames. Do not JsonDocument-parse an unbounded graph before checking tree limits, call recursive JsonSerializer per child, or use library default depth 64 as an undisclosed semantic tree limit. The facade separates logical depth from JSON object/array nesting and configures the JSON reader accordingly. Consumers using JsonSerializer directly must configure its transport depth separately; documentation must distinguish that explicit transport cap from QueryPolicy depth. All decoded nodes still undergo independent runtime policy validation.

ConditionOptions' null FilterTree is ignored on serialization; the new converter applies only to the new model. Do not attach converters or renaming rules to old DTOs or change the existing Property/Operator/Value payloads. Newtonsoft.Json support is not part of the initial built-in codec; clients can explicitly adapt the documented contract.

### 3. Typed value encodings and schema-directed conversion

| Entity target | JSON encoding | Conversion boundary |
| --- | --- | --- |
| String | JSON string | Preserve characters exactly; never resolve a prefix-looking string |
| Boolean | Boolean; supported textual Boolean | Reuse existing applicability/conversion decisions |
| Char | One-character string | Reject incompatible lengths/ranges |
| Signed/unsigned integer | Exact number; supported numeric string | Parse directly to target, range-check before emission |
| Decimal | Exact number; supported numeric string | No Double intermediary or silent rounding |
| Single/Double | Finite number; supported numeric string | Target range checks; nonfinite values fail |
| Enum | Name string or supported underlying integral value | Existing case-insensitive name/underlying rules |
| Guid | Standard Guid string, canonical output `D` | Built-in parsing only |
| DateTime | Full ISO string, canonical round-trip form | Explicit immutable context; no current-date or implicit local-zone defaults |
| DateTimeOffset | Full ISO string with explicit offset or Z | Preserve supported offset/instant precision |
| TimeSpan | Invariant constant-format string | Support negative/subsecond durations; avoid legacy CSV interpretation |
| Nullable scalar | Same scalar encoding or null | Existing lifting/null operator applicability |
| Membership | Array of the above or whole collection reference | Per-element target type; reject nested arrays/collection spreads |

Nullable wrappers are supplied by entity schema, not JSON type tags. Guid/enum/temporal strings are intentionally ordinary strings on the wire; schema determines their CLR type. The schema is required for type-preserving expression exchange. Reject unsupported CLR objects rather than invoking their ToString, converters, or serializers. A built-in scalar factory can format trusted CLR scalar types using explicit built-in formatting only; no arbitrary `object` fallback.

Extract pure contextual conversion decisions from the analyzer/runtime helpers and reuse them for tree literals. Apply the schema's immutable ExpressionConversionContext to new-tree literal conversion; preserve legacy ambient conversion paths. Runtime variables retain existing value-resolution/conversion rules, including value-dependent failures. Same-type trusted values must not be routed through unsupported Convert.ChangeType paths.

Numeric token retention is needed for UInt64/Decimal boundaries and text budgets. Never silently turn a JSON Boolean into a String field value or flatten arbitrary objects: only conversions already supported and verified by the shared rules are permitted. Format/schema conversion failure gets an actionable incompatible-operand/value diagnostic.

Alternative rejected: `object Value` plus generic JSON materialization/ToString (lossy numbers, ambiguous objects, application-code execution and inconsistent codecs).

### 4. APIs and direct expression construction

Proposed API families, with generic/runtime-type sync/async counterparts where construction is involved:

| Surface | Operation |
| --- | --- |
| `FilterNode` extensions | `BuildPredicate<T>`, `TryBuildPredicate<T>`, runtime Type variants, Async variants |
| `ExpressionQueryContext` | Tree overloads of BuildPredicate/TryBuildPredicate and Async; `ValidateFilterTree` |
| `FilterExpression` | `Parse(string, ExpressionSchema, ExpressionVariableSchema = null)` and `Format(FilterNode, ExpressionSchema, ExpressionVariableSchema = null)` returning conversion results |
| `ExpressionQueryContext` | `ParseFilterTree(string, variables = null)`, `FormatFilterTree(tree, variables = null)` with policy enforcement |
| `FilterTreeJson` | Strict codec operations, including policy-context-aware decoding; no variable resolver parameter |
| `ConditionOptions` | Optional `FilterNode FilterTree` integrated into existing condition builders |

Standalone tree builders derive schema from T/Type plus existing validProperties/propertyMapping arguments; explicit context operations use the server-owned schema/policy and conversion context. Metadata validation requires explicit variable declarations, while construction may resolve variables without declarations as existing runtime APIs do. Type mismatch/null top-level arguments are explicit argument errors, not malformed-user-input success fallbacks.

Traverse with explicit frames: structural validation and static budgets/binding first, then ordered runtime resolution/leaf construction, then ordered postorder expression assembly with one ParameterExpression. Emit AndAlso, OrElse, Not directly. Do not balance/reorder by running application code; preserve existing left-to-right leaf processing and short-circuit order.

Extract/reuse typed comparison and membership emission primitives from Interpreter below the string/resolver heuristics. Resolve only FilterValue.Variable through VariableResolver and existing async cancellation paths; convert literal/collection elements directly from their tagged values. Never pass new membership arrays into ArrayList.DrawValue/ParseValues or serialize them as CSV. Maintain bounded snapshots for resolved collection variables.

Static failures are collected before resolver initialization. Runtime failures retain exceptions and structured diagnostics, with null Result. Programmatic configuration errors are not disguised as query diagnostics.

### 5. Policy counting is representation-aware

Preserve the existing QueryPolicy constructor and its binary signature. Add `MaxFilterTreeDepth` and an immutable `WithMaxFilterTreeDepth(int?)` copy operation, avoiding a changed optional-parameter signature. QueryPolicy.Recommended explicitly sets tree depth 16; default/old policies leave it unlimited.

- Leaf depth is zero. Every And/Or/Not adds one; an AND -> NOT -> leaf path has depth two. Charge supplied wrappers before any canonicalization.
- Keep MaxParenthesisDepth for actual text and its existing legacy FilterGroup convention. No new tree charge arises from the private legacy adapter or ConditionOptions' synthetic AND.
- Charge one atomic condition per tree condition; NOT and groups add none.
- Sum raw supplied field/op and value text for tree length: all literal strings, original JSON numeric tokens, reference names, and `true`/`false`/`null`; count every array slot and duplicate. No key/delimiter/escape overhead and no application ToString. CLR-built numeric factories supply invariant token text as part of value creation.
- Condition operation aggregates tree, Where and legacy input budgets; ordering remains separate. Preserve existing input preparation order and append tree preparation.
- Reuse canonical alias permissions, normalized operators, navigation/collection classification, and membership N+1 snapshot/disposal/cancellation/unverifiable-query rules.
- Enforce limits while reading/traversing, before descending/allocating the excess branch; avoid unbounded call stacks even when limits are unlimited. Independent host transport/body-size limits remain application-owned.
- Parsing text to a tree checks original text budgets and resulting tree depth. Formatting checks input tree budgets and actual emitted text length/parenthesis depth with a bounded output writer. Canonicalizing must not hide excessive supplied wrappers.

Existing operator sets intersect across canonical aliases, and known restricted fields never fall back to variables. New tree failures use the same rule codes except the new `query-policy-filter-tree-depth-exceeded`. JSON Pointer does not replace existing structured paths on legacy results.

### 6. Diagnostics use the existing common model

Reuse ExpressionDiagnostic with InputPath plus zero nontext Start/Length. Tree-root pointer is `""`; examples: `/and/1/not/field`, `/and/0/or/1/op`, `/and/1/value/2`. Embedded trees use `/FilterTree/...`. Escape pointer segments per RFC 6901. Text conversion keeps source UTF-16 spans; do not invent text spans for JSON pointers.

New structural codes are specified in the tree delta; unsupported conversion uses `filter-conversion-unsupported`. Retain existing unknown-property, property-not-queryable, undeclared-variable, unknown-variable-member, incompatible-operand, operator-not-applicable, context-dependent-conversion, and policy codes when applicable. Native JSON exceptions are wrapped by the strict facade into an invalid-json result with original error context, not broad silent catches.

Cap policy-aware diagnostics at 32 and retain truncation/deduplication. Restricted errors expose only submitted external locations, not canonical names/type details or runtime variable values. Suggestions, if projected from shared binding, must use permitted external spellings; no autocomplete feature is added.

### 7. Legacy inputs use a compatibility adapter, not the new public validator

Introduce a private composition representation shared by new tree and old adapters. Legacy leaf records preserve their original Filter and evaluator rather than constructing a new public condition with a typed literal. They therefore retain trimming, string membership grammar, variable heuristics and diagnostics.

Map populated Filter sequences to ordered AND; each FilterGroup to ordered AND; group sequences to ordered OR. Before composition, preserve the entry point's original preprocessing, exception timing, enumeration behavior, and policy locations. Internal compatibility nodes may represent existing empty/always-true or failure behavior, but these are not public JSON node shapes.

Characterize all entry points first. In particular, do not route every group builder through the throwing builder's empty-group removal or “fix” evaluation empty/null quirks. Preserve established lambda/body failure behavior where relevant, with the existing evaluator as the compatibility boundary. Extract shared composition only where its outputs match these tests.

ConditionOptions appends tree processing after existing inputs; policy context appends it after its own existing order. All supplied input predicates combine with AND. When no tree is present, leave legacy condition failure/partial-state behavior unchanged. When a tree is present and any input fails, return an explicitly invalid condition without usable predicates or ordering; this stronger new-input guarantee must be tested and documented.

Alternative rejected: replace every legacy builder with public-tree validation (breaks empty/null/failure and variable behavior), or let tree override Where (silently removes constraints).

### 8. Conversion uses positioned syntax, not runtime evaluation

Extract only the complete-input positioned expression structure needed by conversion from the current lexer/syntax and semantic precedence rules. Share pure binding/applicability/conversion decisions with analysis and retain original source provenance. Do not invoke ExpressionParser.Parse, BuildNode, Constant.Build, or resolver initialization to convert.

Representable subset:

- Ordered logical composition and grouping.
- Field-left existing comparisons against literal/scalar reference.
- IN/NOT IN with literal/scalar-reference slots or whole collection reference.
- Existing contains/startsWith/endsWith forms with field receiver and a representable argument.
- Bare Boolean entity fields normalized to Equal(true), only after parity checks.

Reject arithmetic/computed operands, property-to-property or reversed comparisons, entity-backed membership, standalone Boolean variable/constant predicates, and other unsupported syntax. Do not fold arithmetic or invert/reorder comparisons. Conversion failure has no partial result, with the smallest useful unsupported source span/pointer; permission denial takes priority over exposing restricted metadata.

Canonical text uses normalized comparisons (`=`, `!=`, `<`, `<=`, `>`, `>=`, `in`, `not in`), canonical existing string-function syntax, lowercase `and`/`or`, `not (...)`, standard brackets for lists, invariant numeric text, and escaped single-quoted strings. Use existing precedence and associativity to add required parentheses; preserve child order. Function receiver aliases keep permitted external spelling, not restricted canonical paths. Variable output uses the configured expression prefix captured once per operation; stored references remain unprefixed.

Quotes/escape handling must be proved against the executable grammar, not only syntax highlighting. In particular, ArrayList can reinterpret quoted prefix-starting strings as variables. If no existing spelling preserves such a literal, return unsupported formatting; direct tree construction still supports it. Similarly reject formatting of numeric/temporal values whose text cannot preserve runtime meaning. The representable subset is therefore value-sensitive, not just node-kind-sensitive.

Round-trip equivalence is defined under matching schema/conversion context and fixed runtime variable values. Do not claim ambient-culture/time-zone-independent equivalence for legacy expression execution. For deterministic literals, verify both conversion results and runtime predicates; if a temporal form cannot agree with legacy runtime under the documented context, it is unsupported rather than silently normalized. Preserve offsets/precision and target-typed values, not original quote/operator trivia or redundant group count.

The unfinished semantic-analysis change remains independent. Reuse its existing types and implement narrowly necessary pure rule/syntax extraction with existing analyzer regression coverage. Do not mark its unrelated recovery/docs tasks complete.

### 9. Acceptance and documentation

Add focused tree JSON/model, construction, conversion, policy, condition, legacy characterization, and documentation-example tests in the existing unit-test project. Use handwritten predicates over representative records for both nested examples, all scalar families, nullable membership, variables, escaping and numeric extremes. Test both semantic directions and stable reformatting rather than whitespace identity.

Extend shared relational assertions and both provider test classes with nested mixed logic, NOT/membership/null cases, an alias, and a resolved variable case. Reuse fixtures/model where possible; do not introduce providers or require SQL shape equality across databases. Compare actual returned IDs with explicit expected IDs and handwritten predicates on each provider.

Documentation work during implementation: create `docs/nested-filters.md`, add a README example/link, update directly related query-policy and semantic-analysis references and old “no filter shapes” statements. Include complete API snippets, pointer examples, separate depth rules, explicit reference syntax, type table, legacy migration, value-sensitive conversion limits and server-owned policy caveats. Compile snippets in unit tests and resolve local links.

## Risks / Trade-offs

- [Legacy entry-point quirks] -> Characterize before refactoring; isolate legacy evaluators/preprocessing and preserve old diagnostics rather than repairing unrelated defects.
- [In-flight semantic extraction] -> Integrate with actual current source, share only necessary rules, and run syntax/semantic/runtime regression tests; do not depend on unfinished artifacts as if already delivered.
- [JSON dependency increases transitive footprint] -> Keep one compatible System.Text.Json dependency, no target-framework increase, no global converter changes; verify package/build compatibility.
- [Deep or wide input] -> Iterative decoding/traversal, supplied-shape depth accounting, aggregate condition/text budgets and bounded resolved collections; document application-owned body/time limits.
- [Literal identity cannot always survive old membership grammar] -> Make conversion explicitly value-sensitive and return unsupported; never compromise direct tree semantics.
- [Legacy ambient temporal defaults] -> Immutable context for new literals and conservative conversion boundaries; parity tests in matching contexts and explicit diagnostics elsewhere.
- [Transport serializer limits differ from logical depth] -> Control settings in the facade and document direct JsonSerializer configuration separately.

## Migration Plan

1. Land characterization/pure shared-rule extraction with no legacy public behavior changes.
2. Add opt-in tree/codec/builders/policies/conversion and the optional condition property.
3. Keep existing clients and payloads unchanged. Clients migrate OR-of-AND filters to the tree only when nested logic or typed values is needed.
4. Applications configure a server-owned context and use policy-aware ingress/builders. Do not trust client-side conversion/validation as authorization.
5. Publish guides with supported exchange boundaries. Rollback uses legacy input/API paths; no persisted-payload rewrite or removal of old APIs is required.
