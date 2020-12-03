using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterGroupTest
    {
        [Fact]
        public void TryBuildPredicate_FilterGroup_Succeed()
        {
            var query = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                }
            }.TryBuildPredicate<TestEntity>(validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void TryBuildPredicate_FilterGroups_Succeed()
        {
            var query = new FilterGroup[]
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
            }.TryBuildPredicate<TestEntity>(validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(2, value);
            }
        }

        [Fact]
        public void TryBuildPredicate_FilterGroups_HasInvalidProperty_Failed()
        {
            var query = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
                }
            }.TryBuildPredicate<TestEntity>(validProperties: new string[] { "Integer" });
            Assert.False(query.Succeeded);
        }

        [Fact]
        public void ParsePredicate_CustomVarableResolver_Success()
        {
            var query = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Integer", Operator = "=", Value = "user.id" }
                }
            }.TryBuildPredicate<TestEntity>(new CustomVariableResolver(), validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void TryBuildPredicate_FilterGroups_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var query = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
                }
            }.TryBuildPredicate<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
            Assert.True(query.Succeeded);
        }

        [Fact]
        public void BuildPredicate_FilterGroups_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var query = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                    new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
                }
            }.BuildPredicate<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
        }
    }
}
