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
        #endregion Expresion Parser
    }
}
