using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ConditionOptionsAsyncTest
    {
        [Fact]
        public async Task BuildConditionAsync_Success()
        {
            using (var context = DF.GetContext())
            {
                ConditionOptions p;
                Condition<TestEntity> c;
                int value;
                p = new ConditionOptions
                {
                    Filters = new Filter[]
                    {
                        new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                        new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                    }
                };
                c = await p.BuildConditionAsync<TestEntity>();
                Assert.True(c.IsValid);
                value = context.Entities.Where(c).Count();
                Assert.Equal(1, value);

                p = new ConditionOptions
                {
                    FilterGroups = new FilterGroup[]
                    {
                        new FilterGroup
                        {
                            Filters = new List<Filter>
                            {
                                new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                                new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                            }
                        },
                        new FilterGroup
                        {
                            Filters = new List<Filter>
                            {
                                new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer3.ToString() }
                            }
                        }
                    }
                };
                c = await p.BuildConditionAsync<TestEntity>();
                Assert.True(c.IsValid);
                value = context.Entities.Where(c).Count();
                Assert.Equal(2, value);

                p = new ConditionOptions { Where = $"Integer={DF.Integer1} and String='{DF.String1}'" };
                c = await p.BuildConditionAsync<TestEntity>();
                Assert.True(c.IsValid);
                value = context.Entities.Where(c).Count();
                Assert.Equal(1, value);

                p = new ConditionOptions { OrderBy = "OrderBy1 desc, OrderBy2 desc", Where = "OrderBy1 < 50" };
                c = await p.BuildConditionAsync<TestEntity>();
                Assert.True(c.IsValid);
                var e = context.Entities.Where(c).First();
                Assert.Equal("EBC", e.OrderBy2);

                p = new ConditionOptions { OrderBys = new List<OrderByInfo> { new OrderByInfo { Property = "OrderBy1", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, Where = "OrderBy1 < 50" };
                c = await p.BuildConditionAsync<TestEntity>();
                Assert.True(c.IsValid);
                e = context.Entities.Where(c).First();
                Assert.Equal("EBC", e.OrderBy2);
            }
        }

        [Fact]
        public async void TakeAndTakePage_Success()
        {
            var c = await new ConditionOptions().BuildConditionAsync<TestEntity>();
            var p = new Pagination { Offset = 1, Limit = 2 };
            using (var context = DF.GetContext())
            {
                var e = context.Entities.Take(c, p);
                Assert.Equal(2, e.Count());
                var r = e.First();
                Assert.Equal(DF.Integer2, r.Integer);

                var page = context.Entities.TakePage(c, p);
                Assert.Equal(2, page.Records.Count());
                Assert.Equal(5, page.TotalRecords);
                r = page.Records.First();
                Assert.Equal(DF.Integer2, r.Integer);
            }
        }
    }
}
