# Design

## Context

See [proposal.md](./proposal.md) for the motivation and scope. The core library targets `netstandard2.0`; the existing unit-test project targets `net10.0`, references EF Core InMemory 7.0.9, and exercises predicates in-memory. The README already describes EF Core `IQueryable` usage and explicitly says InMemory does not establish relational translation compatibility. There are no existing repository GitHub Actions workflows or OpenSpec behavior specs.

## Goals / Non-Goals

**Goals:**

- Exercise the same predicate cases with SQL Server and MySQL using relational query execution, without introducing database packages into the core package.
- Keep the common assertion suite independent of provider-specific setup and allow provider-specific behavior to be explicit.
- Make local and CI runs reproducible and self-cleaning.

**Non-Goals:**

- Establish compatibility with every EF Core provider, database version, collation, or every expression the parser accepts.
- Normalize provider semantics or work around translation gaps in the library.
- Reorganize or replace the existing unit-test suite.

## Decisions

### Test project and dependency boundary

Create `src/Kkts.Expressions.EntityFrameworkCore.Tests/` with a project reference to `Kkts.Expressions.csproj`; add it to `src/Kkts.sln`. Target `net10.0`, matching the existing test project. Pin the initial relational matrix to EF Core 10.0.9 and `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9 plus `MySql.EntityFrameworkCore` 10.0.9. Keep all EF Core/provider/Testcontainers references in this test project. Use the Microsoft SQL Server provider maintained with EF Core and Oracle's MySQL provider, which has an EF Core 10-compatible release line. This avoids mixing provider major versions and avoids selecting an EF Core 9-only MySQL provider for an EF Core 10 baseline. Recheck the exact package patch versions and published compatibility at implementation time; update the version tuple together.

### Shared cases, with explicit provider adapters

Use a small common relational test base (or equivalent shared case source) with provider-specific context/fixture adapters. Run the same parsed-string, expected-result cases for both providers, with provider test classes/traits kept separately filterable. For each valid case:

1. Parse with `Interpreter.ParsePredicate<TEntity>` and assert parsing succeeded, reporting its exception/diagnostics if not.
2. Pass the returned `Expression<Func<TEntity, bool>>` directly to `DbSet<TEntity>.Where(...)`.
3. Materialize results from the relational database and assert the explicit expected identifiers.
4. Query the same seeded database with an equivalent handwritten C# predicate and assert its explicit identifiers as an independent baseline.

Never compile the generated expression or switch to client-side evaluation. Keep invalid-expression/parser-validation cases in a separate test class or trait from query translation/execution cases, so a parser failure is not reported as a database translation failure. Do not assert SQL text.

The shared matrix covers the parser's supported scalar comparisons (`=`, `!=`, `<`, `<=`, `>`, `>=` where valid for the operand types), AND/OR/NOT, precedence and explicit grouping, nullable values and null comparisons, string equality/contains/prefix/suffix operations with quote and wildcard-like characters, enums, `DateTime`, and nested/navigation property access where both providers translate the expression. Give each case seeded records and concrete expected IDs. Keep unsupported or provider-specific expressions out of the shared success matrix and document them as such.

### Provider and database baseline

Use SQL Server 2022 build 16.0.4255.1 and MySQL 8.4.4 as the two initial relational engines. Pin SQL Server by its image digest and MySQL by its exact patch tag in the test infrastructure instead of relying on a floating `latest` tag, and update them intentionally with the provider/package matrix.

Use Testcontainers for .NET to start one disposable database container per provider fixture. The test process owns container lifetime; each fixture creates its schema and deterministic seed data once, runs read-only query cases, then disposes the container. Container disposal removes the whole database even after ordinary assertion failures; Testcontainers' resource reaper handles abandoned containers after interrupted runs. Local developers need Docker running and can run the test project directly. CI needs a Docker-capable runner; it does not need a separately managed shared database service.

Keep provider configuration behind the fixture boundary: SQL Server uses `UseSqlServer`, and MySQL uses the selected provider's MySQL options with a pinned server version. The MySQL container pre-creates its test database with the server's default collation, so fixture startup must alter that isolated database to `utf8mb4_0900_bin` before creating the schema; retain a metadata assertion for the effective string-column collation. Do not share migrations or assume identical DDL behavior unless the tested schema requires it; use a small model and `EnsureCreated` if both providers support that schema.

### Provider-specific semantics

Use intentionally configured string collations, recorded per provider, so case/accent behavior is deterministic rather than inherited from machine defaults. Choose common test strings that avoid collation-dependent ambiguity for the shared exact-result cases. Add explicit provider-specific collation cases only where needed, and compare generated and handwritten predicates within that same configured database; do not require cross-provider equality.

Seed shared date cases at whole-second precision. Test additional fractional-second values with explicit provider-specific precision/expectations, documenting the column type and precision each provider uses. Record null-comparison outcomes for each provider against both the generated and handwritten query. Any divergence between databases is documented, not silently normalized. If either selected provider cannot translate a proposed expression, retain it as a separately reported compatibility issue instead of weakening assertions or changing library translation behavior in this change.

### CI and developer documentation

Add a GitHub Actions workflow because the repository currently has none. Run the integration project on .NET 10 with a provider matrix (`sqlserver`, `mysql`) that filters the corresponding provider test class/trait; each matrix job starts and disposes its own Testcontainers instance. Keep CI failure output attributable to provider and test case. Document Docker prerequisites, commands to run one or both providers locally, the exact package/server matrix, collation/null/date configuration, troubleshooting, known provider limitations, and that passing cases are evidence only for that finite matrix.

## Risks / Trade-offs

- [Provider patch versions may not be mutually available at the recorded version by implementation time] → Recheck NuGet and vendor compatibility tables before adding references; pin an internally consistent set and update this design/docs together.
- [Container startup, especially SQL Server, increases test time and can be resource-intensive] → Run provider tests in separate CI matrix jobs and keep fixtures at one container per provider suite rather than one per case.
- [Collation, null semantics, or temporal precision can legitimately produce different results] → Configure and record them, compare each generated expression against handwritten LINQ on the same provider, and avoid cross-provider expected-result equivalence where semantics differ.
- [A passing finite test suite can be mistaken for universal support] → State the exact tested package/server matrix and tested expressions; explicitly disclaim untested expressions, versions, providers, and configurations.
- [A provider-specific translation gap may be discovered] → Report the failing expression and provider/version as a separate follow-up; do not add parser features, SQL rewriting, or client evaluation here.
- [An interrupted local run may leave a container behind] → Use Testcontainers resource cleanup, document the normal Docker cleanup command, and ensure fixture disposal happens in all ordinary failure paths.

## Migration Plan

No production migration is needed: the library API and package dependencies do not change. Add the test project, container-backed tests, CI workflow, and documentation. Rollback consists of removing those test/infrastructure files and solution/CI references; it does not affect consumers or persisted application data.
