using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionValueSuggestionSchemaTest
    {
        private static ExpressionSchema Schema() => new QuerySchema<Product>()
            .Field("Status", product => product.Status)
            .Field("Price", product => product.Price)
            .Build();

        private static KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>> Group(
            string name, params ExpressionValueSuggestion[] hints) =>
            new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>(name, hints);

        [Fact]
        public void BindingOwnsReadOnlyMetadataAndEnforcesSchemaIdentity()
        {
            var schema = Schema();
            var hints = new List<ExpressionValueSuggestion> { new ExpressionValueSuggestion(FilterValue.String("Active"), "Active status") };
            var fields = new List<KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>>
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Status", hints)
            };
            var bound = new ExpressionValueSuggestionSchema(schema, fields);
            hints.Clear();
            fields.Clear();
            var hint = Assert.Single(bound.Fields["status"]);
            Assert.Equal("Active status", hint.Label);
            Assert.Equal("", hint.Description);
            bound.ValidateSchema(schema);
            Assert.Throws<ArgumentException>(() => bound.ValidateSchema(Schema()));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionValueSuggestion>)bound.Fields["Status"]).Clear());
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, IReadOnlyList<ExpressionValueSuggestion>>)bound.Fields).Clear());
            Assert.All(typeof(ExpressionValueSuggestion).GetProperties(), property => Assert.False(property.CanWrite));
        }

        [Fact]
        public void BindingRejectsInvalidFieldsGroupsAndKinds()
        {
            var schema = Schema();
            Assert.Throws<ArgumentNullException>(() => new ExpressionValueSuggestionSchema(null, Array.Empty<KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>>()));
            Assert.Throws<ArgumentNullException>(() => new ExpressionValueSuggestionSchema(schema, null));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(schema, new[] { Group("Unknown") }));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(schema, new[] { Group("Status"), Group("STATUS") }));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(schema, new[] { Group("Status", null) }));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(schema, new[] { Group("Status", new ExpressionValueSuggestion[] { null }) }));
            Assert.Throws<ArgumentNullException>(() => new ExpressionValueSuggestion(null));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestion(FilterValue.Variable("value")));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestion(FilterValue.Collection(Array.Empty<FilterValue>())));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(schema,
                new[] { Group("Price", new ExpressionValueSuggestion(FilterValue.String("not a price"))) }));
        }

        [Theory]
        [InlineData("1e3")]
        [InlineData("99999999999999999999999999999")]
        [InlineData("0.1234567890123456789012345678")]
        public void BindingRejectsNumbersNotLosslessInExpressionGrammar(string number)
        {
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(Schema(),
                new[] { Group("Price", new ExpressionValueSuggestion(FilterValue.Number(number))) }));
        }

        [Fact]
        public void NullIsBindableForLiftedComparisonsAndHintsDoNotRestrictAnalysis()
        {
            var schema = Schema();
            var nullHint = new ExpressionValueSuggestion(FilterValue.Null);
            var bound = new ExpressionValueSuggestionSchema(schema, new[]
            {
                Group("Price", nullHint),
                Group("Status",
                    new ExpressionValueSuggestion(FilterValue.String("Active"), "First"),
                    new ExpressionValueSuggestion(FilterValue.String("Active"), "Second"))
            });
            Assert.Same(nullHint, Assert.Single(bound.Fields["Price"]));
            Assert.True(Interpreter.AnalyzeExpression<Product>("Price = null", schema).IsSemanticallyValid);
            Assert.True(Interpreter.AnalyzeExpression<Product>("Status = 'Unlisted'", schema).IsSemanticallyValid);
            Assert.Equal(new[] { "First", "Second" }, bound.Fields["Status"].Select(hint => hint.Label));
        }

        [Fact]
        public void BindingValidatesResultTypeWithoutExecutingSelectorOrGetter()
        {
            Product.Reads = 0;
            var schema = Schema();
            new ExpressionValueSuggestionSchema(schema,
                new[] { Group("Price", new ExpressionValueSuggestion(FilterValue.Number("12.5"))) });
            Assert.Equal(0, Product.Reads);
            Assert.Throws<InvalidOperationException>(() => new Product().Price);
            Assert.Equal(1, Product.Reads);
        }

        [Theory]
        [InlineData("Active")]
        [InlineData("O'Brien")]
        [InlineData("double \"quote\"")]
        [InlineData("a\\path")]
        [InlineData("line\r\nbreak")]
        [InlineData("\\'")]
        [InlineData("\"\\'\"")]
        public void CodecRoundTripsBothQuoteStylesThroughExistingSyntaxAndRuntime(string value)
        {
            foreach (var quote in new[] { '\'', '"' })
            {
                var hint = FilterValue.String(value);
                Assert.True(ExpressionLiteralCodec.TryEncode(hint, quote, false, out var text, out var literal, out var type));
                Assert.Equal(typeof(string), type);
                Assert.Equal(value, literal);
                Assert.Equal(value, ExpressionLiteralCodec.DecodeString(text.Substring(1, text.Length - 2), quote));
                Assert.True(Interpreter.AnalyzeExpression("Status = " + text).IsComplete);
                var predicate = Interpreter.ParsePredicate<LiteralProduct>("Status = " + text);
                Assert.True(predicate.Succeeded, predicate.Exception?.ToString());
                Assert.True(predicate.Result.Compile()(new LiteralProduct { Status = value }));
            }
        }

        [Fact]
        public void CodecRejectsTrailingBackslashAndMembershipVariableAmbiguity()
        {
            foreach (var quote in new[] { '\'', '"' })
                Assert.False(ExpressionLiteralCodec.TryEncode(FilterValue.String("path\\"), quote, false, out _, out _, out _));
            Assert.Throws<ArgumentException>(() => new ExpressionValueSuggestionSchema(Schema(),
                new[] { Group("Status", new ExpressionValueSuggestion(FilterValue.String("path\\"))) }));
            var dollar = FilterValue.String("$notAVariable");
            Assert.True(ExpressionLiteralCodec.TryEncode(dollar, '\'', false, out _, out _, out _));
            Assert.False(ExpressionLiteralCodec.TryEncode(dollar, '\'', true, out _, out _, out _));
        }

        [Theory]
        [InlineData("==", "=")]
        [InlineData("<>", "!=")]
        [InlineData("CONTAIN", "contains")]
        [InlineData("startwith", "startswith")]
        [InlineData("endwith", "endswith")]
        public void CanonicalSpellingRetainsExistingExchangeBehavior(string source, string expected) =>
            Assert.Equal(expected, ExpressionLiteralCodec.CanonicalOperator(source));

        public sealed class Product
        {
            public static int Reads;
            public decimal Price
            {
                get { Reads++; throw new InvalidOperationException("Getter must not execute during metadata binding."); }
            }
            public string Status { get; set; }
        }

        public sealed class LiteralProduct
        {
            public string Status { get; set; }
        }
    }
}
