using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterAsyncTest
    {
        [Fact]
        public async Task BuildPredicateAsync_AllOperators_Success_WithoutExceptions()
        {
            // integer
            var exp = await (new Filter { Property = "Integer", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = "<>", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = "<", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = "<=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = ">", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = ">=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Integer", Operator = "in", Value = "1, 2, 4" }.BuildPredicateAsync<TestEntity>());
            // integer nullable
            exp = await (new Filter { Property = "IntegerNullable", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "<>", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "<", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "<=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = ">", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = ">=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "IntegerNullable", Operator = "in", Value = "1, 2, 4" }.BuildPredicateAsync<TestEntity>());

            // double
            exp = await (new Filter { Property = "Double", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = "<>", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = "<", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = "<=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = ">", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = ">=", Value = "1.0" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Double", Operator = "in", Value = "1.2, 2, 4" }.BuildPredicateAsync<TestEntity>());
            // double nullable
            exp = await (new Filter { Property = "DoubleNullable", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "<>", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "<", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "<=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = ">", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = ">=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DoubleNullable", Operator = "in", Value = "1, 2, 4" }.BuildPredicateAsync<TestEntity>());

            // boolean
            exp = await (new Filter { Property = "Boolean", Operator = "=", Value = "true" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Boolean", Operator = "!=", Value = "false" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Boolean", Operator = "<>", Value = "false" }.BuildPredicateAsync<TestEntity>());
            // boolean nullable
            exp = await (new Filter { Property = "BooleanNullable", Operator = "=", Value = "true" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "BooleanNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "BooleanNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "BooleanNullable", Operator = "!=", Value = "false" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "BooleanNullable", Operator = "<>", Value = "false" }.BuildPredicateAsync<TestEntity>());

            // string
            exp = await (new Filter { Property = "String", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "<>", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "<>", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "startsWith", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "startWith", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "@*", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "endsWith", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "endWith", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "*@", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "contains", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "contain", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "@", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "String", Operator = "in", Value = "'1.0','lsdf'" }.BuildPredicateAsync<TestEntity>());

            // Guid
            exp = await (new Filter { Property = "Guid", Operator = "=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Guid", Operator = "!=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Guid", Operator = "<>", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Guid", Operator = "in", Value = "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'" }.BuildPredicateAsync<TestEntity>());
            // Guid nullable
            exp = await (new Filter { Property = "GuidNullable", Operator = "=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "GuidNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "GuidNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "GuidNullable", Operator = "!=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "GuidNullable", Operator = "<>", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "GuidNullable", Operator = "in", Value = "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'" }.BuildPredicateAsync<TestEntity>());

            // Enum
            exp = await (new Filter { Property = "Option", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Option", Operator = "=", Value = "Option1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Option", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Option", Operator = "<>", Value = "Option1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Option", Operator = "in", Value = "1, 2, 0" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "Option", Operator = "in", Value = "'Option1', 'Option2', 'Option3'" }.BuildPredicateAsync<TestEntity>());
            // Enum Nullable
            exp = await (new Filter { Property = "OptionNullable", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "=", Value = "Option1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "!=", Value = "1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "<>", Value = "Option1" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "in", Value = "1, 2, 0" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "OptionNullable", Operator = "in", Value = "'Option1', 'Option2', 'Option3'" }.BuildPredicateAsync<TestEntity>());

            // DateTime
            exp = await (new Filter { Property = "DateTime", Operator = "=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = "!=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = "<>", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = "<", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = "<=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = ">", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = ">=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime", Operator = "in", Value = $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'" }.BuildPredicateAsync<TestEntity>());
            // DateTime nullable
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "!=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "<>", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "<", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "<=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = ">", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = ">=", Value = DF.DateTimeString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeNullable", Operator = "in", Value = $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'" }.BuildPredicateAsync<TestEntity>());

            // DateTimeOffset
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "!=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "<>", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "<", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "<=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = ">", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = ">=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffset", Operator = "in", Value = $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'" }.BuildPredicateAsync<TestEntity>());
            // DateTimeOffset nullable
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = "" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = null }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "!=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "<>", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "<", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "<=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = ">", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = ">=", Value = DF.DateTimeOffsetString1 }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTimeOffsetNullable", Operator = "in", Value = $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'" }.BuildPredicateAsync<TestEntity>());

            // nested property
            exp = await (new Filter { Property = "Parent.Id", Operator = "=", Value = "1" }.BuildPredicateAsync<TestEntity>());

            // variable resolver
            exp = await (new Filter { Property = "DateTime", Operator = "=", Value = "now" }.BuildPredicateAsync<TestEntity>());
            exp = await (new Filter { Property = "DateTime.Year", Operator = "=", Value = "now.year" }.BuildPredicateAsync<TestEntity>());

            // actually just test without exceptions
            Assert.NotNull(exp);
        }

        [Fact]
        public async Task BuildPredicateAsync_Filters_Succeed()
        {
            var query = await (new Filter[]
            {
                new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
            }.BuildPredicateAsync<TestEntity>());
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_Filters_Succeed()
        {
            var query = await new Filter[]
            {
                new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: new string[] { "Integer", "String" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_Filters_HasInvalidProperty_Failed()
        {
            var query = await new Filter[]
            {
                new Filter{ Property = "Integer", Operator = "=", Value = DF.Integer1.ToString() },
                new Filter{ Property = "String", Operator = "=", Value = DF.String1 }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: new string[] { "Integer" });
            Assert.False(query.Succeeded);
        }

        [Fact]
        public async Task ParsePredicateAsync_CustomVariableResolver_Success()
        {
            var query = await new Filter[]
            {
                new Filter{ Property = "Integer", Operator = "=", Value = "user.Id" }
            }.TryBuildPredicateAsync<TestEntity>(new CustomVariableResolver(), validProperties: new string[] { "Integer" });
            Assert.True(query.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(query.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task TryBuildPredicateAsync_Filters_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new string[] { };
            var query = await new Filter[]
            {
                new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
            }.TryBuildPredicateAsync<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
            Assert.True(query.Succeeded);
        }

        [Fact]
        public async Task BuildPredicateAsync_Filters_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var query = await new Filter[]
            {
                new Filter{ Property = "Number", Operator = "=", Value = DF.Integer1.ToString() },
                new Filter{ Property = "NumberNullable", Operator = "=", Value = DF.Integer2.ToString() }
            }.BuildPredicateAsync<TestEntity>(validProperties: validProperties, propertyMapping: propertyMapping);
            Assert.NotNull(query);
        }
    }
}
