# Tasks

## 1. Test project and infrastructure

- [x] 1.1 Create `Kkts.Expressions.EntityFrameworkCore.Tests` targeting `net10.0`, reference the core project, add the test and Testcontainers packages, and register it in `src/Kkts.sln`; verify the project builds and `Kkts.Expressions.csproj` has no EF/provider dependencies.
- [x] 1.2 Add provider fixture abstractions, a small relational entity model, deterministic seed data, and per-provider container lifecycle for SQL Server 2022 and MySQL 8.4 with pinned image versions; verify fixture startup creates the schema/data and disposal removes the container on success and failure.
- [x] 1.3 Configure and document explicit provider-specific string collations and temporal column precision in the model; verify the containers accept the schema and seeded whole-second/fractional-second test values.

## 2. SQL Server and shared predicate suite

- [x] 2.1 Implement the reusable relational test cases and SQL Server adapter for supported comparisons, AND/OR/NOT, precedence/grouping, nullable and null comparisons, string operators with special characters, enums, dates, and nested/navigation properties where translated; verify each generated predicate is passed directly to `IQueryable.Where`, executes on SQL Server, and returns the explicit expected IDs.
- [x] 2.2 For each successful SQL Server case, query the same database with an equivalent handwritten C# predicate and verify it returns the same explicit expected IDs; keep parser-invalid expressions in a separate test class/trait and verify its failures are reported as parser validation, not translation/execution errors.
- [x] 2.3 Add SQL Server-specific assertions/documentation for configured collation, null behavior, and date-time precision without SQL snapshots; verify them with `dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests --filter "Provider=SqlServer"`.

## 3. MySQL provider reuse

- [x] 3.1 Implement the MySQL 8.4 adapter using Oracle `MySql.EntityFrameworkCore` 10.0.9 and reuse the shared case source without copying its assertions; verify `dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests --filter "Provider=MySql"` executes against MySQL and checks explicit expected IDs against both generated and handwritten predicates.
- [x] 3.2 Add explicit MySQL collation, null, and temporal precision cases/documentation where semantics differ from SQL Server; verify all such outcomes are asserted within MySQL and the shared suite does not assume cross-provider equality.
- [x] 3.3 Document local Docker prerequisites, commands for one/both provider suites, the package/server version matrix, known limitations, provider-specific collation/null/date behavior, and the tested-cases-not-guarantee boundary in the repository README; verify each documented command matches the implemented test filters and runs successfully.

## 4. CI and end-to-end verification

- [x] 4.1 Add a GitHub Actions workflow that restores/builds the test project and runs a SQL Server/MySQL provider matrix on .NET 10 with Docker-backed Testcontainers; verify each matrix entry filters and runs its corresponding provider tests.
- [x] 4.2 Run the full integration project against both containers and run the existing unit-test project; verify generated expressions remain server-side, all tests pass, and test/container failures identify the provider and case.
- [x] 4.3 Review the package graph and test configuration for scope leakage and unsupported compatibility claims; verify no EF/provider dependency or code change was added to the core library and documentation names only the tested provider/server matrix.
