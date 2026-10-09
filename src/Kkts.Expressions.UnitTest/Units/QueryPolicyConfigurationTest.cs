using System;
using System.Collections.Generic;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyConfigurationTest
    {
        [Fact]
        public void Constructor_DefaultsToUnlimitedAndAllowsCollectionAccess()
        {
            var policy = new QueryPolicy();

            Assert.Null(policy.MaxExpressionLength);
            Assert.Null(policy.MaxParenthesisDepth);
            Assert.Null(policy.MaxAtomicConditions);
            Assert.Null(policy.MaxInItems);
            Assert.Null(policy.MaxNavigationDepth);
            Assert.True(policy.AllowCollectionAccess);
            Assert.Empty(policy.AllowedOperators);
        }

        [Fact]
        public void Recommended_IsExplicitAndUsesDocumentedStartingLimits()
        {
            var policy = QueryPolicy.Recommended;

            Assert.Same(policy, QueryPolicy.Recommended);
            Assert.Equal(4096, policy.MaxExpressionLength);
            Assert.Equal(16, policy.MaxParenthesisDepth);
            Assert.Equal(64, policy.MaxAtomicConditions);
            Assert.Equal(100, policy.MaxInItems);
            Assert.Equal(3, policy.MaxNavigationDepth);
            Assert.False(policy.AllowCollectionAccess);
            Assert.Empty(policy.AllowedOperators);
        }

        [Theory]
        [InlineData(-1, null, null, null, null, "maxExpressionLength")]
        [InlineData(null, -1, null, null, null, "maxParenthesisDepth")]
        [InlineData(null, null, -1, null, null, "maxAtomicConditions")]
        [InlineData(null, null, null, -1, null, "maxInItems")]
        [InlineData(null, null, null, null, -1, "maxNavigationDepth")]
        public void Constructor_RejectsNegativeLimits(
            int? maxExpressionLength,
            int? maxParenthesisDepth,
            int? maxAtomicConditions,
            int? maxInItems,
            int? maxNavigationDepth,
            string parameterName)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QueryPolicy(
                    maxExpressionLength,
                    maxParenthesisDepth,
                    maxAtomicConditions,
                    maxInItems,
                    maxNavigationDepth));

            Assert.Equal(parameterName, exception.ParamName);
        }

        [Fact]
        public void Constructor_AcceptsZeroLimits()
        {
            var policy = new QueryPolicy(
                maxExpressionLength: 0,
                maxParenthesisDepth: 0,
                maxAtomicConditions: 0,
                maxInItems: 0,
                maxNavigationDepth: 0);

            Assert.Equal(0, policy.MaxExpressionLength);
            Assert.Equal(0, policy.MaxParenthesisDepth);
            Assert.Equal(0, policy.MaxAtomicConditions);
            Assert.Equal(0, policy.MaxInItems);
            Assert.Equal(0, policy.MaxNavigationDepth);
        }

        [Fact]
        public void Constructor_SnapshotsOperatorRulesAndExposesReadOnlyCollections()
        {
            var operators = new List<ComparisonOperator>
            {
                ComparisonOperator.Equal,
                ComparisonOperator.GreaterThan,
                ComparisonOperator.Equal
            };
            var rules = new Dictionary<string, IEnumerable<ComparisonOperator>>(StringComparer.Ordinal)
            {
                ["Price"] = operators
            };

            var policy = new QueryPolicy(allowedOperators: rules);
            operators.Clear();
            rules["Price"] = new[] { ComparisonOperator.Contains };
            rules["Name"] = new[] { ComparisonOperator.Equal };

            Assert.Single(policy.AllowedOperators);
            Assert.Equal(
                new[] { ComparisonOperator.Equal, ComparisonOperator.GreaterThan },
                policy.AllowedOperators["price"]);
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, IReadOnlyCollection<ComparisonOperator>>)policy.AllowedOperators)
                .Add("Name", new[] { ComparisonOperator.Equal }));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ComparisonOperator>)policy.AllowedOperators["Price"])
                .Add(ComparisonOperator.Contains));
        }

        [Fact]
        public void Constructor_RejectsDuplicatePathsNullRulesAndUndefinedOperators()
        {
            Assert.Throws<ArgumentException>(() => new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>(StringComparer.Ordinal)
                {
                    ["FirstName"] = new[] { ComparisonOperator.Equal },
                    ["First Name"] = new[] { ComparisonOperator.Contains }
                }));

            Assert.Throws<ArgumentException>(() => new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Price"] = null
                }));

            Assert.Throws<ArgumentException>(() => new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    [" "] = new[] { ComparisonOperator.Equal }
                }));

            Assert.Throws<ArgumentOutOfRangeException>(() => new QueryPolicy(allowedOperators:
                new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Price"] = new[] { (ComparisonOperator)2 }
                }));
        }
    }
}
