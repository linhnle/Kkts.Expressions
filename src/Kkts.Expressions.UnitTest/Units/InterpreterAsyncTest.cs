using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class InterpreterAsyncTest
    {
        #region Expression Parser
        [Fact]
        public async Task ParsePredicateAsync_Integer_Succeed()
        {
            var query = $"Integer={DF.Integer1} and Integer != 0 and Integer<{DF.Integer2} and Integer<={DF.Integer1} and Integer>={DF.Integer1} and Integer>0 and Integer in [{DF.Integer1},{DF.Integer2},{DF.Integer3}]";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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
        public async Task ParsePredicateAsync_IntegerNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Integer={DF.Integer2} and IntegerNullable=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"IntegerNullable>0");
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
        public async Task ParsePredicateAsync_Double_Succeed()
        {
            var query = $"Double={DF.Double3} and Double != 0.0 and Double<{DF.DoubleN} and Double<={DF.Double3} and Double>={DF.Double1} and Double>0.0 and Double in [{DF.Double1},{DF.Double2},{DF.Double3},{DF.DoubleN}]";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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
        public async Task ParsePredicateAsync_DoubleNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Double={DF.Double2} and DoubleNullable=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"DoubleNullable>0");
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
        public async Task ParsePredicateAsync_String_Succeed()
        {
            var query = $"String='{DF.String1}' and String != null and String @* 'N' and String *@ '1' and String @ 'm' and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.DoubleN}']";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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
        public async Task ParsePredicateAsync_String_ComparisonFunction_Succeed()
        {
            var query = $"String='{DF.String1}' and String != null and String.startsWith(String) and String.endsWith('1') and String.contains('m') and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.StringN}']";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
            var t1 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}'.contains('Name')");
            var t2 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}'.startsWith('Name')");
            var t3 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}'.endsWith('1')");
            var t4 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}' @ 'Name'");
            var t5 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}' @* 'Name'");
            var t6 = await Interpreter.ParsePredicateAsync<TestEntity>($"'{DF.String1}' *@ '1'");
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
        public async Task ParsePredicateAsync_Guid_Succeed()
        {
            var query = $"Guid='{DF.Guid1}' and Guid in ['{DF.Guid1}','{DF.Guid2}','{DF.Guid3}']";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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
        public async Task ParsePredicateAsync_GuidNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Guid='{DF.Guid2}' and GuidNullable=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"GuidNullable<>null");
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
        public async Task ParsePredicateAsync_NestedProperty_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Parent.Id = 1 and Parent.Id >= 1");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_ValidProperties_Succeed()
        {
            string[] validProperties = { "String", "Integer" };
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"String = '' and Integer >= 1", validProperties: validProperties);
            Assert.True(result.Succeeded);
            result = await Interpreter.ParsePredicateAsync<TestEntity>($"Boolean = false and Double = 0", validProperties: validProperties);
            Assert.False(result.Succeeded);
            Assert.True(result.InvalidProperties.Contains("Boolean") && result.InvalidProperties.Contains("Double"));
        }

        [Fact]
        public async Task ParsePredicateAsync_Enum_Succeed()
        {
            var query = $"Option='{TestOptions.Option1}' and Option != 1 and Option = 0  and Option in ['{TestOptions.Option1}','{TestOptions.Option2}','{TestOptions.Option3}']";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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
        public async Task ParsePredicateAsync_EnumNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Option='{TestOptions.Option2}' and OptionNullable=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"OptionNullable='{TestOptions.Option1}'");
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
        public async Task ParsePredicateAsync_BooleanAndBooleanNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Boolean and BooleanNullable=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"BooleanNullable=true and Not(Boolean) and !Boolean and Boolean=false");
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
        public async Task ParsePredicateAsync_DateTimeAndVariableResolver_Succeed()
        {
            var query = $"DateTime='{DF.DateTime1}' and DateTime<'{DF.DateTime2}' and DateTime<='{DF.DateTime1}' and DateTime>='{DF.DateTime1}' and DateTime in ['{DF.DateTime1}','{DF.DateTime2}','{DF.DateTime3}']";
            var query2 = $"DateTime='{DF.DateTimeString1}' and DateTime<'{DF.DateTimeString2}' and DateTime<='{DF.DateTimeString1}' and DateTime>='{DF.DateTimeString1}' and DateTime in ['{DF.DateTimeString1}','{DF.DateTimeString2}','{DF.DateTimeString3}']";
            var query3 = $"DateTime.Year=now.year and DateTime.Year==utcnow.year";
            var query4 = $"DateTime.Year=$now.year and DateTime.Year==$utcnow.year";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and1_1 = await Interpreter.ParsePredicateAsync<TestEntity>(query2);
            var and1_2 = await Interpreter.ParsePredicateAsync<TestEntity>(query3);
            var and1_3 = await Interpreter.ParsePredicateAsync<TestEntity>(query4);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
            Assert.True(and1_1.Succeeded);
            Assert.True(and1_2.Succeeded);
            Assert.True(and1_3.Succeeded);
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
                value = context.Entities.Count(and1_3.Result);
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
        public async Task ParsePredicateAsync_DateTimeNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"DateTimeNullable='{DF.DateTimeString1}' and DateTimeNullable!=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"DateTimeNullable>'{DF.DateTimeString1}'");
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
        public async Task ParsePredicateAsync_DateTimeOffsetAndVariableResolver_Succeed()
        {
            var query = $"DateTimeOffset='{DF.DateTimeOffsetString1}' and DateTimeOffset<'{DF.DateTimeOffsetString2}' and DateTimeOffset<='{DF.DateTimeOffsetString1}' and DateTimeOffset>='{DF.DateTimeOffsetString1}' and DateTimeOffset in ['{DF.DateTimeOffsetString1}','{DF.DateTimeOffsetString2}','{DF.DateTimeOffsetString3}']";
            var query3 = $"DateTimeOffset.Year=now.year and DateTimeOffset.Year==utcnow.year";
            var and1 = await Interpreter.ParsePredicateAsync<TestEntity>(query);
            var and1_2 = await Interpreter.ParsePredicateAsync<TestEntity>(query3);
            var and2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&&"));
            var and3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "&"));
            var or1 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "or"));
            var or2 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "||"));
            var or3 = await Interpreter.ParsePredicateAsync<TestEntity>(query.Replace("and", "|"));
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

        [Fact]
        public async Task ParsePredicateAsync_DateTimeOffsetNullable_Succeed()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"DateTimeOffsetNullable='{DF.DateTimeOffsetString1}' and DateTimeOffsetNullable!=null");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"DateTimeOffsetNullable>'{DF.DateTimeOffsetString1}'");
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
        public async Task ParsePredicateAsync_ComplicatedGroup_Success()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"(Integer={DF.Integer1} or Double={DF.Double3}) and (Guid=='{DF.Guid1}' or DateTime='{DF.DateTime3}')");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(2, value);
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_NotOperatorAndNotFunction_Success()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"!Boolean");
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"!(Boolean=true)");
            var result3 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Boolean)");
            var result4 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Boolean=true)");
            var result5 = await Interpreter.ParsePredicateAsync<TestEntity>($"!(Integer={DF.Integer1})");
            var result6 = await Interpreter.ParsePredicateAsync<TestEntity>($"!(Integer in [{DF.Integer1}])");
            var result7 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Integer={DF.Integer1})");
            var result8 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Integer in [{DF.Integer1}])");
            var result9 = await Interpreter.ParsePredicateAsync<TestEntity>($"!Boolean = true");
            var result10 = await Interpreter.ParsePredicateAsync<TestEntity>($"!(Boolean=true) == true");
            var result11 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Boolean) == true");
            var result12 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Boolean=true)==true");
            var result13 = await Interpreter.ParsePredicateAsync<TestEntity>($"!Boolean and !(Integer={DF.Integer1})");
            var result14 = await Interpreter.ParsePredicateAsync<TestEntity>($"not(Boolean) and not(Integer={DF.Integer1})");
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            Assert.True(result3.Succeeded);
            Assert.True(result4.Succeeded);
            Assert.True(result5.Succeeded);
            Assert.True(result6.Succeeded);
            Assert.True(result7.Succeeded);
            Assert.True(result8.Succeeded);
            Assert.True(result9.Succeeded);
            Assert.True(result10.Succeeded);
            Assert.True(result11.Succeeded);
            Assert.True(result12.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result2.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result3.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result4.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result5.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result6.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result7.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result8.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result9.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result10.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result11.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result12.Result);
                Assert.Equal(4, value);
                value = context.Entities.Count(result13.Result);
                Assert.Equal(3, value);
                value = context.Entities.Count(result14.Result);
                Assert.Equal(3, value);
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_CustomVariableResolver_Success()
        {
            var result = await Interpreter.ParsePredicateAsync<TestEntity>("Integer=user.Id", new CustomVariableResolver());
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>("Integer=$user.Id", new CustomVariableResolver());
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public async Task ParsePredicateAsync_InOperatorAndCustomVariableResolver_Success()
        {
            var resolver = new CustomVariableResolver();
            var result = await Interpreter.ParsePredicateAsync<TestEntity>("Integer in [$user.Id]", resolver);
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>("Integer in userIds", resolver);
            var result3 = await Interpreter.ParsePredicateAsync<TestEntity>("Integer in $userIds", resolver);
            Assert.True(result.Succeeded);
            Assert.True(result2.Succeeded);
            Assert.True(result3.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result2.Result);
                Assert.Equal(1, value);
                value = context.Entities.Count(result3.Result);
                Assert.Equal(1, value);
            }
        }

        #endregion Expression Parser


        #region BuildPredicate

        [Fact]
        public async Task BuildPredicate_AllOperators_Success_WithoutExceptions()
        {
            // integer
            var exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.Equal, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.LessThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.LessThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.GreaterThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.GreaterThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.In, "1, 2, 4");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Integer", ComparisonOperator.In, "$now.month, 2, 4");
            // integer nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.Equal, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.LessThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.LessThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.GreaterThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.GreaterThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.In, "1, 2, 4");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("IntegerNullable", ComparisonOperator.In, "$now.year, 2, 4");

            // double
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.Equal, "1.0");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.LessThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.LessThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.GreaterThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.GreaterThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.In, "1.2, 2, 4");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Double", ComparisonOperator.In, "1.2, 2, $now.day");
            // double nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.Equal, "1.0");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.LessThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.LessThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.GreaterThan, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.GreaterThanOrEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DoubleNullable", ComparisonOperator.In, "1.2, 2, 4");

            // boolean
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Boolean", ComparisonOperator.Equal, "true");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Boolean", ComparisonOperator.NotEqual, "false");
            // boolean nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("BooleanNullable", ComparisonOperator.Equal, "true");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("BooleanNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("BooleanNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("BooleanNullable", ComparisonOperator.NotEqual, "false");


            // string
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.Equal, "1.0");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.StartsWith, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.EndsWith, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.Contains, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("String", ComparisonOperator.In, "'1.0','lsdf'");

            // Guid
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Guid", ComparisonOperator.Equal, "32F281EB-9973-4E78-86C0-3D7AEB791E6F");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Guid", ComparisonOperator.NotEqual, "32F281EB-9973-4E78-86C0-3D7AEB791E6F");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Guid", ComparisonOperator.In, "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'");
            // Guid nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("GuidNullable", ComparisonOperator.Equal, "32F281EB-9973-4E78-86C0-3D7AEB791E6F");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("GuidNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("GuidNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("GuidNullable", ComparisonOperator.NotEqual, "32F281EB-9973-4E78-86C0-3D7AEB791E6F");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("GuidNullable", ComparisonOperator.In, "'32F281EB-9973-4E78-86C0-3D7AEB791E6F', '32F281EB-9973-4E78-86C0-3D7AEE791E6F', '32F281EB-9973-4E78-86C0-3D7AEB791E65'");

            // Enum
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.Equal, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.Equal, "Option1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.NotEqual, "Option1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.In, "1, 2, 0");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Option", ComparisonOperator.In, "'Option1', 'Option2', 'Option3'");
            // Enum Nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.Equal, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.Equal, "Option1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.NotEqual, "1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.NotEqual, "Option1");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.In, "1, 2, 0");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("OptionNullable", ComparisonOperator.In, "'Option1', 'Option2', 'Option3'");

            // DateTime
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.Equal, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.NotEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.LessThan, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.LessThanOrEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.GreaterThan, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.GreaterThanOrEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.In, $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'");
            // DateTime nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.Equal, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.NotEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.LessThan, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.LessThanOrEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.GreaterThan, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.GreaterThanOrEqual, DF.DateTimeString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeNullable", ComparisonOperator.In, $"'{DF.DateTimeString1}', '{DF.DateTimeString2}', '{DF.DateTimeString3}'");

            // DateTimeOffset
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.Equal, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.NotEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.LessThan, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.LessThanOrEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.GreaterThan, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.GreaterThanOrEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffset", ComparisonOperator.In, $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'");
            // DateTimeOffset nullable
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.Equal, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.Equal, "");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.Equal, null);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.NotEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.LessThan, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.LessThanOrEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.GreaterThan, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.GreaterThanOrEqual, DF.DateTimeOffsetString1);
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTimeOffsetNullable", ComparisonOperator.In, $"'{DF.DateTimeOffsetString1}', '{DF.DateTimeOffsetString2}', '{DF.DateTimeOffsetString3}'");

            // nested property
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("Parent.Id", ComparisonOperator.Equal, "1");

            // variable resolver
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime", ComparisonOperator.Equal, "now");
            exp = await Interpreter.BuildPredicateAsync<TestEntity>("DateTime.Year", ComparisonOperator.Equal, "now.year");

            // actually just test without exceptions
            Assert.NotNull(exp);
        }

        [Fact]
        public async Task ParsePredicateAsync_PropertyMapping_Succeed()
        {
            var propertyMapping = new Dictionary<string, string> { ["Number"] = "Integer", ["NumberNullable"] = "IntegerNullable" };
            var validProperties = new[] { "Number", "NumberNullable" };
            var result = await Interpreter.ParsePredicateAsync<TestEntity>($"Number={DF.Integer2} and NumberNullable=null", validProperties: validProperties, propertyMapping: propertyMapping);
            var result2 = await Interpreter.ParsePredicateAsync<TestEntity>($"NumberNullable>0", validProperties: validProperties, propertyMapping: propertyMapping);
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

        #endregion BuildPredicate
    }
}
