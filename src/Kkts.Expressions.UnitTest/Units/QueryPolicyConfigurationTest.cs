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
            Assert.Null(policy.MaxFilterTreeDepth);
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
            Assert.Equal(16, policy.MaxFilterTreeDepth);
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
        public void TreeDepthCopyIsImmutableAndPreservesOtherPolicySettings()
        {
            var original = new QueryPolicy(
                maxExpressionLength: 123,
                maxParenthesisDepth: 7,
                maxAtomicConditions: 9,
                maxInItems: 11,
                maxNavigationDepth: 2,
                allowCollectionAccess: false,
                allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Price"] = new[] { ComparisonOperator.GreaterThan }
                });

            var limited = original.WithMaxFilterTreeDepth(4);
            var unlimited = limited.WithMaxFilterTreeDepth(null);

            Assert.Null(original.MaxFilterTreeDepth);
            Assert.Equal(4, limited.MaxFilterTreeDepth);
            Assert.Null(unlimited.MaxFilterTreeDepth);
            Assert.Equal(123, limited.MaxExpressionLength);
            Assert.Equal(7, limited.MaxParenthesisDepth);
            Assert.Equal(9, limited.MaxAtomicConditions);
            Assert.Equal(11, limited.MaxInItems);
            Assert.Equal(2, limited.MaxNavigationDepth);
            Assert.False(limited.AllowCollectionAccess);
            Assert.Equal(
                new[] { ComparisonOperator.GreaterThan },
                limited.AllowedOperators["price"]);
        }

        [Fact]
        public void TreeDepthCopyAcceptsZeroAndRejectsNegativeValues()
        {
            Assert.Equal(0, new QueryPolicy().WithMaxFilterTreeDepth(0).MaxFilterTreeDepth);
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QueryPolicy().WithMaxFilterTreeDepth(-1));
            Assert.Equal("maxFilterTreeDepth", exception.ParamName);
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
