using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionDiscoveryBudgetTest
    {
        [Theory]
        [InlineData(4096, false)]
        [InlineData(4097, true)]
        public void DescriptorVisitsIncludeRejectedFieldsAndStopAtTheCeiling(int count, bool truncated)
        {
            var builder = new QuerySchema<Product>();
            for (var index = 0; index < count; ++index)
                builder.Field("field" + index, product => product.Price, canFilter: index % 2 == 0);
            var schema = builder.Build();
            var syntax = Interpreter.AnalyzeExpression("");
            var slot = ExpressionCompletionContext.Recognize("", 0, schema, null, syntax);
            var candidates = new ExpressionCompletionCandidates("", schema, null, syntax, slot);
            candidates.AddFields();
            Assert.Equal(4096, candidates.DescriptorVisits);
            Assert.Equal(truncated, candidates.IsTruncated);
            Assert.All(candidates.Items, item => Assert.Equal(0, int.Parse(item.InsertionText.Substring(5)) % 2));
            var result = candidates.Shape(new ExpressionCompletionOptions(200));
            Assert.Equal(200, result.Items.Count);
            Assert.True(result.IsIncomplete);
        }

        [Theory]
        [InlineData(1, 1, true)]
        [InlineData(50, 50, true)]
        [InlineData(200, 51, false)]
        public void ResultCapsAreAfterDeduplicationAndStableOrdering(int maximum, int expected, bool incomplete)
        {
            var schema = ExpressionSchema.FromType<Product>();
            var variables = new ExpressionVariableSchema(Enumerable.Range(0, 51)
                .Reverse().Select(index => new ExpressionVariableDefinition("value" + index.ToString("D2"), typeof(decimal))));
            const string text = "Price > $";
            var syntax = Interpreter.AnalyzeExpression(text);
            var slot = ExpressionCompletionContext.Recognize(text, text.Length, schema, variables, syntax);
            var candidates = new ExpressionCompletionCandidates(text, schema, variables, syntax, slot);
            candidates.AddVariables();
            var result = candidates.Shape(new ExpressionCompletionOptions(maximum));
            Assert.Equal(expected, result.Items.Count);
            Assert.Equal(incomplete, result.IsIncomplete);
            Assert.Equal("$value00", result.Items[0].InsertionText);
            Assert.Equal(result.Items.Select(item => item.InsertionText),
                candidates.Shape(new ExpressionCompletionOptions(maximum)).Items.Select(item => item.InsertionText));
        }

        [Fact]
        public void FieldAliasesAndCaseSensitiveVariableRootsRemainDistinctAndDeterministic()
        {
            var schema = ExpressionSchema.FromType<Product>(
                validProperties: new[] { "priceAlias", "Price" },
                propertyMapping: new Dictionary<string, string> { ["priceAlias"] = "Price" });
            var first = Interpreter.CompleteExpression("Pri", 3, schema);
            Assert.Equal(new[] { "Price", "priceAlias" }, first.Items.Select(item => item.InsertionText));
            Assert.Equal(first.Items.Select(item => item.InsertionText),
                Interpreter.CompleteExpression("Pri", 3, schema).Items.Select(item => item.InsertionText));
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("Value", typeof(decimal)),
                new ExpressionVariableDefinition("value", typeof(decimal))
            });
            Assert.Equal(new[] { "$Value", "$value" },
                Interpreter.CompleteExpression("Price > $", 9, schema, variables).Items.Select(item => item.InsertionText));
            Assert.Equal("$value", Assert.Single(
                Interpreter.CompleteExpression("Price > $v", 10, schema, variables).Items).InsertionText);
        }

        [Fact]
        public void DescriptorBudgetIsSharedAcrossRejectedFieldsAndVariableCandidates()
        {
            var builder = new QuerySchema<Product>();
            for (var index = 0; index < 4095; ++index)
                builder.Field("hidden" + index, product => product.Price, canFilter: false);
            var schema = builder.Build();
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("visible", typeof(decimal)),
                new ExpressionVariableDefinition("unvisited", typeof(decimal))
            });
            var syntax = Interpreter.AnalyzeExpression("");
            var slot = ExpressionCompletionContext.Recognize("", 0, schema, variables, syntax);
            var candidates = new ExpressionCompletionCandidates("", schema, variables, syntax, slot);
            candidates.AddFields();
            candidates.AddVariables();
            Assert.Equal(4096, candidates.DescriptorVisits);
            var result = candidates.Shape();
            Assert.True(result.IsIncomplete);
            Assert.Equal("$visible", Assert.Single(result.Items).InsertionText);
            Assert.Equal(ExpressionCompletionStatus.Available, result.Status);
        }

        public class Product { public decimal Price { get; set; } }
    }
}
