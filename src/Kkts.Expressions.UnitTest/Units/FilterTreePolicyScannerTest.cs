using System;
using System.Collections.Generic;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreePolicyScannerTest
    {
        [Fact]
        public void TreeDepthCountsLogicalContainersWithoutUsingParenthesisDepth()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Not(FilterNode.Condition("Id", "=", FilterValue.Number("1")))
            });
            var policy = new QueryPolicy(maxParenthesisDepth: 0).WithMaxFilterTreeDepth(2);
            var execution = new QueryPolicyExecution(policy);

            Assert.True(FilterTreePolicyScanner.TryScan(tree, execution));
            Assert.Empty(execution.Diagnostics.ToReadOnlyList());

            var tooDeep = FilterNode.And(new[]
            {
                FilterNode.Not(FilterNode.Or(new[]
                {
                    FilterNode.Condition("Id", "=", FilterValue.Number("1"))
                }))
            });
            var tooDeepExecution = new QueryPolicyExecution(policy);

            Assert.False(FilterTreePolicyScanner.TryScan(tooDeep, tooDeepExecution));
            var diagnostic = Assert.Single(tooDeepExecution.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-filter-tree-depth-exceeded", diagnostic.Code);
            Assert.Equal(2, diagnostic.ConfiguredLimit);
            Assert.Equal(3, diagnostic.ObservedValue);
            Assert.Equal("/and/0/not/or", diagnostic.InputPath);
            Assert.Equal(0, diagnostic.Start);
            Assert.Equal(0, diagnostic.Length);
        }

        [Fact]
        public void LeafHasDepthZeroAndSingleChildGroupsAreCounted()
        {
            var leaf = FilterNode.Condition("Id", "=", FilterValue.Number("1"));
            Assert.True(FilterTreePolicyScanner.TryScan(
                leaf,
                new QueryPolicyExecution(new QueryPolicy().WithMaxFilterTreeDepth(0))));

            var group = FilterNode.And(new[] { leaf });
            var execution = new QueryPolicyExecution(new QueryPolicy().WithMaxFilterTreeDepth(0));
            Assert.False(FilterTreePolicyScanner.TryScan(group, execution));
            Assert.Equal(
                "/and",
                Assert.Single(execution.Diagnostics.ToReadOnlyList()).InputPath);
        }

        [Fact]
        public void CountsOnlyLeavesAndRejectsTheFirstExcessCondition()
        {
            var tree = FilterNode.Or(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Number("1")),
                FilterNode.Condition("Id", "=", FilterValue.Number("2"))
            });
            var execution = new QueryPolicyExecution(new QueryPolicy(maxAtomicConditions: 1));

            Assert.False(FilterTreePolicyScanner.TryScan(tree, execution));
            var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-condition-count-exceeded", diagnostic.Code);
            Assert.Equal(2, diagnostic.ObservedValue);
            Assert.Equal("/or/1/field", diagnostic.InputPath);

            var oneLeaf = new QueryPolicyExecution(new QueryPolicy(maxAtomicConditions: 1));
            Assert.True(FilterTreePolicyScanner.TryScan(
                FilterNode.And(new[] { tree.Children[0] }),
                oneLeaf));
        }

        [Fact]
        public void TextBudgetCountsExactTypedContributionsWithoutJsonDelimiters()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("F", "=", FilterValue.Number("18446744073709551615")),
                FilterNode.Condition("S", "=", FilterValue.String("é")),
                FilterNode.Condition("N", "=", FilterValue.Null),
                FilterNode.Condition("B", "=", FilterValue.Boolean(false)),
                FilterNode.Condition("V", "=", FilterValue.Variable("user.id"))
            });
            // 1+1+20 + 1+1+1 + 1+1+4 + 1+1+5 + 1+1+7 = 47.
            var exact = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 47));
            Assert.True(FilterTreePolicyScanner.TryScan(tree, exact));
            Assert.Empty(exact.Diagnostics.ToReadOnlyList());

            var tooShort = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 46));
            Assert.False(FilterTreePolicyScanner.TryScan(tree, tooShort));
            var diagnostic = Assert.Single(tooShort.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-expression-length-exceeded", diagnostic.Code);
            Assert.Equal(47, diagnostic.ObservedValue);
            Assert.Equal("/and/4/value", diagnostic.InputPath);
            Assert.Equal(0, diagnostic.Start);
            Assert.Equal(0, diagnostic.Length);
        }

        [Fact]
        public void MembershipBudgetCountsEverySuppliedSlotBeforeConversion()
        {
            var tree = FilterNode.Condition(
                "Id",
                "in",
                FilterValue.Collection(new[]
                {
                    FilterValue.Null,
                    FilterValue.Number("1"),
                    FilterValue.Variable("one")
                }));
            var atLimit = new QueryPolicyExecution(new QueryPolicy(maxInItems: 3));
            Assert.True(FilterTreePolicyScanner.TryScan(tree, atLimit));

            var overLimit = new QueryPolicyExecution(new QueryPolicy(maxInItems: 2));
            Assert.False(FilterTreePolicyScanner.TryScan(tree, overLimit));
            var diagnostic = Assert.Single(overLimit.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-in-items-exceeded", diagnostic.Code);
            Assert.Equal("/value/2", diagnostic.InputPath);
            Assert.Equal(3, diagnostic.ObservedValue);
        }

        [Fact]
        public void TextContributionsAggregateWithAnEarlierExpressionOnTheSameExecution()
        {
            var execution = new QueryPolicyExecution(new QueryPolicy(maxExpressionLength: 8));
            Assert.True(execution.TryAdmitCombinedExpression("Id = 1"));
            Assert.False(FilterTreePolicyScanner.TryScan(
                FilterNode.Condition("X", "=", FilterValue.Number("1")),
                execution));
            var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
            Assert.Equal(9, diagnostic.ObservedValue);
        }

        [Fact]
        public void ContextPolicyUsesCanonicalAliasPermissionsAndNavigationDepth()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["publicId"] = "Id",
                    ["shortNested"] = "Nested.Value"
                });
            var operatorContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Id"] = new[] { ComparisonOperator.Equal }
                    }));
            var deniedOperator = FilterNode.Condition("publicId", "!=", FilterValue.Number("1"));
            var operatorExecution = new QueryPolicyExecution(operatorContext.Policy);

            Assert.False(FilterTreePolicyScanner.TryScan(
                deniedOperator,
                operatorContext,
                operatorExecution));
            Assert.Equal(
                "/op",
                Assert.Single(operatorExecution.Diagnostics.ToReadOnlyList()).InputPath);
            var normalizedAlias = new QueryPolicyExecution(operatorContext.Policy);
            Assert.True(FilterTreePolicyScanner.TryScan(
                FilterNode.Condition("publicId", "==", FilterValue.Number("1")),
                operatorContext,
                normalizedAlias));

            var navigationContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(maxNavigationDepth: 0));
            var tooDeep = FilterNode.Condition("shortNested", "=", FilterValue.Number("1"));
            var navigationExecution = new QueryPolicyExecution(navigationContext.Policy);

            Assert.False(FilterTreePolicyScanner.TryScan(
                tooDeep,
                navigationContext,
                navigationExecution));
            var navigation = Assert.Single(navigationExecution.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-navigation-depth-exceeded", navigation.Code);
            Assert.Equal("/field", navigation.InputPath);
            Assert.Equal(1, navigation.ObservedValue);
        }

        [Fact]
        public void ContextPolicyDeniesRestrictedAliasesWithoutCanonicalNameDisclosure()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["publicSecret"] = "Secret"
                },
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Secret", typeof(int), canQuery: false)
                });
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var execution = new QueryPolicyExecution(context.Policy);

            Assert.False(FilterTreePolicyScanner.TryScan(
                FilterNode.Condition("publicSecret", "=", FilterValue.Number("1")),
                context,
                execution));
            var diagnostic = Assert.Single(execution.Diagnostics.ToReadOnlyList());
            Assert.Equal("property-not-queryable", diagnostic.Code);
            Assert.Equal("/field", diagnostic.InputPath);
            Assert.DoesNotContain("Secret", diagnostic.Message);
        }

        [Fact]
        public void ContextPolicyIntersectsExternalAllowlistAndCollectionAccess()
        {
            var schema = ExpressionSchema.FromType<PolicyEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["publicId"] = "Id"
                });
            var allowlistedContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(),
                validProperties: new[] { "Id" });
            var allowlistExecution = new QueryPolicyExecution(allowlistedContext.Policy);
            Assert.False(FilterTreePolicyScanner.TryScan(
                FilterNode.Condition("publicId", "=", FilterValue.Number("1")),
                allowlistedContext,
                allowlistExecution));
            Assert.Equal(
                "property-not-queryable",
                Assert.Single(allowlistExecution.Diagnostics.ToReadOnlyList()).Code);

            var collectionContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowCollectionAccess: false));
            var collectionExecution = new QueryPolicyExecution(collectionContext.Policy);
            Assert.False(FilterTreePolicyScanner.TryScan(
                FilterNode.Condition("Items", "=", FilterValue.Number("1")),
                collectionContext,
                collectionExecution));
            var diagnostic = Assert.Single(collectionExecution.Diagnostics.ToReadOnlyList());
            Assert.Equal("query-policy-collection-access-denied", diagnostic.Code);
            Assert.Equal("/field", diagnostic.InputPath);
        }
        private sealed class PolicyEntity
        {
            public int Id { get; set; }
            public int Secret { get; set; }
            public NestedEntity Nested { get; set; }
            public int[] Items { get; set; }
        }

        private sealed class NestedEntity
        {
            public int Value { get; set; }
        }
    }
}
