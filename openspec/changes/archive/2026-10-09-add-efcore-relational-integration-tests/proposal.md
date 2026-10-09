# Proposal

## Why

Kkts.Expressions documents applying generated predicates to EF Core queries, but the current test project uses EF Core InMemory and does not prove that relational providers can translate or execute those expression trees. The README already warns that provider translation and null behavior vary; an isolated SQL Server and MySQL integration suite will turn that gap into repeatable, provider-specific evidence.

## What Changes

- Add `Kkts.Expressions.EntityFrameworkCore.Tests`, a .NET 10 test project that keeps all EF Core and provider dependencies out of the core library.
- Start with EF Core 10.0.9, `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9, and Oracle's `MySql.EntityFrameworkCore` 10.0.9. Run against SQL Server 2022 and MySQL 8.4.
- Build reusable, provider-parameterized database tests, starting with SQL Server and then reusing the cases for MySQL. Pass parsed expression trees directly to `IQueryable.Where`, execute against each relational database, and compare results to handwritten predicates on that same database.
- Cover supported comparisons and logical operators, precedence/grouping, nullable and null cases, string operators and special characters, enums, dates, and nested/navigation properties where supported. Keep parser validation failures distinct from provider translation/execution failures; record provider-specific collation, null, and date-time precision behavior rather than assuming identical semantics.
- Provide isolated local and CI database lifecycles with deterministic seed data and cleanup, add CI execution for both providers, and document setup, package/server versions, known limitations, and the boundary between tested cases and compatibility guarantees.
- Do not add parser features, change public APIs, or implement provider-specific SQL translation. Any discovered incompatibilities are reported as findings for separate follow-up work.

## Capabilities

### New Capabilities

None. This change adds test and CI infrastructure only; it does not introduce or change library behavior.

### Modified Capabilities

None.

## Impact

- Adds a relational integration-test project to `src/` and includes it in `src/Kkts.sln`.
- Updates CI configuration and developer documentation.
- Adds test-only dependencies on EF Core 10.0.9 and its SQL Server and MySQL providers. `Kkts.Expressions.csproj` remains unchanged.
- Requires Docker-capable local/CI environments for SQL Server 2022 and MySQL 8.4 integration runs.

The selected MySQL provider is Oracle's `MySql.EntityFrameworkCore`, rather than the community Pomelo provider, because the proposed baseline is EF Core 10 and Oracle's provider publishes an EF Core 10-compatible line. The Microsoft SQL Server provider is maintained with EF Core itself. The repository has no existing provider-specific usage to reuse. Provider/version compatibility should be rechecked when implementation begins and pinned consistently; the matrix is a tested configuration, not a promise that every supported expression translates on every provider.
