using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionReplacementTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();
        private static readonly ExpressionVariableSchema Variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("utcnow", typeof(DateTime))
        });
        private static readonly ExpressionValueSuggestionSchema Hints = new ExpressionValueSuggestionSchema(Schema,
            new[]
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Status", new[]
                {
                    new ExpressionValueSuggestion(FilterValue.String("Active"))
                })
            });

        [Theory]
        [InlineData("Sta|Tus = 'Active' and Price > 1", "Status", 0, 6, "Status", "Status = 'Active' and Price > 1")]
        [InlineData("Customer.Na|ZZ = 'A'", "Customer.Name", 0, 13, "Customer.Name", "Customer.Name = 'A'")]
        [InlineData("Customer.|", "Customer.Name", 0, 9, "Customer.Name", "Customer.Name")]
        [InlineData("CreatedAt > $u|ZZ", "utcnow", 12, 4, "$utcnow", "CreatedAt > $utcnow")]
        [InlineData("CreatedAt > $|", "utcnow", 12, 1, "$utcnow", "CreatedAt > $utcnow")]
        [InlineData("Price >|= 1", ">=", 6, 2, ">=", "Price >= 1")]
        [InlineData("Price not   i|n [1]", "not in", 6, 8, "not in", "Price not in [1]")]
        [InlineData("Status = 'Ac|ZZ' and Price > 1", "'Active'", 9, 6, "'Active'", "Status = 'Active' and Price > 1")]
        [InlineData("Status = \"Ac|", "\"Active\"", 9, 3, "\"Active\"", "Status = \"Active\"")]
        [InlineData("Enabled = fa|ZZ", "false", 10, 4, "false", "Enabled = false")]
        [InlineData("Price = nu|ZZ", "null", 8, 4, "null", "Price = null")]
        [InlineData("Price > 1|", "and", 9, 0, " and", "Price > 1 and")]
        [InlineData("not (Price > 1|", ")", 14, 0, ")", "not (Price > 1)")]
        [InlineData("Price in |", "[", 9, 0, "[", "Price in [")]
        [InlineData("Status in ['Active'|", ",", 19, 0, ",", "Status in ['Active',")]
        public void ItemsApplyAsExactOriginalSourceSlices(
            string marked, string label, int start, int length, string insertion, string expected)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var result = Interpreter.CompleteExpression(text, position, Schema, Variables, Hints);
            var item = Assert.Single(result.Items.Where(candidate => candidate.Label == label));
            Assert.Equal(start, item.Start);
            Assert.Equal(length, item.Length);
            Assert.Equal(insertion, item.InsertionText);
            Assert.Equal(expected, Apply(text, item));
            Assert.All(result.Items, candidate =>
            {
                Assert.InRange(candidate.Start, 0, text.Length);
                Assert.InRange(candidate.Length, 0, text.Length - candidate.Start);
                Assert.StartsWith(text.Substring(0, candidate.Start), Apply(text, candidate));
                Assert.EndsWith(text.Substring(candidate.Start + candidate.Length), Apply(text, candidate));
            });
        }

        [Theory]
        [InlineData("Status in ['Active'|]", "]")]
        [InlineData("Status in {'Active'|}", "}")]
        [InlineData("Status in ('Active'|)", ")")]
        [InlineData("not (Price > 1|)", ")")]
        [InlineData("Status in ['Active'|, 'Pending']", ",")]
        public void SurvivingPunctuationIsNeverDuplicated(string marked, string punctuation)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            Assert.DoesNotContain(Interpreter.CompleteExpression(text, position, Schema, Variables, Hints).Items,
                item => item.InsertionText == punctuation);
        }

        [Fact]
        public void MultilineSupplementaryTextAndWhitespaceGapsRemainExact()
        {
            const string text = "Status = '\U0001f600'\r\nand StaTus = 'Active'";
            var start = text.IndexOf("StaTus", StringComparison.Ordinal);
            var item = Assert.Single(Interpreter.CompleteExpression(text, start + 3, Schema, Variables, Hints)
                .Items.Where(candidate => candidate.Label == "Status"));
            Assert.Equal(start, item.Start);
            Assert.Equal(6, item.Length);
            Assert.Equal("Status = '\U0001f600'\r\nand Status = 'Active'", Apply(text, item));
            const string gap = "Price  ";
            item = Interpreter.CompleteExpression(gap, gap.Length, Schema).Items.First(candidate => candidate.Label == "=");
            Assert.Equal(gap.Length, item.Start);
            Assert.Equal(0, item.Length);
            Assert.Equal("Price  =", Apply(gap, item));
        }

        private static string Apply(string text, ExpressionCompletionItem item) =>
            text.Substring(0, item.Start) + item.InsertionText + text.Substring(item.Start + item.Length);

        public class Product
        {
            public decimal Price { get; set; }
            public string Status { get; set; }
            public bool Enabled { get; set; }
            public DateTime CreatedAt { get; set; }
            public Customer Customer { get; set; }
        }

        public class Customer { public string Name { get; set; } }
    }
}
