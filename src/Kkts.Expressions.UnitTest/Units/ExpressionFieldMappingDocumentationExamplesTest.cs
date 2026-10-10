using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionFieldMappingDocumentationExamplesTest
    {
        [Fact]
        public void QuickStartExample_UsesTypedSelectorsAndPublicMetadata()
        {
            var schema = new QuerySchema<DocumentationOrder>()
                .Field(
                    "customerName",
                    order => order.Customer == null ? null : order.Customer.Name,
                    displayName: "Customer",
                    description: "The customer's public name",
                    nullability: ExpressionNullability.Nullable)
                .Field(
                    "total",
                    order => order.UnitPrice * order.Quantity,
                    canSort: false)
                .Field("createdAt", order => order.CreatedAt)
                .Build();

            var queryContext = new ExpressionQueryContext(schema, new QueryPolicy());

            Assert.True(schema.IsPublicSchema);
            Assert.Equal(new[] { "customerName", "total", "createdAt" },
                schema.Fields.Select(field => field.Name));
            Assert.Equal(typeof(decimal), schema.Fields[1].ClrType);
            Assert.False(schema.Fields[1].CanSort);
            Assert.Equal("Customer", schema.Fields[0].DisplayName);
            Assert.NotNull(queryContext);
        }

        [Fact]
        public void ReadmeQuickStartExample_UsesOnlyRegisteredPublicFields()
        {
            var publicSchema = new QuerySchema<ReadmeData>()
                .Field("displayName", item => item.Name)
                .Field("enabled", item => item.IsEnabled)
                .Field("createdAt", item => item.CreationDate, canFilter: false)
                .Build();
            var publicContext = new ExpressionQueryContext(publicSchema, new QueryPolicy());
            var predicate = publicContext.ParsePredicate<ReadmeData>(
                "displayName = 'Test' and enabled = true");
            var results = new[]
            {
                new ReadmeData { Id = 1, Name = "Test", IsEnabled = true },
                new ReadmeData { Id = 2, Name = "Test", IsEnabled = false }
            }.AsQueryable().Where(predicate.Result).Select(item => item.Id).ToArray();

            Assert.True(predicate.Succeeded, predicate.Exception?.ToString());
            Assert.Equal(new[] { 1 }, results);
        }

        [Fact]
        public void QuerySurfaceGuideExample_UsesSharedTypedFieldResolution()
        {
            var queryContext = new ExpressionQueryContext(
                new QuerySchema<DocumentationOrder>()
                    .Field("customerName",
                        order => order.Customer == null ? null : order.Customer.Name)
                    .Field("total", order => order.UnitPrice * order.Quantity)
                    .Field("createdAt", order => order.CreatedAt)
                    .Build(),
                new QueryPolicy());
            var orders = new[]
            {
                new DocumentationOrder
                {
                    Id = 1,
                    Customer = new DocumentationCustomer { Name = "North" },
                    UnitPrice = 10m,
                    Quantity = 1,
                    CreatedAt = new DateTime(2024, 1, 1)
                },
                new DocumentationOrder
                {
                    Id = 2,
                    Customer = new DocumentationCustomer { Name = "South" },
                    UnitPrice = 5m,
                    Quantity = 3,
                    CreatedAt = new DateTime(2024, 1, 2)
                },
                new DocumentationOrder
                {
                    Id = 3,
                    Customer = new DocumentationCustomer { Name = "North" },
                    UnitPrice = 2m,
                    Quantity = 4,
                    CreatedAt = new DateTime(2024, 1, 3)
                }
            };

            var textPredicate = queryContext.ParsePredicate<DocumentationOrder>("total > 9");
            var directPredicate = queryContext.TryBuildPredicate<DocumentationOrder>(
                "total",
                ComparisonOperator.GreaterThan,
                10m);
            var filtersPredicate = queryContext.TryBuildPredicate<DocumentationOrder>(new[]
            {
                new Filter { Property = "total", Operator = ">", Value = "9" },
                new Filter { Property = "customerName", Operator = "contains", Value = "North" }
            });
            var groupPredicate = queryContext.TryBuildPredicate<DocumentationOrder>(new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter { Property = "total", Operator = ">", Value = "9" }
                }
            });
            var treePredicate = queryContext.TryBuildPredicate<DocumentationOrder>(FilterNode.And(new[]
            {
                FilterNode.Condition("total", ">", FilterValue.Number("9")),
                FilterNode.Condition("customerName", "contains", FilterValue.String("North"))
            }));
            var condition = queryContext.BuildCondition<DocumentationOrder>(new ConditionOptions
            {
                Where = "total > 9",
                OrderBy = "createdAt desc"
            });

            Assert.True(textPredicate.Succeeded, textPredicate.Exception?.ToString());
            Assert.True(directPredicate.Succeeded, directPredicate.Exception?.ToString());
            Assert.True(filtersPredicate.Succeeded, filtersPredicate.Exception?.ToString());
            Assert.True(groupPredicate.Succeeded, groupPredicate.Exception?.ToString());
            Assert.True(treePredicate.Succeeded, treePredicate.Exception?.ToString());
            Assert.True(condition.IsValid, condition.Error?.EvaluationResult?.Exception?.ToString());
            Assert.Equal(new[] { 1, 2 }, MatchIds(orders, textPredicate.Result));
            Assert.Equal(new[] { 2 }, MatchIds(orders, directPredicate.Result));
            Assert.Equal(new[] { 1 }, MatchIds(orders, filtersPredicate.Result));
            Assert.Equal(new[] { 1, 2 }, MatchIds(orders, groupPredicate.Result));
            Assert.Equal(new[] { 1 }, MatchIds(orders, treePredicate.Result));
            Assert.Equal(new[] { 2, 1 }, condition.OrderByClause
                .Sort(orders.Where(order =>
                    condition.Predicates.All(predicate => predicate.Compile()(order))))
                .Select(order => order.Id));
        }

        private static int[] MatchIds(
            IEnumerable<DocumentationOrder> orders,
            System.Linq.Expressions.Expression<Func<DocumentationOrder, bool>> predicate)
        {
            var compiled = predicate.Compile();
            return orders.Where(compiled).Select(order => order.Id).ToArray();
        }

        private sealed class DocumentationOrder
        {
            public int Id { get; set; }
            public DocumentationCustomer Customer { get; set; }
            public decimal UnitPrice { get; set; }
            public int Quantity { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private sealed class DocumentationCustomer
        {
            public string Name { get; set; }
        }

        private sealed class ReadmeData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; }
            public DateTime CreationDate { get; set; }
        }
    }
}
