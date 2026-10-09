using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kkts.Expressions.EntityFrameworkCore.Tests;

internal sealed record PredicateCase(
    string Text,
    int[] ExpectedIds,
    Expression<Func<RelationalRecord, bool>> Handwritten);

internal static class RelationalPredicateAssertions
{
    private static readonly PredicateCase[] SharedCases =
    {
        new("Integer = 1", new[] { 1 }, record => record.Integer == 1),
        new("Integer != 1", new[] { 2, 3, 4, 5 }, record => record.Integer != 1),
        new("Integer < 3", new[] { 1, 2 }, record => record.Integer < 3),
        new("Integer <= 3", new[] { 1, 2, 3 }, record => record.Integer <= 3),
        new("Integer > 3", new[] { 4, 5 }, record => record.Integer > 3),
        new("Integer >= 3", new[] { 3, 4, 5 }, record => record.Integer >= 3),
        new("Integer >= 2 and Enabled = true", new[] { 3, 4 },
            record => record.Integer >= 2 && record.Enabled),
        new("Integer = 1 or Integer = 5", new[] { 1, 5 },
            record => record.Integer == 1 || record.Integer == 5),
        new("not (Integer = 3 or Integer = 4)", new[] { 1, 2, 5 },
            record => !(record.Integer == 3 || record.Integer == 4)),
        new("Integer = 1 or Integer = 2 and Enabled = false", new[] { 1, 2 },
            record => record.Integer == 1 || (record.Integer == 2 && !record.Enabled)),
        new("(Integer = 1 or Integer = 2) and Enabled = false", new[] { 2 },
            record => (record.Integer == 1 || record.Integer == 2) && !record.Enabled),
        new("NullableInteger = null", new[] { 2, 4 },
            record => record.NullableInteger == null),
        new("NullableInteger != null", new[] { 1, 3, 5 },
            record => record.NullableInteger != null),
        new("NullableInteger > 2", new[] { 3, 5 },
            record => record.NullableInteger > 2),
        new("NullableEnabled = null", new[] { 2 },
            record => record.NullableEnabled == null),
        new("NullableEnabled = false", new[] { 3, 5 },
            record => record.NullableEnabled == false),
        new("Name = 'alpha'", new[] { 1 }, record => record.Name == "alpha"),
        new("Name.contains('%_')", new[] { 3 }, record => record.Name.Contains("%_")),
        new("Name.startsWith('alpha')", new[] { 1, 5 },
            record => record.Name.StartsWith("alpha")),
        new("Name.endsWith('_suffix')", new[] { 5 },
            record => record.Name.EndsWith("_suffix")),
        new("Name = 'ALPHA'", Array.Empty<int>(), record => record.Name == "ALPHA"),
        new("Status = 'Active'", new[] { 1, 3, 5 },
            record => record.Status == RelationalStatus.Active),
        new("Status != 'Active'", new[] { 2, 4 },
            record => record.Status != RelationalStatus.Active),
        new("CreatedAt >= '2024-01-03' and CreatedAt < '2024-01-05'",
            new[] { 3, 4 },
            record => record.CreatedAt >= new DateTime(2024, 1, 3)
                && record.CreatedAt < new DateTime(2024, 1, 5)),
        new("CreatedAt = '2024-01-03 00:00:00.999'", new[] { 3 },
            record => record.CreatedAt == new DateTime(2024, 1, 3, 0, 0, 0, 999)),
        new("OptionalCreatedAt = null", new[] { 2, 4 },
            record => record.OptionalCreatedAt == null),
        new("Parent.Name = 'North'", new[] { 1, 2, 5 },
            record => record.Parent.Name == "North")
    };

    public static async Task AssertSharedCasesAsync(
        Func<RelationalTestDbContext> createContext,
        string provider)
    {
        foreach (var testCase in SharedCases)
        {
            var parsed = Interpreter.ParsePredicate<RelationalRecord>(testCase.Text);
            Assert.True(parsed.Succeeded,
                $"{provider} parser rejected '{testCase.Text}': {parsed.Exception}");

            await using var context = createContext();
            var generatedIds = await context.Records
                .AsNoTracking()
                .Where(parsed.Result)
                .OrderBy(record => record.Id)
                .Select(record => record.Id)
                .ToArrayAsync();
            var handwrittenIds = await context.Records
                .AsNoTracking()
                .Where(testCase.Handwritten)
                .OrderBy(record => record.Id)
                .Select(record => record.Id)
                .ToArrayAsync();

            Assert.True(testCase.ExpectedIds.SequenceEqual(generatedIds),
                $"{provider} generated predicate '{testCase.Text}' returned [{string.Join(", ", generatedIds)}], expected [{string.Join(", ", testCase.ExpectedIds)}].");
            Assert.True(testCase.ExpectedIds.SequenceEqual(handwrittenIds),
                $"{provider} handwritten predicate for '{testCase.Text}' returned [{string.Join(", ", handwrittenIds)}], expected [{string.Join(", ", testCase.ExpectedIds)}].");
            Assert.Equal(handwrittenIds, generatedIds);
        }
    }
}

[Trait("Provider", "SqlServer")]
public sealed class SqlServerPredicateIntegrationTests(SqlServerFixture fixture)
    : IClassFixture<SqlServerFixture>
{
    [Fact]
    public Task ExecutesSharedPredicatesOnSqlServer() =>
        RelationalPredicateAssertions.AssertSharedCasesAsync(fixture.CreateContext, "SQL Server");

    [Fact]
    public async Task UsesConfiguredCollationAndDatePrecision()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();

        await using var collationCommand = connection.CreateCommand();
        collationCommand.CommandText = """
            SELECT COLLATION_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Records'
              AND COLUMN_NAME = 'Name'
            """;
        Assert.Equal("Latin1_General_100_BIN2", await collationCommand.ExecuteScalarAsync());

        await using var precisionCommand = connection.CreateCommand();
        precisionCommand.CommandText = """
            SELECT DATETIME_PRECISION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Records'
              AND COLUMN_NAME = 'CreatedAt'
            """;
        Assert.Equal(3, Convert.ToInt32(await precisionCommand.ExecuteScalarAsync()));
    }
}

[Trait("Provider", "MySql")]
public sealed class MySqlPredicateIntegrationTests(MySqlFixture fixture)
    : IClassFixture<MySqlFixture>
{
    [Fact]
    public Task ExecutesSharedPredicatesOnMySql() =>
        RelationalPredicateAssertions.AssertSharedCasesAsync(fixture.CreateContext, "MySQL");

    [Fact]
    public async Task UsesConfiguredCollationAndDatePrecision()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();

        await using var collationCommand = connection.CreateCommand();
        collationCommand.CommandText = """
            SELECT COLLATION_NAME
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'Records'
              AND COLUMN_NAME = 'Name'
            """;
        Assert.Equal("utf8mb4_0900_bin", await collationCommand.ExecuteScalarAsync());

        await using var precisionCommand = connection.CreateCommand();
        precisionCommand.CommandText = """
            SELECT DATETIME_PRECISION
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'Records'
              AND COLUMN_NAME = 'CreatedAt'
            """;
        Assert.Equal(3, Convert.ToInt32(await precisionCommand.ExecuteScalarAsync()));
    }
}

public sealed class ParserValidationTests
{
    [Theory]
    [InlineData("Integer = 'not-an-integer'")]
    [InlineData("Integer")]
    public void InvalidPredicateFailsBeforeDatabaseExecution(string text)
    {
        var parsed = Interpreter.ParsePredicate<RelationalRecord>(text);

        Assert.False(parsed.Succeeded);
        Assert.NotNull(parsed.Exception);
        Assert.Null(parsed.Result);
    }
}
