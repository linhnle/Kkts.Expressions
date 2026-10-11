# Design

## Context

See [proposal.md](proposal.md) for motivation and [the completion delta](specs/expression-autocomplete/spec.md) for observable contracts.

Observed infrastructure:

- `ExpressionAnalyzer` obtains whole-source tokens from `ExpressionLexer` and validates using `ExpressionSyntaxParser(syntaxOnly: true)`. Recovery resumes at logical operators and appropriate group boundaries. Tokens preserve untrimmed UTF-16 positions; paths are whole tokens, and quotes/escapes prevent false recovery boundaries.
- `ExpressionSemanticAnalyzer` binds complete expressions or reliable recovered ranges. Its nested parser and semantic nodes currently hide expected operand slots, receiver types, and contributing field paths. Incomplete regions with syntax errors are skipped, so simply calling `AnalyzeExpression` is not enough for completion.
- `ExpressionSyntaxTree.Parse` returns no root for incomplete syntax; it is useful for complete expression/tree exchange, not a prerequisite for typing assistance.
- Operator/literal behavior already lives in `ExpressionGrammar`, `ExpressionOperatorRules`, `ExpressionConversionRules`, and `NumericOperands`. `QueryPolicyFieldMetadata` normalizes policy comparison identities; `QueryPolicySourceScanner` and `QueryPolicyExecution` enforce bounded static accounting.
- `ExpressionSchema` exposes public `Fields`, legacy `ValidProperties`, `PropertyMapping`, and `Properties`. `ExpressionQueryContext` also owns an additional allowlist, canonical operator intersections, navigation and collection checks. Its completion entry point must reuse that effective context.
- `QuerySchema<T>.Field` registers scalar results and rejects dotted field names. Public schemas bind exact registrations, never reflected selector inputs or suffix members. Legacy schemas allow explicitly permitted readable paths and exact aliases. This is why `Customer.` is a legacy-path example, not an excuse to widen typed public registrations.
- `ExpressionVariableSchema.Variables` and variable `Members` are immutable declarations. Root/direct dotted lookup is ordinal, explicit child lookup is ordinal case-insensitive, and reflection traversal occurs only where `FromType` enables it. Enumeration must preserve those distinctions.
- `FilterValue` already models owned literal data without arbitrary object formatting. `FilterExpression` has canonical operator spelling and string quoting prior art; its conservative membership handling rejects strings beginning with `$`. `StringParser` escapes only the active quote, not JSON/C# escape sequences. Encoding must prove round-trip fidelity, including trailing backslash edge cases.
- Tests are xUnit in `src/Kkts.Expressions.UnitTest` targeting `net10.0`; the core is `netstandard2.0`. Existing console examples target `net6.0` and have no Monaco/CodeMirror convention. README editor examples are UI-neutral snippets, not runnable editor applications.
- Main `expression-editor-analysis` and `query-policies` specs coexist with unarchived semantic-analysis and expression-field-mapping changes. The latter explicitly overrides legacy public-path/fallback wording for typed public schemas. Completion follows implemented behavior and does not amend/archive those changes.

## Goals / Non-Goals

**Goals:**

- One bounded cursor analysis, shared metadata compatibility/permission decisions, and cheap candidate filtering rather than full predicate parsing for every candidate.
- Explicitly distinguish reliable empty completion, ambiguous context, resource refusal, and truncated candidate sets.
- Provide exact edits that a frontend can apply without knowing the grammar or fixing up prefixes.
- Keep descriptions and type information inside the same public boundary as accepted field names.

**Non-Goals:**

- A second language grammar, independent operator/type table, runtime value discovery, resolver-based suggestions, or compiled expression evaluation.
- Making typed public fields traversable, redefining nullability semantics, or enforcing hint values as an allowed-value set.
- A production editor, language server, editor dependency in core, external suggestion provider, or changes to provider/authorization/tenant semantics.
- Expression-result caches or background prefetch; existing immutable descriptor caches remain usable.

## Decisions

### 1. Add immutable completion contracts and two entry points

Proposed API shapes (to implement, not existing signatures):

```csharp
Interpreter.CompleteExpression(
    string expression,
    int cursorPosition,
    ExpressionSchema schema,
    ExpressionVariableSchema variables = null,
    ExpressionValueSuggestionSchema valueSuggestions = null,
    ExpressionCompletionOptions options = null);

queryContext.CompleteExpression(
    string expression,
    int cursorPosition,
    ExpressionVariableSchema variables = null,
    ExpressionValueSuggestionSchema valueSuggestions = null,
    ExpressionCompletionOptions options = null);
```

Both return `ExpressionCompletionResult` with `Items`, `Diagnostics`, `Status`, and `IsIncomplete`. Items expose `Kind`, `Label`, `InsertionText`, `Start`, `Length`, `Description`, and `TypeInfo` (`ExpressionTypeInfo`, null for untyped punctuation/operators). Stable explicit enum values protect consumers; sample JSON encodes kinds/status as names, not inferred frontend categories. Value items describe their contextual type; null keeps the existing Null type kind. Fields and variables expose declared types/nullability/element types.

`ExpressionCompletionOptions` is immutable and initially contains only `MaxResults` (default 50, range 1..200). Fixed work ceilings are documented completion contracts, not new `QueryPolicy` defaults. Schema input is required; a public empty schema stays empty. Validate argument/null/cursor/surrogate boundaries before lexing.

Prefer these two entry points over many generic/runtime-type overloads: the schema already carries the entity type, and the query context already carries effective restrictions. Neither accepts a resolver. Do not attach completion status to semantic validity or change existing diagnostic-correction items.

### 2. Extend existing parser/binder machinery with cursor-slot observations

Add an internal completion-context mode that reuses lexer tokens and parser transitions. Its output describes the replaceable token/span, decoded prefix, expected slot, enclosing group/list/function, typed receiver/operand, contributing public field identities, and suffix constraints.

Processing:

1. Admit exact text using length and bounded source/scope scanning before deep work. Use policy execution/scanner accounting plus completion-only guards; no synthetic trimmed text.
2. Lex once with a token ceiling. Locate cursor/token boundaries, including a dangling dot, `$`, partial compound operator, and a quoted token.
3. Replay shared syntax transitions through the cursor's editable slot without prematurely demanding end-of-input completeness. Expose a read-only expectation snapshot from `ExpressionSyntaxParser`; do not infer state from token kind alone.
4. Reuse/extract semantic operand descriptors and compatibility probes from `ExpressionSemanticAnalyzer` for reliable operand regions. Carry contributing fields for arithmetic, string concatenation, and implicit Boolean predicates. Extract the existing recovered-range logic rather than create a new logical-boundary splitter.
5. Analyze surviving suffix once for scope balance, slot requirements, existing delimiters, and incompatible adjacent operands. A cursor inside a complete token edits that token; at its end, prefer continuation when the exact operand is complete. Partial unmatched tokens remain completion slots.
6. Mark ambiguous local states unavailable. Independent errors outside the local scope do not suppress reliable results. Do not reinterpret text inside a quote as subsequent clauses.

Compatibility probes call shared literal conversion, numeric promotion, comparison construction/type rules, membership element checks, Boolean rules, and normalized operator permissions. Tiny type-probe expression nodes used by existing operator rules are not executable predicates; do not compose, compile, or evaluate selectors. Membership/function applicability and variable conversion-support checks currently embedded in the analyzer become small shared helpers only where required.

A tolerant AST implementation unrelated to these parsers was rejected: it would duplicate grammar/recovery and drift. The complete-only syntax tree remains unchanged unless a narrowly shared helper is needed; incomplete completion is not gated on it.

### 3. Enumerate descriptors at the requested level, then enforce the public boundary

Enumerate public registrations directly in public mode. In legacy mode, choose candidate external names from nonempty allowlists and exact mappings, or inspect readable member descriptors only at the currently requested level when unrestricted. Root suggestions can include explicitly allowed full nested paths. Do not invent a standalone navigation-prefix item for a prefix that is not itself queryable; a caller can type `Customer.` to request its permitted next-level scalar paths.

Never recursively flatten an entity/variable graph. Cyclic CLR types are harmless because each request only advances the cursor's existing path. A direct dotted declaration can be offered as a full path without inventing traversable intermediate metadata. A variable parent with an incompatible object type can still offer a known directly compatible member path.

For every candidate, before creating its label/description/type output:

- Resolve the external path through `ExpressionSchema` and the current query context.
- Require filterability, allowlists, ancestor permission, canonical navigation/collection allowance, and applicable operator permission. Respect empty operator sets and Equal identity for standalone Boolean fields.
- If a receiver is an arithmetic operand, intersect permissions for all contributing fields, not just its first token.
- Public registrations use result type and public identity, never selector-body traversal. Legacy mappings never rewrite a prefix: buyer -> Customer.Name does not make buyer.Name valid.
- Filter hints and metadata before formatting output; generated suppression diagnostics contain only a generic explanation and cursor span, never hidden canonical names.

A separate unrestricted reflection suggestion catalog was rejected because it could bypass exact mappings, allowlists, or computed-field opacity.

### 4. Reuse literal data for advisory value metadata

Introduce immutable `ExpressionValueSuggestion` entries containing a scalar `FilterValue`, optional label, and optional description, grouped by external field name in `ExpressionValueSuggestionSchema`. Bind/snapshot field-name groups to the supplied schema at configuration time; require only Null/Boolean/Number/String kinds. Use `FilterValue.String` for enum names and application date/Guid/string hints rather than invent another value hierarchy or use arbitrary object `ToString`.

Expose a schema-binding factory/constructor that takes the entity schema and field groups; completion rejects a value-hint schema bound to a different entity schema snapshot. Validate unknown keys, duplicates under field-name identity rules, scalar kinds, compatible conversion under the bound conversion context, and lossless expression representation. Binding accepts null when an existing comparison context supports null lifting; do not reject it solely using the stricter membership-literal conversion probe. Apply the actual operator/list context filter per request. For repeated equivalent hints, the first declared hint supplies presentation metadata. Do not make hinted values exhaustive or change semantic/runtime validation.

Generate enum suggestions from declared names only, not flag combinations or numeric guesses. Boolean/null generation uses shared operator/literal semantics; in particular, comparisons can lift null while null list elements use existing contextual conversion checks. Hint literals with the same value as generated candidates use the explicit hint label/description.

Reuse or extract canonical operator/literal-writing helpers from `FilterExpression` where their contracts fit. A grammar-aware codec must preserve active quote style, escape only the active delimiter, and verify lossless representation using metadata-only literal decoding. Do not introduce JSON-style backslash escaping into the language. Some strings (notably problematic trailing backslashes) cannot safely be encoded under current grammar; reject such hint configuration explicitly. A string beginning with `$` is a valid scalar hint where lossless, but omit it in ambiguous membership contexts using existing rules. Preserve existing conversion behavior; tightly coupled codec fixes require regression tests, not silent runtime changes.

Value-dependent compatible variables remain eligible without resolving values; mark the description as declared/type-compatible, not runtime-available. Whole collection variables use exact existing element compatibility; scalar list slots never accept a collection as a scalar.

### 5. Make complete-token replacement and suffix constraints explicit

Build the edit before ranking the item:

| Editing state | Replacement | Insertion |
|---|---|---|
| `Sta|Tus = ...` | Entire Status token | Complete public `Status` |
| `Customer.Na|me` | Entire dotted token | Complete `Customer.Name` |
| `$u|tcnow` or `$|` | Entire variable token including `$` | Complete `$utcnow` |
| `Price not i|` | Entire identified partial compound operator | `not in` |
| `Status = 'Ac|ZZ'` | Entire quoted token | `'Active'` |
| `Status = "Ac|` | Entire unfinished quoted token to EOF | `"Active"` if representable |
| Whitespace/empty slot | Zero-length cursor range | Selected token plus only required boundary spaces |
| Before existing `)` or comma | No replacement of that punctuation | No duplicate closer/separator item |

The `|` notation is explanatory and not part of input. For a path with deeper segments after the active segment, retain them as candidate constraints; do not complete Customer in Customer.Address.City by throwing away Address.City. Right-hand characters in the active identifier segment are replaced, but outside-token suffix text is preserved. Existing suffix operands must remain type-compatible with an operator edit.

Optional whitespace is not included. For example, inserting `and` at the end of `Price > 1` requires insertion ` and` to avoid `1and`; before an adjacent identifier it also requires a trailing space. A closer inserts only `)`; an opening list item inserts only its opening delimiter, not an auto-paired close. Commas are suggested only between completed list elements and only if another item is allowed. No automatic quote repair outside replacing a bound literal.

Validate result length and static budget deltas on the edited source. Replacement is not a second condition/item. Do not perform a full re-lex/re-bind for each candidate: reuse source accounting and cursor-slot/suffix constraints, invoking a bounded shared admission probe only for grammar-sensitive deltas.

### 6. Deterministic filtering and bounded refusal

Prefix matching and dedup/rank follow the spec, never display-label ordering or fuzzy correction. Use accepted public spelling for names, canonical existing operator spellings, decoded literal value identity, and binding-valid variable identity. Preserve distinct public aliases and case-sensitive variable roots. Operator canonical order is a presentation order, not another applicability table.

Limits:

| Budget | Completion ceiling / behavior |
|---|---|
| Exact source length | 16,384 UTF-16 units; earlier tighter policy length wins |
| Tokens | 8,192; stop lexing at the first extra token |
| Simultaneously open syntactic scopes | 64, including group/function/list scopes outside quotes; tighter applicable policy depth wins |
| Candidate descriptor visits | 4,096 total, including hidden/rejected paths and value/variable descriptors |
| Results | Default 50, configurable 1..200, applied after permission/filter/dedup/rank |
| Combined diagnostics | 32 with existing truncation-marker convention |

Candidate visits use deterministic owned declaration order; at a reflected level use stable descriptor ordering from the shared metadata snapshot (prepare that order once with the descriptor snapshot, not by sorting an unbounded graph each request). When the visit ceiling is reached, sort only proven eligible candidates from the visited prefix, return bounded results, and set `IsIncomplete`; do not claim global top matches across unvisited metadata. This bounded subset rule is documented. Source/token/scope refusal returns no items with limit status and a positioned diagnostic.

Use iterative group/unary/path traversal and bounded local storage. Shared scanner APIs must support early completion guards without imposing these ceilings on existing entry points. No-policy completion still has fixed safety ceilings; no-policy analysis and predicate construction remain unchanged.

Already exceeded policy resource budgets stop completion. At a policy boundary, suppress candidates whose actual insertion would be prohibited: new conditions/items, excess text/depth, denied paths/operators. Edits within an existing reserved slot and matching closers can remain eligible. Policy permission failure elsewhere can produce diagnostics without necessarily poisoning a reliable independent cursor context. Unknown variable cardinality remains a deferred runtime check.

Reject reparsing all 4,096 candidates and cross-call expression caching: neither is justified by measurements, and reparsing would make ordinary keystrokes unnecessarily allocation-heavy.

### 7. Keep the runnable CodeMirror example isolated

Use CodeMirror 6 because the repository has no incumbent editor and its completion source maps naturally to a text document's UTF-16 offsets. Add `examples/Kkts.Examples/Kkts.Examples.Autocomplete/` with a small `net10.0` sample host (matching the active unit-test runtime), project reference to core, and sample-local frontend package manifest/lockfile. This does not retarget existing `net6.0` examples or add dependencies to core. Use a small development server/proxy and documented two-command startup, not a production app.

The host serves a single metadata-owned completion/diagnostics operation. Browser requests carry only exact text, offset, and snapshot identity; schema, policy, variables, and literal hints stay server-owned. Serialize a narrow DTO: item kinds, labels, insertion/ranges/descriptions, safe public type summaries, status, and diagnostic spans. Never serialize `ExpressionSchema`, selector trees, mappings, arbitrary `Type` objects, or resolver values.

Configure decimal Price, string Status hints Active/Pending/Closed, DateTime CreatedAt and explicitly declared utcnow/startOfMonth; configure a permitted legacy Customer.Name path for the nested example. Show typed computed-field configuration in the guide separately so the sample does not imply dotted typed registrations are valid.

Frontend responsibilities:

- `view.state.selection.main.head` is the UTF-16 cursor offset; send the exact `doc.toString()` without newline normalization. For line/column examples use `doc.line(n).from + column`, documenting zero-based versus one-based coordinates.
- Map item kind to CodeMirror completion type. Use a custom `apply` callback for each item's exact `Start..Start+Length`, rather than assume every result shares CodeMirror's word range.
- Debounce backend requests by 150 ms; abort superseded fetches where possible and compare both document/cursor snapshot and monotonic request id before publishing completion or diagnostics. Cursor-only moves also invalidate results. Recheck snapshot before accepting an edit.
- Map existing diagnostics to CodeMirror lint ranges on that snapshot, including zero-length EOF positions; render labels/descriptions as text, never raw HTML. Completion and diagnostics use the same configured query context, but completion status is not semantic validity.
- Document sample prerequisites, dependency install, host/client startup, configured fixtures, and a small smoke test for suggestions, accepted edits, and stale-response rejection.

A production HTTP API or client-supplied arbitrary query metadata was rejected. HTTP is merely the isolated .NET-to-browser sample bridge; core remains synchronous and framework-independent.

### 8. Verification uses exact edits, purity instrumentation, and measured workloads

Add xUnit contract/context/replacement/policy/purity test classes beside existing analysis tests. Share small fixtures or theory data where clear. Every representative suggestion test checks item shape and applies `text[..Start] + InsertionText + text[Start+Length..]` (using target-compatible slicing helpers in core-facing examples), then verifies exact resulting text and established semantics. Incomplete accepted prefixes need not become full predicates.

Test null comparison lifting, numeric promotion, value-dependent declared variable conversion, canonical operator intersections, alias identity, typed selector opacity, denied hint descriptions, all membership delimiters, compound partial operators, quoted escapes/trailing backslashes, interior suffix constraints, supplementary characters and CRLF.

Purity counters cover binding and valid/invalid/incomplete/over-budget completion. Throwing getter/resolver/conversion/selector/enumerator fixtures must remain untouched; a separate runtime control proves counters work.

Use deterministic counters to verify source/token/scope/descriptor ceilings at N and N+1. A representative performance harness lives with the sample/test tooling, not core dependencies: warmed Release, at least 100 warmups and 1,000 measured requests, at most 4,096 source units and 256 relevant descriptors, repeated typing snapshots plus middle edits and recovery. Report p50/p95 and per-request allocations with machine/runtime/workload configuration; required p95 <=20 ms and mean allocation <=256 KiB. Keep wall-clock performance acceptance an explicit reproducible command on a documented reference machine, not a flaky absolute-timing unit assertion. Adversarial ceilings have separate counter/termination checks.

Run targeted completion/shared-rule tests first, then all existing unit analysis/predicate tests and relational suites per documented provider setup. Do not weaken thresholds or skip a compatibility failure to claim completion; report unavailable relational infrastructure explicitly.

## Risks / Trade-offs

- [Parser/binder expectation extraction could change legacy recovery] -> Keep completion observations opt-in; assert original token/diagnostic and predicate outcomes in shared-helper regression tests.
- [Strict public mode conflicts with an illustrative nested example] -> Keep it on explicitly permitted legacy paths; show separately registered scalar projections in typed-schema docs.
- [Current escape grammar cannot encode every string] -> Prove codec round trips, explicitly reject unrepresentable hints, and suppress context-specific membership ambiguity rather than add syntax.
- [Metadata levels can be very large] -> Bounded descriptor-prefix results with `IsIncomplete`; no recursive flattening, per-request unbounded sort, or false claim of global best matches.
- [Performance targets depend on hardware] -> Publish environment/workload and reference measurements; enforce structural ceilings in portable tests and timing in a dedicated Release command.
- [Backend races can apply incorrect offsets] -> Snapshot checks before response publication and acceptance, including cursor-only changes.
- [Hints could be mistaken for validation/authorization] -> Document advisory semantics and independent runtime query enforcement; server-owned sample configuration only.
- [Shared helper extraction affects expression/tree literal serialization] -> Reuse narrowly, preserve existing behavior with conversion tests, and fix only genuinely coupled defects.

## Migration Plan

1. Land additive contracts and internal helper extraction with unchanged existing entry-point regressions.
2. Land bounded context recognition, metadata candidate generation, exact edits, and policy admission with focused tests.
3. Add the runnable sample, guide, README links, and reproducible performance checks; verify build/run/links.
4. Existing consumers need not migrate. Opt-in consumers call completion with the same schema/variables/context as analysis, and still validate actual input at runtime.
5. Rollback removes opt-in completion usage/sample integration; no data migration, cache invalidation, or changes to existing schema registrations are required.
