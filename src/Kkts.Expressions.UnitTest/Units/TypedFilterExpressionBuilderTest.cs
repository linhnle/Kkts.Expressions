using System;
using System.Linq.Expressions;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class TypedFilterExpressionBuilderTest
    {
        [Fact]
        public void ApplyComparisonBuildsTypedScalarComparison()
        {
            var value = Expression.Parameter(typeof(int), "value");
            var comparison = TypedFilterExpressionBuilder.ApplyComparison(
                value,
                ComparisonOperator.GreaterThanOrEqual,
                Expression.Constant(3));
            var predicate = Expression.Lambda<Func<int, bool>>(comparison, value).Compile();

            Assert.True(predicate(3));
            Assert.False(predicate(2));
        }

        [Fact]
        public void BuildMembershipUsesTypedCollectionWithoutParsingText()
        {
            var value = Expression.Parameter(typeof(int), "value");
            var membership = TypedFilterExpressionBuilder.BuildMembership(
                value,
                Expression.Constant(new[] { 2, 5 }),
                typeof(int),
                negate: false);
            var predicate = Expression.Lambda<Func<int, bool>>(membership, value).Compile();

            Assert.True(predicate(2));
            Assert.False(predicate(3));
        }

        [Fact]
        public void BuildNegatedMembershipPreservesNotInSemantics()
        {
            var value = Expression.Parameter(typeof(string), "value");
            var membership = TypedFilterExpressionBuilder.BuildMembership(
                value,
                Expression.Constant(new[] { "Archived", "External" }),
                typeof(string),
                negate: true);
            var predicate = Expression.Lambda<Func<string, bool>>(membership, value).Compile();

            Assert.False(predicate("Archived"));
            Assert.True(predicate("Active"));
        }

        [Fact]
        public void BuildMembershipRejectsMissingTypedInputs()
        {
            var value = Expression.Parameter(typeof(int), "value");

            Assert.Throws<ArgumentNullException>(() =>
                TypedFilterExpressionBuilder.BuildMembership(value, null, typeof(int), negate: false));
            Assert.Throws<ArgumentNullException>(() =>
                TypedFilterExpressionBuilder.BuildMembership(value, Expression.Constant(new int[0]), null, negate: false));
        }
    }
}
