using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterGroupAsyncTest
    {
        [Fact]
        public async Task TryBuildPredicateAsync_FilterGroup_Succeed()
        {
            var query = await new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_FilterGroups_Succeed()
        {
            var query = await new FilterGroup[]
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
            }.TryBuildPredicateAsync<TestEntity>(validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(2, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_FilterGroups_HasInvalidProperty_Failed()
        {
            var query = await new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: new string[] { "Integer" });
            Assert.False(query.Succeeded);
        }

        [Fact]
        public async Task ParsePredicateAsync_CustomVarableResolver_Success()
        {
            var query = await new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = "user.id" }
                }
            }.TryBuildPredicateAsync<TestEntity>(new CustomVariableResolver(), validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_FilterGroups_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var query = await new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
                }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
            Assert.True(query.Succeeded);
        }

        [Fact]
        public async Task BuildPredicateAsync_FilterGroups_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var query = await new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
                }
            }.BuildPredicateAsync<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
        }
    }
}
