# EF Core Relational Provider Test Report

**Run date:** 2026-10-09  
**Result:** Passed for both configured providers  
**Test project:** `src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj`

## Results

| Provider | Database image | Passed | Failed | Skipped | Duration |
|---|---|---:|---:|---:|---:|
| SQL Server | SQL Server 2022, pinned image digest | 2 | 0 | 0 | 920 ms |
| MySQL | MySQL 8.4.4 | 2 | 0 | 0 | 496 ms |
| **Total** | | **4** | **0** | **0** | |

The two tests per provider execute the shared generated-predicate matrix against
the relational database and verify configured string collation and date/time
precision metadata. The predicate matrix also checks explicit expected record
IDs against equivalent handwritten LINQ predicates on the same database.

## Commands

The provider filters were run sequentially without rebuilding after an initial
build. The successful rerun commands were:

```sh
dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj --configuration Release --no-build --filter "Provider=SqlServer"
dotnet test src/Kkts.Expressions.EntityFrameworkCore.Tests/Kkts.Expressions.EntityFrameworkCore.Tests.csproj --configuration Release --no-build --filter "Provider=MySql"
```

Both runs completed with zero failures or skips. They used .NET SDK 10.0.401
and the project's `net10.0` target. Each fixture starts an isolated
Testcontainers database and disposes it after its provider tests.

## Configuration and scope

- EF Core and the SQL Server provider: 10.0.9.
- Oracle `MySql.EntityFrameworkCore`: 10.0.9.
- Testcontainers for .NET: 4.16.0.
- SQL Server: 2022 build 16.0.4255.1,
  `mcr.microsoft.com/mssql/server@sha256:e07b9699a2b749969f19d86563ceeea22bd3a69f7f1db85a8d1ac4bdaf0c6f56`.
- MySQL: 8.4.4 (`mysql:8.4.4`).
- SQL Server uses `Latin1_General_100_BIN2`; MySQL uses `utf8mb4_0900_bin`.
- Date/time columns use millisecond precision: `datetime2(3)` on SQL Server
  and `datetime(3)` on MySQL.

These runs verify the selected shared cases only; they are not a compatibility
guarantee for every expression, provider release, database release, or
configuration. Parser validation tests are separate and are not included in
the provider-filtered counts.

The first parallel invocation also passed both provider filters, but produced
a transient MSBuild file-copy retry because both builds shared project output
files. The sequential reruns above passed without that build contention.
