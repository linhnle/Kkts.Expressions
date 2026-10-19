using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class OrderByKeysTest
    {
        [Fact]
        public void GetKeys_StringExpression_ReturnsKeysInOrder()
        {
            var clause = Interpreter.TryBuildOrderByClause<TestEntity>("orderBy2 desc, id").Result;

            var keys = clause.GetKeys<TestEntity>();

            Assert.Equal(2, keys.Count);
            Assert.True(keys[0].Descending);
            Assert.Equal("OrderBy2", ((MemberExpression)keys[0].KeySelector.Body).Member.Name);
            Assert.False(keys[1].Descending);
            Assert.Equal("Id", ((MemberExpression)keys[1].KeySelector.Body).Member.Name);
            AssertMatchesSort(clause, keys);
        }

        [Fact]
        public void GetKeys_OrderByInfos_ReturnsKeysInOrder()
        {
            var clause = new[]
            {
                new OrderByInfo { Property = "Parent.Name" },
                new OrderByInfo { Property = "OrderBy1", Descending = true },
            }.TryBuildOrderByClause<TestEntity>().Result;

            var keys = clause.GetKeys(typeof(TestEntity));

            Assert.Equal(new[] { false, true }, keys.Select(key => key.Descending));
            Assert.Equal("p.Parent.Name", keys[0].KeySelector.Body.ToString());
            AssertMatchesSort(clause, keys);
        }

        [Fact]
        public void GetKeys_PropertyMapping_UsesMappedMember()
        {
            var clause = Interpreter.TryBuildOrderByClause<TestEntity>(
                "name desc",
                propertyMapping: new Dictionary<string, string> { ["Name"] = "OrderBy1" }).Result;

            var keys = clause.GetKeys<TestEntity>();

            Assert.Single(keys);
            Assert.Equal("p.OrderBy1", keys[0].KeySelector.Body.ToString());
            AssertMatchesSort(clause, keys);
        }

        [Fact]
        public void GetKeys_PublicExpressionField_UsesComposedSelector()
        {
            var context = new ExpressionQueryContext(
                new QuerySchema<TestEntity>()
                    .Field("score", item => item.Integer * 2)
                    .Field("label", item => item.String)
                    .Build(),
                new QueryPolicy());
            var clause = context.TryBuildOrderByClause("score desc, label").Result;

            var keys = clause.GetKeys<TestEntity>();

            Assert.Equal(2, keys.Count);
            Assert.Equal("(p.Integer * 2)", keys[0].KeySelector.Body.ToString());
            Assert.Equal(typeof(int), keys[0].KeySelector.ReturnType);
            AssertMatchesSort(clause, keys);
        }

        [Fact]
        public void GetKeys_DoesNotChangeSorting()
        {
            var clause = Interpreter.TryBuildOrderByClause<TestEntity>("orderBy1 desc").Result;
            var data = new[] { new TestEntity { OrderBy1 = 1 }, new TestEntity { OrderBy1 = 3 }, new TestEntity { OrderBy1 = 2 } };

            clause.GetKeys<TestEntity>();

            Assert.Equal(new[] { 3, 2, 1 }, clause.Sort(data).Select(item => item.OrderBy1));
            Assert.Throws<ArgumentNullException>(() => clause.GetKeys(null));
        }

        [Fact]
        public void ReadmeExample_PrintsKeys()
        {
            var clause = Interpreter.TryBuildOrderByClause<ReadmeData>("Name desc, Id").Result;

            var lines = new List<string>();
            foreach (OrderByKey key in clause.GetKeys<ReadmeData>())
            {
                lines.Add($"{key.KeySelector} {(key.Descending ? "desc" : "asc")}");
            }

            Assert.Equal(new[] { "p => p.Name desc", "p => p.Id asc" }, lines);
        }

        private sealed class ReadmeData
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private static void AssertMatchesSort(OrderByClause clause, IReadOnlyList<OrderByKey> keys)
        {
            var sorted = clause.Sort(Array.Empty<TestEntity>().AsQueryable()).Expression;
            var applied = new List<(LambdaExpression Selector, bool Descending)>();
            while (sorted is MethodCallExpression call && call.Arguments.Count == 2)
            {
                applied.Insert(0, ((LambdaExpression)((UnaryExpression)call.Arguments[1]).Operand, call.Method.Name.EndsWith("Descending")));
                sorted = call.Arguments[0];
            }

            Assert.Equal(applied.Count, keys.Count);
            for (var i = 0; i < keys.Count; i++)
            {
                Assert.Equal(applied[i].Selector.ToString(), keys[i].KeySelector.ToString());
                Assert.Equal(applied[i].Selector.ReturnType, keys[i].KeySelector.ReturnType);
                Assert.Equal(applied[i].Descending, keys[i].Descending);
            }
        }
    }
}
