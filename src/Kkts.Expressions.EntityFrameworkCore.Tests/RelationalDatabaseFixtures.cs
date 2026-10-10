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
            CreateRecord(1, 1, 1, true, true, "alpha", RelationalStatus.Active,
                new DateTime(2024, 1, 1, 0, 0, 0, 123),
                new DateTime(2024, 1, 1, 0, 0, 0, 123), 1),
            CreateRecord(2, 2, null, false, null, "Beta", RelationalStatus.Inactive,
                new DateTime(2024, 1, 2, 0, 0, 0, 250), null, 1),
            CreateRecord(3, 3, 3, true, false, "special %_ marker", RelationalStatus.Active,
                new DateTime(2024, 1, 3, 0, 0, 0, 999),
                new DateTime(2024, 1, 3, 0, 0, 0, 999), 2),
            CreateRecord(4, 4, null, true, true, "O'Brien", RelationalStatus.Archived,
                new DateTime(2024, 1, 4, 0, 0, 0, 1), null, 2),
            CreateRecord(5, 5, 5, false, false, "alpha_suffix", RelationalStatus.Active,
                new DateTime(2024, 1, 5, 0, 0, 0, 500),
                new DateTime(2024, 1, 5, 0, 0, 0, 500), 1));
        await context.SaveChangesAsync();
    }

    private static RelationalRecord CreateRecord(
        int id,
        int integer,
        int? nullableInteger,
        bool enabled,
        bool? nullableEnabled,
        string name,
        RelationalStatus status,
        DateTime createdAt,
        DateTime? optionalCreatedAt,
        int parentId) =>
        new()
        {
            Id = id,
            Integer = integer,
            NullableInteger = nullableInteger,
            SByteValue = (sbyte)integer,
            ByteValue = (byte)integer,
            ShortValue = (short)integer,
            UShortValue = (ushort)integer,
            UIntValue = (uint)integer,
            LongValue = integer,
            ULongValue = (ulong)integer,
            FloatValue = integer,
            DoubleValue = integer,
            DecimalValue = integer,
            NullableDecimalValue = nullableInteger.HasValue ? integer : null,
            Enabled = enabled,
            NullableEnabled = nullableEnabled,
            Name = name,
            Status = status,
            CreatedAt = createdAt,
            OptionalCreatedAt = optionalCreatedAt,
            ParentId = parentId,
            OptionalParentId = id switch
            {
                1 or 5 => 1,
                3 => 2,
                _ => null
            }
        };
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
