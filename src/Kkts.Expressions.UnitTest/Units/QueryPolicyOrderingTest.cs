using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyOrderingTest
    {
        [Fact]
        public void Ordering_UsesFieldPermissionsButNotComparisonOperatorPermissions()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(
                    propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" }),
                new QueryPolicy(
                    maxExpressionLength: 100,
                    maxNavigationDepth: 0,
                    allowCollectionAccess: false,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new ComparisonOperator[0]
                    }));

            var allowed = context.TryBuildOrderByClause("cost desc");
            var denied = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "NotQueryable" }
            });

            Assert.True(allowed.Succeeded);
            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal("OrderBys[1].Property", denied.Diagnostics[0].InputPath);
        }

        [Fact]
        public void Ordering_RejectsLengthOverrunAndReturnsNoSortedSource()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 2));
            var source = new[] { new OrderingEntity { Id = 2 }, new OrderingEntity { Id = 1 } }.AsQueryable();

            var result = context.TryOrderBy(source, "Id ");

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void StructuredOrderingChecksCumulativePropertyLengthAndSupportsEnumerableSources()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 4));
            var overLimit = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "Id" }
            });

            Assert.False(overLimit.Succeeded);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(overLimit.Diagnostics).Code);
            Assert.Equal("OrderBys[2].Property", overLimit.Diagnostics[0].InputPath);

            var allowed = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 4));
            var sorted = allowed.TryOrderBy(
                new[] { new OrderingEntity { Id = 2 }, new OrderingEntity { Id = 1 } },
                new[] { new OrderByInfo { Property = "Id" } });

            Assert.True(sorted.Succeeded);
            Assert.Equal(new[] { 1, 2 }, sorted.Result.Select(entity => entity.Id));
        }

        [Fact]
        public void Ordering_EnforcesNavigationAndCollectionRestrictionsThroughAliases()
        {
            var schema = ExpressionSchema.FromType<ComplexOrderingEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["city"] = "Customer.Name",
                    ["itemCount"] = "Items.Count"
                });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(
                    maxNavigationDepth: 0,
                    allowCollectionAccess: false));

            var navigation = context.TryBuildOrderByClause(
                new OrderByInfo { Property = "city" });
            var collectionContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowCollectionAccess: false));
            var collection = collectionContext.TryBuildOrderByClause(
                new OrderByInfo { Property = "itemCount" });

            Assert.False(navigation.Succeeded);
            Assert.Equal("query-policy-navigation-depth-exceeded", Assert.Single(navigation.Diagnostics).Code);
            Assert.Equal("OrderBys[0].Property", navigation.Diagnostics[0].InputPath);
            Assert.False(collection.Succeeded);
            Assert.Equal("query-policy-collection-access-denied", Assert.Single(collection.Diagnostics).Code);
        }

        private sealed class OrderingEntity
        {
            public int Id { get; set; }

            public decimal Price { get; set; }
        }

        private sealed class ComplexOrderingEntity
        {
            public Customer Customer { get; set; }

            public List<Item> Items { get; set; }
        }

        private sealed class Customer
        {
            public string Name { get; set; }
        }

        private sealed class Item
        {
            public int Id { get; set; }
        }
    }
}
