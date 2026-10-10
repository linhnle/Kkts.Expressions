using System;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionOperandTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();
        private static readonly ExpressionVariableSchema Variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("amount", typeof(double)),
            new ExpressionVariableDefinition("text", typeof(string)),
            new ExpressionVariableDefinition("amounts", typeof(decimal[]))
        });

        [Theory]
        [InlineData("Price + Quantity", typeof(decimal), "Price", "Quantity")]
        [InlineData("Name + Quantity", typeof(string), "Name", "Quantity")]
        [InlineData("Price + Price", typeof(decimal), "Price", null)]
        [InlineData("$amount + Quantity", typeof(double), "Quantity", null)]
        public void OperandDescriptorsReuseBindingAndContributingFieldIdentity(
            string text, Type expectedType, string firstField, string secondField)
        {
            var binder = Binder(text);
            Assert.True(binder.TryDescribeOperand(0, Interpreter.AnalyzeExpression(text).Tokens.Count, out var operand));
            Assert.Equal(expectedType, operand.Type);
            Assert.Equal(secondField == null ? new[] { firstField } : new[] { firstField, secondField }, operand.EntityPaths);
            Assert.False(operand.IsField);
            Assert.False(operand.IsVariable);
        }

        [Theory]
        [InlineData("Price", "=", "$amount", true)]
        [InlineData("Price", ">", "$amount", true)]
        [InlineData("Price + Quantity", ">", "$amount", false)]
        [InlineData("Price", "=", "null", true)]
        [InlineData("Price", "in", "[null]", false)]
        [InlineData("Price", "in", "[$text]", true)]
        [InlineData("Price", "in", "$amounts", true)]
        [InlineData("Quantity", "in", "$amounts", false)]
        [InlineData("Name", "contains", "'a'", true)]
        [InlineData("Price", "contains", "'a'", false)]
        [InlineData("Name", "+", "Quantity", true)]
        public void BinaryProbesReuseTheAnalyzerWithoutWholeExpressionParsing(
            string left, string op, string right, bool compatible)
        {
            var text = left + " " + right;
            var syntax = Interpreter.AnalyzeExpression(text);
            var boundary = Interpreter.AnalyzeExpression(left).Tokens.Count;
            var binder = Binder(text);
            Assert.True(binder.TryDescribeOperand(0, boundary, out var leftOperand));
            Assert.True(binder.TryDescribeOperand(boundary, syntax.Tokens.Count, out var rightOperand,
                membershipOperand: op == "in"));
            Assert.Equal(compatible, binder.TryDescribeBinary(op, leftOperand, rightOperand, out var result));
            if (compatible) Assert.NotNull(result);
            else Assert.Null(result);
        }

        [Fact]
        public void FailedProbeDoesNotPoisonNextProbeOrExposeDeniedMetadata()
        {
            const string text = "Secret Price";
            var schema = ExpressionSchema.FromType<Product>(properties: new[]
            {
                new ExpressionPropertyDefinition("Secret", typeof(decimal), canQuery: false)
            });
            var binder = new ExpressionSemanticAnalyzer(text, schema, Variables,
                Interpreter.AnalyzeExpression(text), completionMode: true);
            Assert.False(binder.TryDescribeOperand(0, 1, out var denied));
            Assert.Null(denied);
            Assert.True(binder.TryDescribeOperand(1, 2, out var allowed));
            Assert.True(allowed.IsField);
            Assert.Equal(new[] { "Price" }, allowed.EntityPaths);
        }

        [Fact]
        public void LongUnaryChainsUseIterativeCompletionBinding()
        {
            var text = new string('!', 1000) + "Enabled";
            var syntax = Interpreter.AnalyzeExpression(text);
            var binder = Binder(text);
            Assert.True(binder.TryDescribeOperand(0, syntax.Tokens.Count, out var operand));
            Assert.Equal(typeof(bool), operand.Type);
            Assert.Equal(new[] { "Enabled" }, operand.EntityPaths);
        }

        private static ExpressionSemanticAnalyzer Binder(string text) =>
            new ExpressionSemanticAnalyzer(text, Schema, Variables, Interpreter.AnalyzeExpression(text),
                completionMode: true);

        public sealed class Product
        {
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public decimal Secret { get; set; }
        }
    }
}
