using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterNodeContractTest
    {
        [Fact]
        public void GroupsPreserveOrderDuplicatesAndSnapshotTheirChildren()
        {
            var first = FilterNode.Condition("Status", "=", FilterValue.String("Active"));
            var second = FilterNode.Condition("Priority", ">=", FilterValue.Number("3"));
            var source = new List<FilterNode> { first, second, first };

            var group = FilterNode.And(source);
            source.Clear();

            Assert.Equal(FilterNodeKind.And, group.Kind);
            Assert.Equal(new[] { first, second, first }, group.Children);
            Assert.Same(first, group.Children[0]);
            Assert.Same(first, group.Children[2]);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<FilterNode>)group.Children).Add(second));
        }

        [Fact]
        public void SingleChildGroupsAndSharedOccurrencesAreValid()
        {
            var condition = FilterNode.Condition("Id", "==", FilterValue.Number("1"));
            var and = FilterNode.And(new[] { condition });
            var or = FilterNode.Or(new[] { condition, condition });

            Assert.Same(condition, and.Children.Single());
            Assert.Equal(2, or.Children.Count);
            Assert.All(or.Children, child => Assert.Same(condition, child));
        }

        [Fact]
        public void FactoriesRejectMissingOrMalformedNodeShapes()
        {
            Assert.Throws<ArgumentNullException>(() => FilterNode.And(null));
            Assert.Throws<ArgumentException>(() => FilterNode.And(Array.Empty<FilterNode>()));
            Assert.Throws<ArgumentException>(() => FilterNode.Or(new FilterNode[] { null }));
            Assert.Throws<ArgumentNullException>(() => FilterNode.Not(null));
            Assert.Throws<ArgumentException>(() => FilterNode.Condition(" ", "=", FilterValue.Null));
            Assert.Throws<ArgumentException>(() => FilterNode.Condition("Id", "matches", FilterValue.String("x")));
            Assert.Throws<ArgumentNullException>(() => FilterNode.Condition("Id", "=", null));
            Assert.Throws<ArgumentException>(() =>
                FilterNode.Condition("Id", "=", FilterValue.Collection(Array.Empty<FilterValue>())));
        }

        [Fact]
        public void NotAndConditionExposeOnlyTheirOwnShape()
        {
            var condition = FilterNode.Condition("Id", "in", FilterValue.Collection(new[]
            {
                FilterValue.Number("1"),
                FilterValue.Null
            }));
            var not = FilterNode.Not(condition);

            Assert.Equal(FilterNodeKind.Not, not.Kind);
            Assert.Same(condition, not.Child);
            Assert.Empty(not.Children);
            Assert.Null(not.Field);
            Assert.Null(condition.Child);
            Assert.Empty(condition.Children);
            Assert.Equal("Id", condition.Field);
            Assert.Equal("in", condition.Operator);
            Assert.Equal(FilterValueKind.Collection, condition.Value.Kind);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-0")]
        [InlineData("3.14")]
        [InlineData("1e+10")]
        [InlineData("-2.5E-3")]
        public void NumberValueRetainsValidJsonNumberTokens(string token)
        {
            var value = FilterValue.Number(token);

            Assert.Equal(FilterValueKind.Number, value.Kind);
            Assert.Equal(token, value.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("01")]
        [InlineData("+1")]
        [InlineData(".5")]
        [InlineData("1.")]
        [InlineData("1e")]
        [InlineData("NaN")]
        [InlineData(" 1")]
        public void NumberValueRejectsNonJsonNumberTokens(string token)
        {
            Assert.Throws<ArgumentException>(() => FilterValue.Number(token));
        }

        [Fact]
        public void VariableReferencesAreExplicitAndUseUnprefixedDottedNames()
        {
            var literal = FilterValue.String("$ids");
            var reference = FilterValue.Variable("request.ids");

            Assert.Equal(FilterValueKind.String, literal.Kind);
            Assert.Equal(FilterValueKind.Variable, reference.Kind);
            Assert.Equal("$ids", literal.Text);
            Assert.Equal("request.ids", reference.Text);
            Assert.Throws<ArgumentException>(() => FilterValue.Variable("$ids"));
            Assert.Throws<ArgumentException>(() => FilterValue.Variable("request..ids"));
            Assert.Throws<ArgumentException>(() => FilterValue.Variable("request.ids "));
        }

        [Fact]
        public void CollectionsAreFlatAndSnapshotValues()
        {
            var source = new List<FilterValue> { FilterValue.String("A"), FilterValue.Null };
            var collection = FilterValue.Collection(source);
            source.Clear();

            Assert.Equal(new[] { FilterValueKind.String, FilterValueKind.Null }, collection.Items.Select(item => item.Kind));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<FilterValue>)collection.Items).Add(FilterValue.Boolean(true)));
            Assert.Throws<ArgumentException>(() => FilterValue.Collection(new FilterValue[] { null }));
            Assert.Throws<ArgumentException>(() =>
                FilterValue.Collection(new[] { FilterValue.Collection(Array.Empty<FilterValue>()) }));
        }

        [Fact]
        public void FromObjectEncodesSupportedScalarsAndRejectsArbitraryToString()
        {
            Assert.Equal("12.5", FilterValue.FromObject(12.5m).Text);
            Assert.Equal(FilterValueKind.Boolean, FilterValue.FromObject(true).Kind);
            Assert.Equal("A", FilterValue.FromObject('A').Text);
            Assert.Equal("00000000-0000-0000-0000-000000000001",
                FilterValue.FromObject(Guid.Parse("00000000-0000-0000-0000-000000000001")).Text);
            Assert.Throws<NotSupportedException>(() => FilterValue.FromObject(new ExplosiveToString()));
            Assert.Throws<NotSupportedException>(() => FilterValue.FromObject(double.NaN));
        }

        private sealed class ExplosiveToString
        {
            public override string ToString() => throw new InvalidOperationException("Must not be called.");
        }
    }
}
