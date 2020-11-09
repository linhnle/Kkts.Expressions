using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTest
    {
        [Fact]
        public void BuildPredicate_AllOperators_Success_WithoutExceptions()
        {
            // integer
            var exp = new Filter { Property = "Integer", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = "<>", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = "<", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = "<=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = ">", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = ">=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Integer", Operator = "in", Value = "1, 2, 4" }.BuildPredicate<TestEntity>();
            // integer nullable
            exp = new Filter { Property = "IntegerNullable", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "<>", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "<", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "<=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = ">", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = ">=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "IntegerNullable", Operator = "in", Value = "1, 2, 4" }.BuildPredicate<TestEntity>();

            // double
            exp = new Filter { Property = "Double", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = "<>", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = "<", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = "<=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = ">", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = ">=", Value = "1.0" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Double", Operator = "in", Value = "1.2, 2, 4" }.BuildPredicate<TestEntity>();
            // double nullable
            exp = new Filter { Property = "DoubleNullable", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "<>", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "<", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "<=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = ">", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = ">=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DoubleNullable", Operator = "in", Value = "1, 2, 4" }.BuildPredicate<TestEntity>();

            // boolean
            exp = new Filter { Property = "Boolean", Operator = "=", Value = "true" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Boolean", Operator = "!=", Value = "false" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Boolean", Operator = "<>", Value = "false" }.BuildPredicate<TestEntity>();
            // boolean nullable
            exp = new Filter { Property = "BooleanNullable", Operator = "=", Value = "true" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "BooleanNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "BooleanNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "BooleanNullable", Operator = "!=", Value = "false" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "BooleanNullable", Operator = "<>", Value = "false" }.BuildPredicate<TestEntity>();

            // string
            exp = new Filter { Property = "String", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "<>", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "<>", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "startsWith", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "startWith", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "@*", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "endsWith", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "endWith", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "*@", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "contains", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "contain", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "@", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "String", Operator = "in", Value = "'1.0','lsdf'" }.BuildPredicate<TestEntity>();

            // Guid
            exp = new Filter { Property = "Guid", Operator = "=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Guid", Operator = "!=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Guid", Operator = "<>", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Guid", Operator = "in", Value = "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'" }.BuildPredicate<TestEntity>();
            // Guid nullable
            exp = new Filter { Property = "GuidNullable", Operator = "=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "GuidNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "GuidNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "GuidNullable", Operator = "!=", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "GuidNullable", Operator = "<>", Value = "32F281EB-9973-4E78-86C0-3D7AEB791E6F" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "GuidNullable", Operator = "in", Value = "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'" }.BuildPredicate<TestEntity>();

            // Enum
            exp = new Filter { Property = "Option", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Option", Operator = "=", Value = "Option1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Option", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Option", Operator = "<>", Value = "Option1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Option", Operator = "in", Value = "1, 2, 0" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "Option", Operator = "in", Value = "'Option1', 'Option2', 'Option3'" }.BuildPredicate<TestEntity>();
            // Enum Nullable
            exp = new Filter { Property = "OptionNullable", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "=", Value = "Option1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "!=", Value = "1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "<>", Value = "Option1" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "in", Value = "1, 2, 0" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "OptionNullable", Operator = "in", Value = "'Option1', 'Option2', 'Option3'" }.BuildPredicate<TestEntity>();

            // DateTime
            exp = new Filter { Property = "DateTime", Operator = "=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = "!=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = "<>", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = "<", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = "<=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = ">", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = ">=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime", Operator = "in", Value = $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'" }.BuildPredicate<TestEntity>();
            // DateTime nullable
            exp = new Filter { Property = "DateTimeNullable", Operator = "=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "!=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "<>", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "<", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "<=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = ">", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = ">=", Value = DF.DateTimeString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeNullable", Operator = "in", Value = $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'" }.BuildPredicate<TestEntity>();

            // DateTimeOffset
            exp = new Filter { Property = "DateTimeOffset", Operator = "=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = "!=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = "<>", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = "<", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = "<=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = ">", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = ">=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffset", Operator = "in", Value = $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'" }.BuildPredicate<TestEntity>();
            // DateTimeOffset nullable
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = "" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "=", Value = null }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "!=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "<>", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "<", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "<=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = ">", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = ">=", Value = DF.DateTimeOffsetString1 }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTimeOffsetNullable", Operator = "in", Value = $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'" }.BuildPredicate<TestEntity>();

            // nested property
            exp = new Filter { Property = "Parent.Id", Operator = "=", Value = "1" }.BuildPredicate<TestEntity>();

            // variable resolver
            exp = new Filter { Property = "DateTime", Operator = "=", Value = "now" }.BuildPredicate<TestEntity>();
            exp = new Filter { Property = "DateTime.Year", Operator = "=", Value = "now.year" }.BuildPredicate<TestEntity>();

            // actually just test without exceptions
            Assert.NotNull(exp);
        }
    }
}
