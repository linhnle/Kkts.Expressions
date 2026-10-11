using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreePredicateBuilderTest
    {
        [Fact]
        public void BuildsRepresentativeJsonTreeLikeHandwrittenPredicate()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Or(new[]
                {
                    FilterNode.Condition("Status", "=", FilterValue.String("Active")),
                    FilterNode.Condition("Priority", ">=", FilterValue.Number("3"))
                }),
                FilterNode.Not(FilterNode.Condition(
                    "Department",
                    "in",
                    FilterValue.Collection(new[]
                    {
                        FilterValue.String("Archived"),
                        FilterValue.String("External")
                    })))
            });
            var predicate = Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(
                FilterTreePredicateBuilder.Build(tree, ExpressionSchema.FromType<FilterEntity>()));
            Func<FilterEntity, bool> handwritten = entity =>
                (entity.Status == "Active" || entity.Priority >= 3) &&
                !(new[] { "Archived", "External" }.Contains(entity.Department));

            var entities = new[]
            {
                new FilterEntity { Status = "Active", Priority = 0, Department = "Sales" },
                new FilterEntity { Status = "Inactive", Priority = 3, Department = "Sales" },
                new FilterEntity { Status = "Active", Priority = 4, Department = "Archived" },
                new FilterEntity { Status = "Inactive", Priority = 2, Department = "External" },
                new FilterEntity { Status = "Inactive", Priority = 2, Department = "Sales" }
            };
            var compiled = predicate.Compile();

            Assert.Equal(entities.Select(handwritten), entities.Select(compiled));
        }

        [Fact]
        public void BuildsNestedAndOrNotPatternLikeHandwrittenPredicate()
        {
            FilterNode Condition(string field) =>
                FilterNode.Condition(field, "=", FilterValue.Boolean(true));
            var tree = FilterNode.And(new[]
            {
                FilterNode.Or(new[] { Condition(nameof(FilterEntity.A)), Condition(nameof(FilterEntity.B)) }),
                FilterNode.Not(FilterNode.Or(new[]
                {
                    Condition(nameof(FilterEntity.C)),
                    FilterNode.And(new[]
                    {
                        Condition(nameof(FilterEntity.D)),
                        Condition(nameof(FilterEntity.E))
                    })
                }))
            });
            var predicate = Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(
                FilterTreePredicateBuilder.Build(tree, ExpressionSchema.FromType<FilterEntity>())).Compile();

            foreach (var a in new[] { false, true })
            foreach (var b in new[] { false, true })
            foreach (var c in new[] { false, true })
            foreach (var d in new[] { false, true })
            foreach (var e in new[] { false, true })
            {
                var entity = new FilterEntity { A = a, B = b, C = c, D = d, E = e };
                Assert.Equal((a || b) && !(c || (d && e)), predicate(entity));
            }
        }

        [Fact]
        public void PreservesSingleChildGroupsRepeatedNotAndEmptyMembership()
        {
            var leaf = FilterNode.Condition("Priority", "=", FilterValue.Number("4"));
            var wrapped = FilterNode.Not(FilterNode.Not(FilterNode.And(new[] { leaf })));
            var predicate = Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(
                FilterTreePredicateBuilder.Build(
                    FilterNode.Or(new[] { wrapped }),
                    ExpressionSchema.FromType<FilterEntity>())).Compile();
            Assert.True(predicate(new FilterEntity { Priority = 4 }));
            Assert.False(predicate(new FilterEntity { Priority = 3 }));

            var emptyIn = FilterNode.Condition(
                "Priority",
                "in",
                FilterValue.Collection(Array.Empty<FilterValue>()));
            var emptyNotIn = FilterNode.Condition(
                "Priority",
                "not in",
                FilterValue.Collection(Array.Empty<FilterValue>()));
            var emptyInPredicate = Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(
                FilterTreePredicateBuilder.Build(emptyIn, ExpressionSchema.FromType<FilterEntity>())).Compile();
            var emptyNotInPredicate = Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(
                FilterTreePredicateBuilder.Build(emptyNotIn, ExpressionSchema.FromType<FilterEntity>())).Compile();
            Assert.False(emptyInPredicate(new FilterEntity { Priority = 4 }));
            Assert.True(emptyNotInPredicate(new FilterEntity { Priority = 4 }));
        }

        [Fact]
        public void BuildsDeepTreeIterativelyAndMembershipWithoutStringReparsing()
        {
            FilterNode tree = FilterNode.Condition("Priority", "in", FilterValue.Collection(new[]
            {
                FilterValue.Number("2"),
                FilterValue.Number("5")
            }));
            for (var index = 0; index < 256; index++)
                tree = FilterNode.Not(FilterNode.Not(tree));

            var predicate = FilterTreePredicateBuilder.Build(
                tree,
                ExpressionSchema.FromType<FilterEntity>());
            Assert.True(Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(predicate)
                .Compile()(new FilterEntity { Priority = 5 }));
            var membership = new MembershipFinder();
            membership.Visit(predicate);
            Assert.NotNull(membership.Call);
            Assert.Equal(typeof(Enumerable), membership.Call.Method.DeclaringType);
            var array = Assert.IsAssignableFrom<NewArrayExpression>(membership.Call.Arguments[0]);
            Assert.Equal(typeof(int), array.Type.GetElementType());
            Assert.Equal(new[] { 2, 5 }, array.Expressions
                .Select(expression => (int)((ConstantExpression)expression).Value)
                .ToArray());
        }

        [Fact]
        public void BuildsEveryOccurrenceWhenBranchesShareTheSameNodeInstance()
        {
            var shared = FilterNode.Condition("Priority", ">=", FilterValue.Number("3"));
            var tree = FilterNode.And(new[] { shared, shared });
            var predicate = FilterTreePredicateBuilder.Build(
                tree,
                ExpressionSchema.FromType<FilterEntity>());
            var body = Assert.IsAssignableFrom<BinaryExpression>(predicate.Body);

            Assert.Equal(ExpressionType.AndAlso, body.NodeType);
            Assert.NotSame(body.Left, body.Right);
            Assert.True(Assert.IsAssignableFrom<Expression<Func<FilterEntity, bool>>>(predicate)
                .Compile()(new FilterEntity { Priority = 3 }));
        }

        private sealed class FilterEntity
        {
            public string Status { get; set; }
            public int Priority { get; set; }
            public string Department { get; set; }
            public bool A { get; set; }
            public bool B { get; set; }
            public bool C { get; set; }
            public bool D { get; set; }
            public bool E { get; set; }
        }

        private sealed class MembershipFinder : ExpressionVisitor
        {
            internal MethodCallExpression Call { get; private set; }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name == nameof(Enumerable.Contains))
                    Call = node;
                return base.VisitMethodCall(node);
            }
        }
    }
}
