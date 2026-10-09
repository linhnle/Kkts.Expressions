using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Xunit;

namespace Kkts.Expressions.EntityFrameworkCore.Tests;

public abstract class RelationalDatabaseFixture : IAsyncLifetime
{
    public abstract RelationalTestDbContext CreateContext();

    protected abstract Task StartContainerAsync();
    protected abstract Task DisposeContainerAsync();
    protected virtual Task ConfigureDatabaseAsync(RelationalTestDbContext context) => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        try
        {
            await StartContainerAsync();
            await using var context = CreateContext();
            await ConfigureDatabaseAsync(context);
            await context.Database.EnsureCreatedAsync();
            await SeedAsync(context);
        }
        catch
        {
            await DisposeContainerAsync();
            throw;
        }
    }

    public Task DisposeAsync() => DisposeContainerAsync();

    private static async Task SeedAsync(RelationalTestDbContext context)
    {
        context.Parents.AddRange(
            new RelationalParent { Id = 1, Name = "North" },
            new RelationalParent { Id = 2, Name = "South" });
        context.Records.AddRange(
            new RelationalRecord
            {
                Id = 1,
                Integer = 1,
                NullableInteger = 1,
                Enabled = true,
                NullableEnabled = true,
                Name = "alpha",
                Status = RelationalStatus.Active,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, 123),
                OptionalCreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, 123),
                ParentId = 1
            },
            new RelationalRecord
            {
                Id = 2,
                Integer = 2,
                NullableInteger = null,
                Enabled = false,
                NullableEnabled = null,
                Name = "Beta",
                Status = RelationalStatus.Inactive,
                CreatedAt = new DateTime(2024, 1, 2, 0, 0, 0, 250),
                OptionalCreatedAt = null,
                ParentId = 1
            },
            new RelationalRecord
            {
                Id = 3,
                Integer = 3,
                NullableInteger = 3,
                Enabled = true,
                NullableEnabled = false,
                Name = "special %_ marker",
                Status = RelationalStatus.Active,
                CreatedAt = new DateTime(2024, 1, 3, 0, 0, 0, 999),
                OptionalCreatedAt = new DateTime(2024, 1, 3, 0, 0, 0, 999),
                ParentId = 2
            },
            new RelationalRecord
            {
                Id = 4,
                Integer = 4,
                NullableInteger = null,
                Enabled = true,
                NullableEnabled = true,
                Name = "O'Brien",
                Status = RelationalStatus.Archived,
                CreatedAt = new DateTime(2024, 1, 4, 0, 0, 0, 1),
                OptionalCreatedAt = null,
                ParentId = 2
            },
            new RelationalRecord
            {
                Id = 5,
                Integer = 5,
                NullableInteger = 5,
                Enabled = false,
                NullableEnabled = false,
                Name = "alpha_suffix",
                Status = RelationalStatus.Active,
                CreatedAt = new DateTime(2024, 1, 5, 0, 0, 0, 500),
                OptionalCreatedAt = new DateTime(2024, 1, 5, 0, 0, 0, 500),
                ParentId = 1
            });
        await context.SaveChangesAsync();
    }
}

public sealed class SqlServerFixture : RelationalDatabaseFixture
{
    private const string DatabaseName = "KktsExpressionsTests";
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server@sha256:e07b9699a2b749969f19d86563ceeea22bd3a69f7f1db85a8d1ac4bdaf0c6f56")
        .WithPassword("Kkts!RelationalTests_2026")
        .Build();

    public override RelationalTestDbContext CreateContext()
    {
        var connectionString = $"{_container.GetConnectionString()};Database={DatabaseName}";
        var options = new DbContextOptionsBuilder<SqlServerRelationalTestDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SqlServerRelationalTestDbContext(options);
    }

    protected override Task StartContainerAsync() => _container.StartAsync();
    protected override Task DisposeContainerAsync() => _container.DisposeAsync().AsTask();
}

public sealed class MySqlFixture : RelationalDatabaseFixture
{
    private const string DatabaseName = "kkts_expressions_tests";
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4.4")
        .WithDatabase(DatabaseName)
        .WithUsername("kkts_test")
        .WithPassword("Kkts!RelationalTests_2026")
        .Build();

    public override RelationalTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MySqlRelationalTestDbContext>()
            .UseMySQL(_container.GetConnectionString())
            .Options;
        return new MySqlRelationalTestDbContext(options);
    }

    protected override Task StartContainerAsync() => _container.StartAsync();
    protected override Task DisposeContainerAsync() => _container.DisposeAsync().AsTask();

    protected override Task ConfigureDatabaseAsync(RelationalTestDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            $"ALTER DATABASE `{DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin");
}
