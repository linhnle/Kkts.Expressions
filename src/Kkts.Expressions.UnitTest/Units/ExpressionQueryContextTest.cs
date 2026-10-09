using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionQueryContextTest
    {
        [Fact]
        public void Constructor_RequiresSchemaAndPolicy()
        {
            var policy = new QueryPolicy();
            Assert.Throws<ArgumentNullException>(() => new ExpressionQueryContext(null, policy));
            Assert.Throws<ArgumentNullException>(() => new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(), null));
        }

        [Fact]
        public void AnalyzeExpression_RejectsOverLimitBeforeTokenization()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxExpressionLength: 8));

            var result = context.AnalyzeExpression("Price = 1 ");

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal(8, diagnostic.Start);
            Assert.Equal(2, diagnostic.Length);
            Assert.Equal(8, diagnostic.ConfiguredLimit);
            Assert.Equal(10, diagnostic.ObservedValue);
            Assert.Empty(result.Tokens);
            Assert.True(result.IsTruncated);
            Assert.False(result.IsSemanticallyValid);
        }

        [Fact]
        public void AnalyzeExpression_RejectsExcessNestingBeforeTokenization()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy(maxParenthesisDepth: 1));

            var result = context.AnalyzeExpression("((Price = 1");

            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("query-policy-parenthesis-depth-exceeded", diagnostic.Code);
            Assert.Equal(1, diagnostic.ConfiguredLimit);
            Assert.Equal(2, diagnostic.ObservedValue);
            Assert.Empty(result.Tokens);
            Assert.True(result.IsTruncated);
        }

        [Fact]
        public void Constructor_ValidatesOperatorFieldsAgainstSchema()
        {
            var schema = ExpressionSchema.FromType<QueryEntity>();
            var policy = new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Missing"] = new[] { ComparisonOperator.Equal }
                });

            var exception = Assert.Throws<ArgumentException>(() =>
                new ExpressionQueryContext(schema, policy));
            Assert.Contains("unknown entity property path", exception.Message);
        }

        [Fact]
        public void Constructor_IntersectsOperatorRulesForAliasesOfSameMember()
        {
            var schema = ExpressionSchema.FromType<QueryEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["cost"] = "Price"
                });
            var policy = new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["cost"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
                    ["Price"] = new[] { ComparisonOperator.Equal, ComparisonOperator.Contains }
                });

            var context = new ExpressionQueryContext(schema, policy);

            Assert.True(context.TryGetAllowedOperators("cost", out var aliasOperators));
            Assert.True(context.TryGetAllowedOperators("Price", out var canonicalOperators));
            Assert.Equal(new[] { ComparisonOperator.Equal }, aliasOperators);
            Assert.Equal(aliasOperators, canonicalOperators);
        }

        [Fact]
        public void AdditionalAllowlistIntersectsWithSchemaAndIsSnapshotted()
        {
            var schema = ExpressionSchema.FromType<QueryEntity>(
                validProperties: new[] { "Price", "Secret", "cost" },
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" },
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Secret", typeof(decimal), canQuery: false)
                });
            var allowed = new[] { "cost", "Secret" };
            var context = new ExpressionQueryContext(schema, new QueryPolicy(), allowed);
            allowed[0] = "Name";

            Assert.True(context.IsPropertyQueryable("cost"));
            Assert.False(context.IsPropertyQueryable("Price"));
            Assert.False(context.IsPropertyQueryable("Secret"));
            Assert.Equal(new[] { "cost", "Secret" }, context.ValidProperties);
        }

        [Fact]
        public void Constructor_RejectsInvalidAdditionalAllowlist()
        {
            var schema = ExpressionSchema.FromType<QueryEntity>();
            Assert.Throws<ArgumentException>(() =>
                new ExpressionQueryContext(schema, new QueryPolicy(), new[] { "Price", "price" }));
            Assert.Throws<ArgumentException>(() =>
                new ExpressionQueryContext(schema, new QueryPolicy(), new[] { " " }));
        }

        [Fact]
        public void ValidateEntityType_RejectsSchemaTypeMismatch()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<QueryEntity>(),
                new QueryPolicy());

            Assert.Throws<ArgumentNullException>(() => context.ValidateEntityType(null));
            Assert.Throws<ArgumentException>(() => context.ValidateEntityType(typeof(OtherEntity)));
            context.ValidateEntityType(typeof(QueryEntity));
        }

        [Fact]
        public void SchemaPolicyPathInfo_CanonicalizesAliasesAndCountsOnlyEntityNavigation()
        {
            var schema = ExpressionSchema.FromType<PathEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["city"] = "Customer.Address.City"
                });

            Assert.True(schema.TryGetPolicyPathInfo("city", out var cityPath, out var cityDepth, out var cityCollection));
            Assert.Equal("Customer.Address.City", cityPath);
            Assert.Equal(2, cityDepth);
            Assert.False(cityCollection);

            Assert.True(schema.TryGetPolicyPathInfo("CreatedAt.Year", out _, out var scalarDepth, out _));
            Assert.Equal(0, scalarDepth);

            Assert.True(schema.TryGetPolicyPathInfo("Children.Count", out _, out var collectionDepth, out var collectionAccess));
            Assert.Equal(1, collectionDepth);
            Assert.True(collectionAccess);
        }

        [Theory]
        [InlineData("=", ComparisonOperator.Equal)]
        [InlineData("==", ComparisonOperator.Equal)]
        [InlineData("<>", ComparisonOperator.NotEqual)]
        [InlineData("!=", ComparisonOperator.NotEqual)]
        [InlineData("CONTAIN", ComparisonOperator.Contains)]
        [InlineData("@", ComparisonOperator.Contains)]
        [InlineData("@*", ComparisonOperator.StartsWith)]
        [InlineData("*@", ComparisonOperator.EndsWith)]
        [InlineData(" NOT   IN ", ComparisonOperator.NotIn)]
        public void OperatorNormalization_ReusesExistingAliases(string spelling, ComparisonOperator expected)
        {
            Assert.Equal(expected, QueryPolicyFieldMetadata.NormalizeComparisonOperator(spelling));
        }

        [Fact]
        public void Context_AppliesNavigationDepthToCanonicalPathsNotScalarMembers()
        {
            var schema = ExpressionSchema.FromType<PathEntity>();
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(maxNavigationDepth: 1, allowCollectionAccess: false));

            Assert.True(context.IsNavigationAllowed("Customer.Name", out var oneLevel));
            Assert.Equal(1, oneLevel);
            Assert.False(context.IsNavigationAllowed("Customer.Address.City", out var twoLevels));
            Assert.Equal(2, twoLevels);
            Assert.True(context.IsNavigationAllowed("CreatedAt.Year", out var scalarDepth));
            Assert.Equal(0, scalarDepth);
            Assert.False(context.IsCollectionAccessAllowed("Children.Count"));
            Assert.True(context.IsCollectionAccessAllowed("CreatedAt.Year"));
        }

        [Fact]
        public void Context_RequiresOperatorPermissionForEveryComparisonField()
        {
            var schema = ExpressionSchema.FromType<QueryEntity>(
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowedOperators:
                    new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["cost"] = new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
                        ["Discount"] = new[] { ComparisonOperator.Equal }
                    }));

            Assert.True(context.AreOperatorsAllowed(
                new[] { "Price", "cost" },
                QueryPolicyFieldMetadata.NormalizeComparisonOperator(">")));
            Assert.False(context.AreOperatorsAllowed(
                new[] { "Price", "Discount" },
                QueryPolicyFieldMetadata.NormalizeComparisonOperator(">")));
            Assert.True(context.IsOperatorAllowed(
                "Discount",
                QueryPolicyFieldMetadata.NormalizeComparisonOperator("=")));
            Assert.False(context.IsOperatorAllowed(
                "Discount",
                QueryPolicyFieldMetadata.NormalizeComparisonOperator("not in")));
        }

        private sealed class QueryEntity
        {
            public decimal Price { get; set; }
            public decimal Discount { get; set; }
            public string Name { get; set; }
            public decimal Secret { get; set; }
        }

        private sealed class OtherEntity
        {
            public int Id { get; set; }
        }

        private sealed class PathEntity
        {
            public DateTime CreatedAt { get; set; }
            public PathCustomer Customer { get; set; }
            public List<PathChild> Children { get; set; }
        }

        private sealed class PathCustomer
        {
            public string Name { get; set; }
            public PathAddress Address { get; set; }
        }

        private sealed class PathAddress
        {
            public string City { get; set; }
        }

        private sealed class PathChild
        {
            public string Name { get; set; }
        }
    }
}
