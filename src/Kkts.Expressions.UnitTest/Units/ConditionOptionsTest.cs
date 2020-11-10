using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ConditionOptionsTest
    {
        [Fact]
        public void BuildCondition_Success()
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
                c = p.BuildCondition<TestEntity>();
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
                c = p.BuildCondition<TestEntity>();
                Assert.True(c.IsValid);
                value = context.Entities.Where(c).Count();
                Assert.Equal(2, value);

                p = new ConditionOptions { Where = $"Integer={DF.Integer1} and String='{DF.String1}'" };
                c = p.BuildCondition<TestEntity>();
                Assert.True(c.IsValid);
                value = context.Entities.Where(c).Count();
                Assert.Equal(1, value);

                p = new ConditionOptions { OrderBy = "OrderBy1 desc, OrderBy2 desc", Where = "OrderBy1 < 50" };
                c = p.BuildCondition<TestEntity>();
                Assert.True(c.IsValid);
                var e = context.Entities.Where(c).First();
                Assert.Equal("EBC", e.OrderBy2);

                p = new ConditionOptions { OrderBys = new List<OrderByInfo> { new OrderByInfo { Property = "OrderBy1", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, Where = "OrderBy1 < 50" };
                c = p.BuildCondition<TestEntity>();
                Assert.True(c.IsValid);
                e = context.Entities.Where(c).First();
                Assert.Equal("EBC", e.OrderBy2);
            }
        }
    }
}
