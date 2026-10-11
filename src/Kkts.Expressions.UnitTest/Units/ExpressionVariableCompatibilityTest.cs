using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionVariableCompatibilityTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();

        [Theory]
        [InlineData("Price > $value", typeof(double), 1.5d)]
        [InlineData("$value < Price", typeof(double), 1.5d)]
        [InlineData("Price = $value", typeof(int), 2)]
        [InlineData("Price in [$value]", typeof(double), 2d)]
        [InlineData("Price in [$value]", typeof(string), "2")]
        [InlineData("Price in {$value}", typeof(string), "2")]
        [InlineData("Price in ($value)", typeof(string), "2")]
        [InlineData("Price = $value", typeof(bool), true)]
        public void MetadataAcceptsContextualConversionsWithoutResolvingValues(
            string text, Type variableType, object value)
        {
            var resolver = new Resolver(value);
            var analysis = Interpreter.AnalyzeExpression<Product>(text, Schema, Variables(variableType));
            Assert.True(analysis.IsSemanticallyValid, Diagnostics(analysis));
            Assert.Equal(0, resolver.Calls);

            var runtime = Interpreter.ParsePredicate<Product>(text, resolver);
            Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
            Assert.True(runtime.Result.Compile()(new Product { Price = value is bool ? 1m : 2m }));
            Assert.Equal(1, resolver.Calls);
        }

        [Theory]
        [InlineData("Price = $value", typeof(string))]
        [InlineData("Price in [$value]", typeof(decimal[]))]
        [InlineData("Price in $value", typeof(double[]))]
        [InlineData("Price + 1 > $value", typeof(double))]
        [InlineData("Price + $value > 1", typeof(double))]
        [InlineData("(Price) > $value", typeof(double))]
        [InlineData("Price > ($value)", typeof(double))]
        [InlineData("Discount > $value", typeof(double))]
        public void MetadataDoesNotApplyScalarContextualConversionsToOtherSlots(string text, Type variableType)
        {
            var analysis = Interpreter.AnalyzeExpression<Product>(text, Schema, Variables(variableType));
            Assert.False(analysis.IsSemanticallyValid);
            Assert.NotEmpty(analysis.SemanticDiagnostics);
        }

        [Fact]
        public void RuntimeFailuresRemainDeferredForOtherwiseCompatibleValues()
        {
            const string text = "Price in [$value]";
            var analysis = Interpreter.AnalyzeExpression<Product>(text, Schema, Variables(typeof(string)));
            Assert.True(analysis.IsSemanticallyValid, Diagnostics(analysis));
            var runtime = Interpreter.ParsePredicate<Product>(text, new Resolver("not a number"));
            Assert.False(runtime.Succeeded);
        }

        [Theory]
        [InlineData("Price IN [$value]")]
        [InlineData("Price NOT   IN [$value]")]
        public void OperatorCaseAndCompoundWhitespaceUseTheRuntimeIdentity(string text)
        {
            var analysis = Interpreter.AnalyzeExpression<Product>(text, Schema, Variables(typeof(double)));
            Assert.True(analysis.IsSemanticallyValid, Diagnostics(analysis));
            var runtime = Interpreter.ParsePredicate<Product>(text, new Resolver(2d));
            Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
            Assert.True(runtime.Result.Compile()(new Product
            {
                Price = text.Contains("NOT", StringComparison.Ordinal) ? 3m : 2m
            }));
        }

        [Fact]
        public void ScalarMetadataProbeMatchesFrameworkConversions()
        {
            var samples = new object[]
            {
                (sbyte)1, (byte)1, (short)1, (ushort)1, '1', 1, 1u, 1L, 1UL, 1f, 1d, 1m,
                true, DateTime.MinValue, SampleEnum.One
            };
            var targets = new[]
            {
                typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(char), typeof(int),
                typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
                typeof(bool), typeof(DateTime), typeof(string), typeof(SampleEnum), typeof(Guid), typeof(TimeSpan)
            };
            foreach (var value in samples)
            foreach (var target in targets)
            {
                var supported = true;
                try { Convert.ChangeType(value, target, CultureInfo.InvariantCulture); }
                catch (InvalidCastException) { supported = false; }
                Assert.True(supported == ExpressionConversionRules.CanConvertVariable(value.GetType(), target),
                    value.GetType().Name + " -> " + target.Name);
            }
        }

        [Fact]
        public void MetadataProbeDoesNotInvokeApplicationConversionCode()
        {
            Assert.False(ExpressionConversionRules.CanConvertVariable(typeof(ThrowingConvertible), typeof(decimal)));
            Assert.False(ExpressionConversionRules.CanConvertVariable(typeof(decimal), typeof(ThrowingConvertible)));
            Assert.Equal(0, ThrowingConvertible.Calls);
        }

        private static ExpressionVariableSchema Variables(Type type) => new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("value", type)
        });

        private static string Diagnostics(ExpressionSemanticAnalysisResult result) =>
            string.Join("; ", result.SemanticDiagnostics);

        public sealed class Product
        {
            public decimal Price { get; set; }
            public decimal? Discount { get; set; }
        }

        private enum SampleEnum { One = 1 }

        private sealed class Resolver : VariableResolver
        {
            private readonly object _value;
            public int Calls { get; private set; }
            public Resolver(object value) => _value = value;
            protected override Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(new VariableInfo { Name = name, Resolved = true, Value = _value });
            }
        }

        private sealed class ThrowingConvertible : IConvertible
        {
            public static int Calls;
            private static Exception Fail()
            {
                Calls++;
                return new InvalidOperationException("Application conversion executed.");
            }
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
