# Proposal

## Why

Editors currently receive syntax diagnostics but cannot identify misspelled or restricted fields, incompatible operands, or undeclared variables without attempting runtime predicate construction. Metadata-only semantic analysis is the next P0 editor feature, ahead of additional operators, so callers can safely diagnose expressions while users type.

## What Changes

- Add generic and runtime-entity-type `AnalyzeExpression` overloads accepting an entity schema and declared variable metadata, without changing `AnalyzeExpression(string)`.
- Reuse reflected entity members, case-insensitive property paths, exact property mappings, and query allowlist conventions. Distinguish unknown properties from known properties not permitted for querying.
- Check literal conversions, operand types, existing operator applicability, membership element types, nullability, and Boolean predicate results using rules shared with runtime construction.
- Validate explicit variable references and member paths using declarations, never runtime values. Preserve declared bare-variable fallback where no entity member exists, but never let fallback bypass query permissions.
- Return stable semantic codes, original-input UTF-16 spans, structured type information, and conservative permission-safe correction suggestions alongside unchanged syntax diagnostics.
- Retain independent diagnostics after recovery; suppress semantic cascades from incomplete or invalid syntax and invalid child expressions.
- Use immutable conversion context metadata for deterministic culture-sensitive analysis. Separately diagnose temporal literals requiring the current date or local time zone; runtime conversion defaults remain unchanged.
- Add `docs/semantic-analysis.md` with complete, compile-checked C# examples and an editor-consumption guide; add a concise README introduction, quick start, and relative guide link.

## Capabilities

### New Capabilities

- `expression-semantic-analysis`: Metadata-only schema and variable binding, shared type rules, semantic diagnostics, deterministic recovery, safe suggestions, and the public semantic-analysis documentation contract.

### Modified Capabilities

None. The existing `expression-editor-analysis` capability remains syntax-only, including its definition of completeness, classifications, spans, diagnostics, and no-evaluation guarantees. Semantic analysis composes that contract rather than redefining it.

## Impact

- Core `netstandard2.0` library: additive public metadata/result/diagnostic types and `Interpreter` overloads; internal analyzer/parser recovery plumbing, member metadata, and pure type/conversion rule extraction.
- Runtime comparison, arithmetic, membership, and literal conversion code will consume shared decisions without changing runtime resolution, conversion defaults, expression shape, or legacy error reporting.
- Extend xUnit coverage near `ExpressionAnalysisTest`, conversion/culture, mapping, variable, arithmetic, and predicate regression tests. Existing EF Core tests are regression checks only; analysis stays independent of EF Core and editor frameworks.
- No new operators, dependencies, autocomplete, UI, language server, SQL translation validation, or runtime semantics changes.
- Acceptance requires all requested semantic cases, exact diagnostic contracts, multiple-error and incomplete-input recovery, throwing/counting resolver and getter noninvocation proofs, existing syntax/runtime regression suites, a compatible library build, compiling documentation examples, and resolving documentation links.
- Deliver in phases: freeze metadata/contracts; expose recoverable positioned syntax; extract and verify pure runtime rules; implement binding/type checking and suggestions; finish adversarial/parity tests and documentation. No implementation is authorized by this proposal.
