using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionValueOperatorTest
    {
        private static ExpressionSchema Schema() => new QuerySchema<Product>()
            .Field("Price", product => product.Price, allowedOperators: new[]
            {
                ComparisonOperator.Equal, ComparisonOperator.NotEqual,
                ComparisonOperator.GreaterThan, ComparisonOperator.GreaterThanOrEqual,
                ComparisonOperator.LessThan, ComparisonOperator.LessThanOrEqual, ComparisonOperator.In
            })
            .Field("Name", product => product.Name)
            .Field("OtherPrice", product => product.OtherPrice, allowedOperators: new[] { ComparisonOperator.Equal })
            .Field("Enabled", product => product.Enabled)
            .Field("State", product => product.State)
            .Build();

        [Fact]
        public void ConfiguredOperatorsHaveCanonicalOrderAndIntersectContributingFields()
        {
            var schema = Schema();
            Assert.Equal(new[] { "=", "!=", ">", ">=", "<", "<=", "in" },
                Complete("Price |", schema).Items.Select(item => item.InsertionText));
            Assert.Equal("=", Assert.Single(Complete("Price + OtherPrice |", schema).Items).InsertionText.Trim());
            Assert.Empty(Complete("Price con|", schema).Items);
            Assert.Equal("Name.contains", Assert.Single(Complete("Name.con|", schema).Items).InsertionText);
        }

        [Fact]
        public void PartialOperatorsReplaceWholeTokenAndRespectSurvivingOperand()
        {
            var schema = Schema();
            var item = Assert.Single(Complete("Price >|= 1", schema).Items.Where(candidate => candidate.Label == ">="));
            Assert.Equal(6, item.Start);
            Assert.Equal(2, item.Length);
            Assert.Equal(">=", item.InsertionText);
            Assert.Empty(Complete("Price >|= 'bad'", schema).Items);
            Assert.Equal("not in", Assert.Single(Complete("OtherPrice not i|",
                ExpressionSchema.FromType<Product>()).Items).InsertionText);
        }

        [Fact]
        public void DeclaredHintsRemainAdvisoryDeduplicateAndKeepFirstPresentation()
        {
            var schema = Schema();
            var hints = Hints(schema,
                new ExpressionValueSuggestion(FilterValue.String("Active"), "Preferred active", "First"),
                new ExpressionValueSuggestion(FilterValue.String("Active"), "Duplicate", "Second"),
                new ExpressionValueSuggestion(FilterValue.String("Pending")),
                new ExpressionValueSuggestion(FilterValue.String("Closed")));
            var items = Complete("Name = |", schema, hints).Items;
            Assert.Equal(new[] { "'Active'", "'Closed'", "'Pending'", "null" }, items.Select(item => item.InsertionText));
            Assert.Equal("Preferred active", items[0].Label);
            Assert.Equal("First", items[0].Description);
            Assert.True(Interpreter.AnalyzeExpression("Name = 'Unlisted'", typeof(Product), schema).IsSemanticallyValid);
        }

        [Theory]
        [InlineData("Name = 'Ac|ZZ' and Price > 1", "'Active'", 7, 6)]
        [InlineData("Name = \"Ac|", "\"Active\"", 7, 3)]
        [InlineData("Name in ['Ac|ZZ', 'Pending']", "'Active'", 9, 6)]
        public void QuotesRetainStyleAndReplaceWholeTokens(string marked, string insertion, int start, int length)
        {
            var schema = Schema();
            var item = Assert.Single(Complete(marked, schema,
                Hints(schema, new ExpressionValueSuggestion(FilterValue.String("Active")))).Items);
            Assert.Equal(insertion, item.InsertionText);
            Assert.Equal(start, item.Start);
            Assert.Equal(length, item.Length);
            Assert.Equal(typeof(string), item.TypeInfo.ClrType);
        }

        [Fact]
        public void GeneratedValuesFollowNullBooleanEnumAndMembershipSemantics()
        {
            var schema = Schema();
            Assert.Equal(new[] { "false", "null", "true" },
                Complete("Enabled = |", schema).Items.Select(item => item.InsertionText));
            Assert.Equal("false", Assert.Single(Complete("Enabled = fa|", schema).Items).InsertionText);
            Assert.Equal(new[] { "'Active'", "'Pending'", "null" },
                Complete("State = |", schema).Items.Select(item => item.InsertionText));
            Assert.Empty(Complete("Price in [|", schema).Items);
            var hints = Hints(schema, new ExpressionValueSuggestion(FilterValue.String("$private")));
            Assert.DoesNotContain(Complete("Name in [|", schema, hints).Items, item => item.Label.Contains("$"));
            Assert.Contains(Complete("Name = |", schema, hints).Items, item => item.InsertionText == "'$private'");
            Assert.DoesNotContain(Complete("Name.contains(|", schema).Items, item => item.InsertionText == "null");
        }

        internal static ExpressionCompletionResult Complete(
            string marked, ExpressionSchema schema, ExpressionValueSuggestionSchema hints = null)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var syntax = Interpreter.AnalyzeExpression(text);
            var slot = ExpressionCompletionContext.Recognize(text, position, schema, null, syntax);
            var candidates = new ExpressionCompletionCandidates(text, schema, null, syntax, slot);
            candidates.AddOperators();
            candidates.AddValues(hints);
            return candidates.Shape();
        }

        private static ExpressionValueSuggestionSchema Hints(ExpressionSchema schema,
            params ExpressionValueSuggestion[] values) => new ExpressionValueSuggestionSchema(schema,
                new[] { new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Name", values) });

        public class Product
        {
            public decimal Price { get; set; }
            public decimal OtherPrice { get; set; }
            public string Name { get; set; }
            public bool Enabled { get; set; }
            public State State { get; set; }
        }

        public enum State { Active, Pending }
    }
}
