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
        new("Integer in [1]", new[] { 1 }, record => new[] { 1 }.Contains(record.Integer)),
        new("Integer in [1, 3, 5]", new[] { 1, 3, 5 },
            record => new[] { 1, 3, 5 }.Contains(record.Integer)),
        new("Integer in [1, 1, 3]", new[] { 1, 3 },
            record => new[] { 1, 1, 3 }.Contains(record.Integer)),
        new("Integer not in [1]", new[] { 2, 3, 4, 5 },
            record => !new[] { 1 }.Contains(record.Integer)),
        new("Integer not in [1, 3, 5]", new[] { 2, 4 },
            record => !new[] { 1, 3, 5 }.Contains(record.Integer)),
        new("Name in ['special %_ marker']", new[] { 3 },
            record => new[] { "special %_ marker" }.Contains(record.Name)),
        new("Status in ['Active']", new[] { 1, 3, 5 },
            record => new[] { RelationalStatus.Active }.Contains(record.Status)),
        new("Status not in ['Inactive']", new[] { 1, 3, 4, 5 },
            record => !new[] { RelationalStatus.Inactive }.Contains(record.Status)),
        new("NullableInteger in [1]", new[] { 1 },
            record => new int?[] { 1 }.Contains(record.NullableInteger)),
        new("NullableInteger not in [1]", new[] { 2, 3, 4, 5 },
            record => !new int?[] { 1 }.Contains(record.NullableInteger)),
        new("NullableInteger in [null, 1]", new[] { 1, 2, 4 },
            record => new int?[] { null, 1 }.Contains(record.NullableInteger)),
        new("NullableInteger not in [null, 1]", new[] { 3, 5 },
            record => !new int?[] { null, 1 }.Contains(record.NullableInteger)),
        new("Integer in []", Array.Empty<int>(),
            record => Array.Empty<int>().Contains(record.Integer)),
        new("Integer not in []", new[] { 1, 2, 3, 4, 5 },
            record => !Array.Empty<int>().Contains(record.Integer)),
        new("NullableInteger in []", Array.Empty<int>(),
            record => Array.Empty<int?>().Contains(record.NullableInteger)),
        new("NullableInteger not in []", new[] { 1, 2, 3, 4, 5 },
            record => !Array.Empty<int?>().Contains(record.NullableInteger)),
        new("Integer in [1, 2] and Enabled = false", new[] { 2 },
            record => new[] { 1, 2 }.Contains(record.Integer) && !record.Enabled),
        new("Integer not in [1, 2, 3, 4] or Enabled = false", new[] { 2, 5 },
            record => !new[] { 1, 2, 3, 4 }.Contains(record.Integer) || !record.Enabled),
        new("not (Integer in [1, 2])", new[] { 3, 4, 5 },
            record => !new[] { 1, 2 }.Contains(record.Integer)),
        new("(Integer in [1, 2] or Integer in [4, 5]) and Enabled = false", new[] { 2, 5 },
            record => (new[] { 1, 2 }.Contains(record.Integer)
                || new[] { 4, 5 }.Contains(record.Integer)) && !record.Enabled),
        new("Integer + 1 = 3", new[] { 2 }, record => record.Integer + 1 == 3),
        new("Integer - 1 = 2", new[] { 3 }, record => record.Integer - 1 == 2),
        new("Integer + ParentId = 5", new[] { 3 }, record => record.Integer + record.ParentId == 5),
        new("Integer - ParentId = 1", new[] { 2, 3 },
            record => record.Integer - record.ParentId == 1),
        new("Integer + 2 - 1 = 3", new[] { 2 },
            record => record.Integer + 2 - 1 == 3),
        new("Integer - (ParentId - 1) = 3", new[] { 4 },
            record => record.Integer - (record.ParentId - 1) == 3),
        new("Integer + -1 = 0", new[] { 1 }, record => record.Integer + -1 == 0),
        new("Integer + 1 - ParentId = 2", new[] { 2, 3 },
            record => record.Integer + 1 - record.ParentId == 2),
        new("NullableInteger + 1 = null", new[] { 2, 4 },
            record => record.NullableInteger + 1 == null),
        new("NullableInteger - 1 = 2", new[] { 3 },
            record => record.NullableInteger - 1 == 2),
        new("SByteValue + 1 = 3", new[] { 2 },
            record => record.SByteValue + 1 == 3),
        new("ByteValue + 1 = 3", new[] { 2 },
            record => record.ByteValue + 1 == 3),
        new("ShortValue + 1 = 3", new[] { 2 },
            record => record.ShortValue + 1 == 3),
        new("UShortValue + 1 = 3", new[] { 2 },
            record => record.UShortValue + 1 == 3),
        new("UIntValue + 1 = 3", new[] { 2 },
            record => record.UIntValue + 1 == 3),
        new("LongValue + 1 = 3", new[] { 2 },
            record => record.LongValue + 1 == 3),
        new("ULongValue + 1 = 3", new[] { 2 },
            record => record.ULongValue + 1 == 3),
        new("FloatValue + Integer = 6", new[] { 3 },
            record => record.FloatValue + record.Integer == 6),
        new("DoubleValue + Integer = 6", new[] { 3 },
            record => record.DoubleValue + record.Integer == 6),
        new("DecimalValue + Integer = 6", new[] { 3 },
            record => record.DecimalValue + record.Integer == 6),
        new("NullableDecimalValue + Integer = null", new[] { 2, 4 },
            record => record.NullableDecimalValue + record.Integer == null),
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

    public static async Task AssertNestedFilterTreeAsync(
        Func<RelationalTestDbContext> createContext,
        string provider)
    {
        var schema = ExpressionSchema.FromType<RelationalRecord>(
            propertyMapping: new Dictionary<string, string> { ["state"] = "Status" });
        var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());
        var tree = FilterNode.And(new[]
        {
            FilterNode.Or(new[]
            {
                FilterNode.Condition("state", "==", FilterValue.String("Active")),
                FilterNode.Condition("Integer", ">=", FilterValue.Variable("minimum"))
            }),
            FilterNode.Not(FilterNode.Condition(
                "NullableInteger",
                "in",
                FilterValue.Collection(new[]
                {
                    FilterValue.Null,
                    FilterValue.Number("1")
                })))
        });
        var variables = new VariableResolver();
        Assert.True(variables.TryAdd("minimum", 4));
        var built = queryContext.TryBuildPredicate<RelationalRecord>(tree, variables);
        Assert.True(built.Succeeded, $"{provider} failed to build the nested tree: {built.Exception}");

        await using var context = createContext();
        var generatedIds = await context.Records
            .AsNoTracking()
            .Where(built.Result)
            .OrderBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenIds = await context.Records
            .AsNoTracking()
            .Where(record =>
                (record.Status == RelationalStatus.Active || record.Integer >= 4) &&
                !new int?[] { null, 1 }.Contains(record.NullableInteger))
            .OrderBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();

        Assert.Equal(new[] { 3, 5 }, generatedIds);
        Assert.Equal(handwrittenIds, generatedIds);
    }

    public static async Task AssertExpressionMappedFieldsAsync(
        Func<RelationalTestDbContext> createContext,
        string provider)
    {
        var schema = new QuerySchema<RelationalRecord>()
            .Field("customerName", record => record.Parent.Name)
            .Field("total", record => record.DecimalValue * record.Integer)
            .Field("optionalTotal", record => record.NullableDecimalValue * record.Integer)
            .Field(
                "optionalParentName",
                record => record.OptionalParent == null ? null : record.OptionalParent.Name,
                nullability: ExpressionNullability.Nullable)
            .Field("createdAt", record => record.CreatedAt)
            .Field("id", record => record.Id)
            .Build();
        var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());

        var customer = queryContext.ParsePredicate<RelationalRecord>("customerName = 'North'");
        var total = queryContext.ParsePredicate<RelationalRecord>("total > 8");
        var optionalNull = queryContext.ParsePredicate<RelationalRecord>("optionalTotal = null");
        var optionalTotal = queryContext.TryBuildPredicate<RelationalRecord>(
            "optionalTotal",
            ComparisonOperator.GreaterThan,
            8m);
        var structuredTotal = queryContext.TryBuildPredicate<RelationalRecord>(new Filter
        {
            Property = "total",
            Operator = ">",
            Value = "8"
        });
        var treeOptionalTotal = queryContext.TryBuildPredicate<RelationalRecord>(
            FilterNode.Condition("optionalTotal", ">", FilterValue.Number("8")));
        var nullableMembership = queryContext.TryBuildPredicate<RelationalRecord>(
            FilterNode.Condition(
                "optionalTotal",
                "in",
                FilterValue.Collection(new[]
                {
                    FilterValue.Null,
                    FilterValue.Number("9")
                })));
        var optionalNavigation = queryContext.ParsePredicate<RelationalRecord>(
            "optionalParentName = null");

        Assert.True(customer.Succeeded, $"{provider}: {customer.Exception}");
        Assert.True(total.Succeeded, $"{provider}: {total.Exception}");
        Assert.True(optionalNull.Succeeded, $"{provider}: {optionalNull.Exception}");
        Assert.True(optionalTotal.Succeeded, $"{provider}: {optionalTotal.Exception}");
        Assert.True(structuredTotal.Succeeded, $"{provider}: {structuredTotal.Exception}");
        Assert.True(treeOptionalTotal.Succeeded, $"{provider}: {treeOptionalTotal.Exception}");
        Assert.True(nullableMembership.Succeeded, $"{provider}: {nullableMembership.Exception}");
        Assert.True(optionalNavigation.Succeeded, $"{provider}: {optionalNavigation.Exception}");

        await using var context = createContext();
        await AssertMappedPredicateAsync(
            context,
            customer.Result,
            record => record.Parent.Name == "North",
            new[] { 1, 2, 5 },
            provider,
            "customerName");
        await AssertMappedPredicateAsync(
            context,
            total.Result,
            record => record.DecimalValue * record.Integer > 8,
            new[] { 3, 4, 5 },
            provider,
            "total");
        await AssertMappedPredicateAsync(
            context,
            optionalNull.Result,
            record => record.NullableDecimalValue * record.Integer == null,
            new[] { 2, 4 },
            provider,
            "optionalTotal null");
        await AssertMappedPredicateAsync(
            context,
            optionalTotal.Result,
            record => record.NullableDecimalValue * record.Integer > 8,
            new[] { 3, 5 },
            provider,
            "optionalTotal");
        await AssertMappedPredicateAsync(
            context,
            structuredTotal.Result,
            record => record.DecimalValue * record.Integer > 8,
            new[] { 3, 4, 5 },
            provider,
            "structured total");
        await AssertMappedPredicateAsync(
            context,
            treeOptionalTotal.Result,
            record => record.NullableDecimalValue * record.Integer > 8,
            new[] { 3, 5 },
            provider,
            "tree optionalTotal");
        await AssertMappedPredicateAsync(
            context,
            nullableMembership.Result,
            record => new decimal?[] { null, 9m }
                .Contains(record.NullableDecimalValue * record.Integer),
            new[] { 2, 3, 4 },
            provider,
            "nullable optionalTotal membership");
        await AssertMappedPredicateAsync(
            context,
            optionalNavigation.Result,
            record => record.OptionalParent == null,
            new[] { 2, 4 },
            provider,
            "optional navigation null");

        var totalOrder = queryContext.TryBuildOrderByClause("total desc, id asc");
        var dateOrder = queryContext.TryBuildOrderByClause("createdAt desc, id asc");
        var customerOrder = queryContext.TryBuildOrderByClause("customerName asc, id asc");
        var optionalNameOrder = queryContext.TryBuildOrderByClause(
            "optionalParentName asc, id asc");
        Assert.True(totalOrder.Succeeded, $"{provider}: {totalOrder.Exception}");
        Assert.True(dateOrder.Succeeded, $"{provider}: {dateOrder.Exception}");
        Assert.True(customerOrder.Succeeded, $"{provider}: {customerOrder.Exception}");
        Assert.True(optionalNameOrder.Succeeded, $"{provider}: {optionalNameOrder.Exception}");

        var generatedTotalOrder = await totalOrder.Result.Sort(context.Records.AsNoTracking())
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenTotalOrder = await context.Records.AsNoTracking()
            .OrderByDescending(record => record.DecimalValue * record.Integer)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, generatedTotalOrder);
        Assert.Equal(handwrittenTotalOrder, generatedTotalOrder);

        var generatedDateOrder = await dateOrder.Result.Sort(context.Records.AsNoTracking())
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenDateOrder = await context.Records.AsNoTracking()
            .OrderByDescending(record => record.CreatedAt)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, generatedDateOrder);
        Assert.Equal(handwrittenDateOrder, generatedDateOrder);

        var generatedCustomerOrder = await customerOrder.Result.Sort(context.Records.AsNoTracking())
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenCustomerOrder = await context.Records.AsNoTracking()
            .OrderBy(record => record.Parent.Name)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.Equal(new[] { 1, 2, 5, 3, 4 }, generatedCustomerOrder);
        Assert.Equal(handwrittenCustomerOrder, generatedCustomerOrder);

        var generatedOptionalNameOrder = await optionalNameOrder.Result
            .Sort(context.Records.AsNoTracking())
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenOptionalNameOrder = await context.Records.AsNoTracking()
            .OrderBy(record =>
                record.OptionalParent == null ? null : record.OptionalParent.Name)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.Equal(new[] { 2, 4, 1, 5, 3 }, generatedOptionalNameOrder);
        Assert.Equal(handwrittenOptionalNameOrder, generatedOptionalNameOrder);

        var condition = queryContext.BuildCondition<RelationalRecord>(new ConditionOptions
        {
            Where = "total > 8",
            OrderBy = "total desc, id asc"
        });
        Assert.True(condition.IsValid, $"{provider}: {condition.Error?.EvaluationResult?.Exception}");
        IQueryable<RelationalRecord> filtered = context.Records.AsNoTracking();
        foreach (var predicate in condition.Predicates)
            filtered = filtered.Where(predicate);
        var generatedConditionIds = await condition.OrderByClause.Sort(filtered)
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenConditionIds = await context.Records.AsNoTracking()
            .Where(record => record.DecimalValue * record.Integer > 8)
            .OrderByDescending(record => record.DecimalValue * record.Integer)
            .ThenBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.Equal(new[] { 5, 4, 3 }, generatedConditionIds);
        Assert.Equal(handwrittenConditionIds, generatedConditionIds);

        var filterOnlyContext = new ExpressionQueryContext(
            new QuerySchema<RelationalRecord>()
                .Field("total", record => record.DecimalValue * record.Integer, canSort: false)
                .Build(),
            new QueryPolicy());
        var deniedSort = filterOnlyContext.TryBuildOrderByClause("total desc");
        Assert.False(deniedSort.Succeeded);
        Assert.Equal("property-not-queryable", Assert.Single(deniedSort.Diagnostics).Code);
    }

    public static async Task AssertNonTranslatableSelectorAsync(
        Func<RelationalTestDbContext> createContext,
        string provider)
    {
        var execution = new SelectorExecutionCounter();
        var schema = new QuerySchema<RelationalRecord>()
            .Field(
                "businessValue",
                record => CalculateBusinessValue(record.DecimalValue, execution))
            .Build();
        var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());
        Assert.True(queryContext.AnalyzeExpression("businessValue > 1").IsSemanticallyValid);
        var predicate = queryContext.ParsePredicate<RelationalRecord>("businessValue > 1");
        Assert.True(predicate.Succeeded, $"{provider}: {predicate.Exception}");
        Assert.Equal(0, execution.Calls);

        var orderBy = queryContext.TryBuildOrderByClause("businessValue desc");
        Assert.True(orderBy.Succeeded, $"{provider}: {orderBy.Exception}");
        await using var context = createContext();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Records.AsNoTracking()
                .Where(predicate.Result)
                .Select(record => record.Id)
                .ToArrayAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await orderBy.Result.Sort(context.Records.AsNoTracking())
                .Select(record => record.Id)
                .ToArrayAsync());
        Assert.Equal(0, execution.Calls);
    }

    private static decimal CalculateBusinessValue(decimal value, SelectorExecutionCounter execution)
    {
        execution.Increment();
        return value + 1m;
    }

    private sealed class SelectorExecutionCounter
    {
        private int _calls;

        public int Calls => _calls;

        public void Increment() => System.Threading.Interlocked.Increment(ref _calls);
    }

    private static async Task AssertMappedPredicateAsync(
        RelationalTestDbContext context,
        Expression<Func<RelationalRecord, bool>> generated,
        Expression<Func<RelationalRecord, bool>> handwritten,
        int[] expected,
        string provider,
        string fieldName)
    {
        var generatedIds = await context.Records.AsNoTracking()
            .Where(generated)
            .OrderBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        var handwrittenIds = await context.Records.AsNoTracking()
            .Where(handwritten)
            .OrderBy(record => record.Id)
            .Select(record => record.Id)
            .ToArrayAsync();
        Assert.True(expected.SequenceEqual(generatedIds),
            $"{provider} mapped field '{fieldName}' returned [{string.Join(", ", generatedIds)}], expected [{string.Join(", ", expected)}].");
        Assert.Equal(handwrittenIds, generatedIds);
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
    public Task ExecutesNestedFilterTreeOnSqlServer() =>
        RelationalPredicateAssertions.AssertNestedFilterTreeAsync(fixture.CreateContext, "SQL Server");

    [Fact]
    public Task ExecutesExpressionMappedFieldsOnSqlServer() =>
        RelationalPredicateAssertions.AssertExpressionMappedFieldsAsync(
            fixture.CreateContext,
            "SQL Server");

    [Fact]
    public Task DistinguishesValidSelectorsFromSqlTranslationOnSqlServer() =>
        RelationalPredicateAssertions.AssertNonTranslatableSelectorAsync(
            fixture.CreateContext,
            "SQL Server");

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
    public Task ExecutesNestedFilterTreeOnMySql() =>
        RelationalPredicateAssertions.AssertNestedFilterTreeAsync(fixture.CreateContext, "MySQL");

    [Fact]
    public Task ExecutesExpressionMappedFieldsOnMySql() =>
        RelationalPredicateAssertions.AssertExpressionMappedFieldsAsync(
            fixture.CreateContext,
            "MySQL");

    [Fact]
    public Task DistinguishesValidSelectorsFromSqlTranslationOnMySql() =>
        RelationalPredicateAssertions.AssertNonTranslatableSelectorAsync(
            fixture.CreateContext,
            "MySQL");

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
