using System;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class InterpreterTest
    {
        #region Expresion Parser
        [Fact]
        public void ParsePredicate_Integer_Succeed()
        {
            var query = $"Integer={DF.Integer1} and Integer != 0 and Integer<{DF.Integer2} and Integer<={DF.Integer1} and Integer>={DF.Integer1} and Integer>0 and Integer in [{DF.Integer1},{DF.Integer2},{DF.Integer3}]";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_IntegerNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Integer={DF.Integer2} and IntegerNullable=null");
            var result2 = Interpreter.ParsePredicate<TestEntity>($"IntegerNullable>0");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_Double_Succeed()
        {
            var query = $"Double={DF.Double3} and Double != 0.0 and Double<{DF.DoubleN} and Double<={DF.Double3} and Double>={DF.Double1} and Double>0.0 and Double in [{DF.Double1},{DF.Double2},{DF.Double3},{DF.DoubleN}]";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_DoubleNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Double={DF.Double2} and DoubleNullable=null");
            var result2 = Interpreter.ParsePredicate<TestEntity>($"DoubleNullable>0");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_String_Succeed()
        {
            var query = $"String='{DF.String1}' and String != null and String @* 'N' and String *@ '1' and String @ 'm' and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.DoubleN}']";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }


        [Fact]
        public void ParsePredicate_String_ComparisonFunction_Succeed()
        {
            var query = $"String='{DF.String1}' and String != null and String.startsWith(String) and String.endsWith('1') and String.contains('m') and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.StringN}']";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            var t1 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}'.contains('Name')");
            var t2 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}'.startsWith('Name')");
            var t3 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}'.endsWith('1')");
            var t4 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}' @ 'Name'");
            var t5 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}' @* 'Name'");
            var t6 = Interpreter.ParsePredicate<TestEntity>($"'{DF.String1}' *@ '1'");
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            Assert.True(t1.Succeeded);
            Assert.True(t2.Succeeded);
            Assert.True(t3.Succeeded);
            Assert.True(t4.Succeeded);
            Assert.True(t5.Succeeded);
            Assert.True(t6.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t3.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t4.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t5.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(t6.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_Guid_Succeed()
        {
            var query = $"Guid='{DF.Guid1}' and Guid in ['{DF.Guid1}','{DF.Guid2}','{DF.Guid3}']";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_GuidNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Guid='{DF.Guid2}' and GuidNullable=null");
            var result2 = Interpreter.ParsePredicate<TestEntity>($"GuidNullable<>null");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_NestedProperty_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Parent.Id = 1 and Parent.Id >= 1");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void ParsePredicate_ValidProperties_Succeed()
        {
            string[] validProperties = { "String", "Integer" };
            var result = Interpreter.ParsePredicate<TestEntity>($"String = '' and Integer >= 1", validProperties: validProperties);
            Assert.True(result.Succeeded);
            result = Interpreter.ParsePredicate<TestEntity>($"Boolean = false and Double = 0", validProperties: validProperties);
            Assert.False(result.Succeeded);
            Assert.True(result.InvalidProperties.Contains("Boolean") && result.InvalidProperties.Contains("Double"));
        }

        [Fact]
        public void ParsePredicate_Enum_Succeed()
        {
            var query = $"Option='{TestOptions.Option1}' and Option != 1 and Option = 0  and Option in ['{TestOptions.Option1}','{TestOptions.Option2}','{TestOptions.Option3}']";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_EnumNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Option='{TestOptions.Option2}' and OptionNullable=null");
            var result2 = Interpreter.ParsePredicate<TestEntity>($"OptionNullable='{TestOptions.Option1}'");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_BooleanAndBooleanNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Boolean and BooleanNullable=null");
            var result2 = Interpreter.ParsePredicate<TestEntity>($"BooleanNullable=true and Not(Boolean) and !Boolean and Boolean=false");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.True(value > 0);
            }
        }

        [Fact]
        public void ParsePredicate_DateTimeAndVariableResolver_Succeed()
        {
            var query = $"DateTime='{DF.DateTime1}' and DateTime<'{DF.DateTime2}' and DateTime<='{DF.DateTime1}' and DateTime>='{DF.DateTime1}' and DateTime in ['{DF.DateTime1}','{DF.DateTime2}','{DF.DateTime3}']";
            var query2 = $"DateTime='{DF.DateTimeString1}' and DateTime<'{DF.DateTimeString2}' and DateTime<='{DF.DateTimeString1}' and DateTime>='{DF.DateTimeString1}' and DateTime in ['{DF.DateTimeString1}','{DF.DateTimeString2}','{DF.DateTimeString3}']";
            var query3 = $"DateTime.Year=now.year and DateTime.Year==utcnow.year";
            var and1 = Interpreter.ParsePredicate<TestEntity>(query);
            var and1_1 = Interpreter.ParsePredicate<TestEntity>(query2);
            var and1_2 = Interpreter.ParsePredicate<TestEntity>(query3);
            var and2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&&"));
            var and3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "&"));
            var or1 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "or"));
            var or2 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "||"));
            var or3 = Interpreter.ParsePredicate<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1_1.Succeeded);
            Assert.True(and1_2.Succeeded);
            Assert.True(and1.Succeeded);
            Assert.True(and2.Succeeded);
            Assert.True(and3.Succeeded);
            Assert.True(or1.Succeeded);
            Assert.True(or2.Succeeded);
            Assert.True(or3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(and1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and1_1.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and1_2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(and3.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(or1.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or2.Result);
                Assert.True(value > 0);
                value = context.Entities.Count(or3.Result);
                Assert.True(value > 0);
            }
        }
        #endregion Expresion Parser
    }
}
