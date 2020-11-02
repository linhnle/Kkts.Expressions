using System;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class InterpreterTest
    {
        #region Expresion Parser
        [Fact]
        public void ParsePredicate_Interger_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Integer={DF.Interger1} and Integer != 0 and Integer<{DF.Interger2} and Integer<={DF.Interger1} and Integer>={DF.Interger1} and Integer>0 and Integer in [{DF.Interger1},{DF.Interger2},{DF.Interger3}]");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void ParsePredicate_IntergerNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Integer={DF.Interger2} and IntegerNullable=null");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void ParsePredicate_Double_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Double={DF.Double3} and Double != 0.0 and Double<{DF.DoubleN} and Double<={DF.Double3} and Double>={DF.Double1} and Double>0.0 and Double in [{DF.Double1},{DF.Double2},{DF.Double3},{DF.DoubleN}]");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void ParsePredicate_DoubleNullable_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"Double={DF.Double2} and DoubleNullable=null");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }

        [Fact]
        public void ParsePredicate_String_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"String='{DF.String1}' and String != null and String @* 'N' and String *@ '1' and String @ 'm' and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.DoubleN}']");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }


        [Fact]
        public void ParsePredicate_String_ComparisonFunction_Succeed()
        {
            var result = Interpreter.ParsePredicate<TestEntity>($"String='{DF.String1}' and String != null and String.startsWith(String) and String.endsWith('1') and String.contains('m') and '${DF.String1}'.contains(String) and String in ['{DF.String1}','{DF.String2}','{DF.String3}','{DF.StringN}']");
            Assert.True(result.Succeeded);
            using (var context = DF.GetContext())
            {
                var value = context.Entities.Count(result.Result);
                Assert.Equal(1, value);
            }
        }
        #endregion Expresion Parser
    }
}
