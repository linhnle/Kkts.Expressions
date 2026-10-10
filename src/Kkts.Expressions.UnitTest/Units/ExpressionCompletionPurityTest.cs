using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionPurityTest
    {
        [Fact]
        public void HintBindingAndCompletionNeverExecuteSelectorsConversionsOrEnumerators()
        {
            var schema = new QuerySchema<InstrumentedProduct>()
                .Field("total", product => InstrumentedProduct.Select(product))
                .Field("hidden", product => product.Amount, canFilter: false)
                .Build();
            var hints = new ExpressionValueSuggestionSchema(schema, new[]
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("total", new[]
                {
                    new ExpressionValueSuggestion(FilterValue.Number("12.5"))
                }),
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("hidden", new[]
                {
                    new ExpressionValueSuggestion(FilterValue.Number("42"), "Private label", "Private hint")
                })
            });
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType<InstrumentedProduct>("product"),
                new ExpressionVariableDefinition("values", typeof(ThrowingEnumerable)),
                new ExpressionVariableDefinition("custom", typeof(ThrowingConvertible))
            });
            foreach (var text in new[]
            {
                "", "total > 1", "total = ", "total > $product.", "total in $",
                "hidden = ", "total = ) and total", new string('!', 8193)
            })
            {
                var result = Interpreter.CompleteExpression(text, text.Length, schema, variables, hints);
                var serialized = JsonSerializer.Serialize(new
                {
                    Items = result.Items.Select(item => new { item.Label, item.Description, item.InsertionText }),
                    result.Diagnostics
                });
                Assert.DoesNotContain("Private label", serialized);
                Assert.DoesNotContain("Private hint", serialized);
            }
            Assert.Equal(0, InstrumentedProduct.GetterCalls);
            Assert.Equal(0, InstrumentedProduct.SelectorCalls);
            Assert.Equal(0, ThrowingEnumerable.EnumerationCalls);
            Assert.Equal(0, ThrowingConvertible.ConversionCalls);

            var predicate = new ExpressionQueryContext(schema, new QueryPolicy())
                .ParsePredicate<InstrumentedProduct>("total > 1");
            Assert.True(predicate.Succeeded);
            Assert.True(predicate.Result.Compile()(new InstrumentedProduct()));
            Assert.Equal(1, InstrumentedProduct.GetterCalls);
            Assert.Equal(1, InstrumentedProduct.SelectorCalls);
            Assert.Throws<InvalidOperationException>(() => new ThrowingEnumerable().GetEnumerator());
            Assert.Throws<InvalidOperationException>(() => new ThrowingConvertible().ToDecimal(null));
            Assert.Equal(1, ThrowingEnumerable.EnumerationCalls);
            Assert.Equal(1, ThrowingConvertible.ConversionCalls);
            InstrumentedProduct.GetterCalls = InstrumentedProduct.SelectorCalls = 0;
            ThrowingEnumerable.EnumerationCalls = ThrowingConvertible.ConversionCalls = 0;
        }

        [Fact]
        public void CompletionDoesNotExecuteApplicationCodeOrDiscloseRestrictedMetadata()
        {
            var schema = new QuerySchema<Product>()
                .Field("total", product => product.SecretPrice * 2, description: "Public total")
                .Field("blocked", product => product.SecretPrice, canFilter: false, description: "classified description")
                .Build();
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType<VariableProduct>("product"),
                new ExpressionVariableDefinition("values", typeof(ThrowingEnumerable)),
                new ExpressionVariableDefinition("custom", typeof(ThrowingConvertible))
            });
            foreach (var marked in new[] { "|", "tot|al > 1", "total > $|", "blocked |", "'random| text'", "total in $|" })
            {
                var position = marked.IndexOf('|');
                var text = marked.Remove(position, 1);
                var result = Interpreter.CompleteExpression(text, position, schema, variables);
                var serialized = JsonSerializer.Serialize(new
                {
                    Items = result.Items.Select(item => new
                    {
                        item.Label, item.InsertionText, item.Description,
                        Type = item.TypeInfo?.ClrType?.FullName
                    }),
                    result.Diagnostics
                });
                Assert.DoesNotContain("SecretPrice", serialized);
                Assert.DoesNotContain("classified description", serialized);
                Assert.DoesNotContain("\"blocked\"", serialized);
            }
            var oversized = new string('!', 8193);
            Assert.Equal(ExpressionCompletionStatus.LimitExceeded,
                Interpreter.CompleteExpression(oversized, oversized.Length, schema, variables).Status);
            Assert.Equal(0, Product.GetterCalls);
            Assert.Equal(0, ThrowingEnumerable.EnumerationCalls);
            Assert.Equal(0, ThrowingConvertible.ConversionCalls);
            var predicate = new ExpressionQueryContext(schema, new QueryPolicy()).ParsePredicate<Product>("total > 1");
            Assert.True(predicate.Succeeded);
            Assert.True(predicate.Result.Compile()(new Product()));
            Assert.Equal(1, Product.GetterCalls);
            Product.GetterCalls = 0;
        }

        public class Product
        {
            public static int GetterCalls;
            public decimal SecretPrice { get { ++GetterCalls; return 5; } }
        }

        public class InstrumentedProduct
        {
            public static int GetterCalls;
            public static int SelectorCalls;
            public decimal Amount { get { ++GetterCalls; return 5; } }
            public static decimal Select(InstrumentedProduct product)
            {
                ++SelectorCalls;
                return product.Amount * 2;
            }
        }

        public class VariableProduct
        {
            public decimal Amount => throw new InvalidOperationException("Variable getter must not execute.");
        }

        public class ThrowingEnumerable : IEnumerable<int>
        {
            public static int EnumerationCalls;
            public IEnumerator<int> GetEnumerator() { ++EnumerationCalls; throw new InvalidOperationException(); }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public class ThrowingConvertible : IConvertible
        {
            public static int ConversionCalls;
            private static Exception Fail() { ++ConversionCalls; return new InvalidOperationException(); }
            public TypeCode GetTypeCode() => throw Fail();
            public bool ToBoolean(IFormatProvider provider) => throw Fail();
            public byte ToByte(IFormatProvider provider) => throw Fail();
            public char ToChar(IFormatProvider provider) => throw Fail();
            public DateTime ToDateTime(IFormatProvider provider) => throw Fail();
            public decimal ToDecimal(IFormatProvider provider) => throw Fail();
            public double ToDouble(IFormatProvider provider) => throw Fail();
            public short ToInt16(IFormatProvider provider) => throw Fail();
            public int ToInt32(IFormatProvider provider) => throw Fail();
            public long ToInt64(IFormatProvider provider) => throw Fail();
            public sbyte ToSByte(IFormatProvider provider) => throw Fail();
            public float ToSingle(IFormatProvider provider) => throw Fail();
            public string ToString(IFormatProvider provider) => throw Fail();
            public object ToType(Type conversionType, IFormatProvider provider) => throw Fail();
            public ushort ToUInt16(IFormatProvider provider) => throw Fail();
            public uint ToUInt32(IFormatProvider provider) => throw Fail();
            public ulong ToUInt64(IFormatProvider provider) => throw Fail();
        }
    }
}
