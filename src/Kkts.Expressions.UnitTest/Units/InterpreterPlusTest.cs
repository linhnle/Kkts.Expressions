using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public partial class InterpreterTest
    {
        [Fact]
        public async Task ParsePredicate_RuntimeRuleCharacterization_AllEntryPoints()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("amount", (int?)4);
            var entity = new TestEntity { Integer = 4, IntegerNullable = 4 };
            foreach (var query in new[]
            {
                "Integer = Integer",
                "(Integer + 1) = (2 + 3)",
                "Integer in (1, 2, 4)"
            })
            {
                var generic = Interpreter.ParsePredicate<TestEntity>(query, resolver);
                var genericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(query, resolver);
                var runtime = Interpreter.ParsePredicate(query, typeof(TestEntity), resolver);
                var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(TestEntity), resolver);

                Assert.True(generic.Succeeded, generic.Exception?.ToString());
                Assert.True(genericAsync.Succeeded, genericAsync.Exception?.ToString());
                Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
                Assert.True(runtimeAsync.Succeeded, runtimeAsync.Exception?.ToString());
                Assert.True(generic.Result.Compile()(entity));
                Assert.True(genericAsync.Result.Compile()(entity));
                Assert.True(((Expression<Func<TestEntity, bool>>)runtime.Result).Compile()(entity));
                Assert.True(((Expression<Func<TestEntity, bool>>)runtimeAsync.Result).Compile()(entity));
            }

            const string nullableVariableQuery = "IntegerNullable = $amount";
            var nullableVariableGeneric = Interpreter.ParsePredicate<TestEntity>(nullableVariableQuery, resolver);
            var nullableVariableGenericAsync = await Interpreter.ParsePredicateAsync<TestEntity>(nullableVariableQuery, resolver);
            var nullableVariableRuntime = Interpreter.ParsePredicate(nullableVariableQuery, typeof(TestEntity), resolver);
            var nullableVariableRuntimeAsync = await Interpreter.ParsePredicateAsync(nullableVariableQuery, typeof(TestEntity), resolver);
            foreach (var result in new EvaluationResultBase[]
                     { nullableVariableGeneric, nullableVariableGenericAsync, nullableVariableRuntime, nullableVariableRuntimeAsync })
            {
                Assert.False(result.Succeeded);
                Assert.IsType<FormatException>(result.Exception);
            }

            const string unsignedQuery = "PULong = 4294967297";
            var unsignedEntity = new PlusEntity { PULong = 4294967297UL };
            var unsignedGeneric = Interpreter.ParsePredicate<PlusEntity>(unsignedQuery);
            var unsignedGenericAsync = await Interpreter.ParsePredicateAsync<PlusEntity>(unsignedQuery);
            var unsignedRuntime = Interpreter.ParsePredicate(unsignedQuery, typeof(PlusEntity));
            var unsignedRuntimeAsync = await Interpreter.ParsePredicateAsync(unsignedQuery, typeof(PlusEntity));
            Assert.True(unsignedGeneric.Succeeded, unsignedGeneric.Exception?.ToString());
            Assert.True(unsignedGenericAsync.Succeeded, unsignedGenericAsync.Exception?.ToString());
            Assert.True(unsignedRuntime.Succeeded, unsignedRuntime.Exception?.ToString());
            Assert.True(unsignedRuntimeAsync.Succeeded, unsignedRuntimeAsync.Exception?.ToString());
            Assert.True(unsignedGeneric.Result.Compile()(unsignedEntity));
            Assert.True(unsignedGenericAsync.Result.Compile()(unsignedEntity));
            Assert.True(((Expression<Func<PlusEntity, bool>>)unsignedRuntime.Result).Compile()(unsignedEntity));
            Assert.True(((Expression<Func<PlusEntity, bool>>)unsignedRuntimeAsync.Result).Compile()(unsignedEntity));

            const string invalidQuery = "PDecimal + PDouble = 0";
            var invalidGeneric = Interpreter.ParsePredicate<PlusEntity>(invalidQuery);
            var invalidGenericAsync = await Interpreter.ParsePredicateAsync<PlusEntity>(invalidQuery);
            var invalidRuntime = Interpreter.ParsePredicate(invalidQuery, typeof(PlusEntity));
            var invalidRuntimeAsync = await Interpreter.ParsePredicateAsync(invalidQuery, typeof(PlusEntity));
            foreach (var result in new EvaluationResultBase[]
                     { invalidGeneric, invalidGenericAsync, invalidRuntime, invalidRuntimeAsync })
            {
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Exception);
                Assert.Contains("+", result.InvalidOperators);
            }
        }

        [Theory]
        [InlineData("PInt+1 = 1+PInt")]
        [InlineData("(PInt + 1) = (2 + 3)")]
        [InlineData("PInt + (1 + (2 + 3)) = 10")]
        [InlineData("PInt + PDouble = 6.5")]
        [InlineData("PInt + PInt = 8")]
        [InlineData("1 + 2 = 3")]
        [InlineData("1.5 + 2 = 3.5")]
        [InlineData("PInt + 1 in [5,6]")]
        [InlineData("PInt + 1 = 5 and PDouble + 0.5 > 2 or Boolean = true")]
        [InlineData("!(PInt + 1 = 6)")]
        [InlineData("not(PInt + 1 = 6)")]
        [InlineData("String = 'a+b'")]
        [InlineData("String + '!' = 'a+b!'")]
        [InlineData("'ID: ' + PInt = 'ID: 4'")]
        [InlineData("PInt + ' items' = '4 items'")]
        [InlineData("1 + 2 + 'x' = '3x'")]
        [InlineData("1 + (2 + 'x') = '12x'")]
        [InlineData("'1' + 2 = '12'")]
        [InlineData("'x' + null = 'x'")]
        [InlineData("String + '!' contains 'b!'")]
        [InlineData("String.contains('a' + '+b')")]
        [InlineData("String.startswith(('a' + '+'))")]
        [InlineData("String.endswith('+' + 'b')")]
        [InlineData("(String + 'x') = ('a+b' + 'x')")]
        [InlineData("String + String = 'a+ba+b'")]
        [InlineData("PInt + null = null")]
        [InlineData("PInt = (1 + 3)")]
        [InlineData("PInt + 1 = PDouble + 2.5")]
        [InlineData("1 + PInt = PDouble + 2.5")]
        [InlineData("null + PInt = null")]
        public async Task ParsePredicate_Plus_AllEntryPoints(string query)
        {
            var entity = new PlusEntity { PInt = 4, PDouble = 2.5, String = "a+b" };
            var sync = Interpreter.ParsePredicate<PlusEntity>(query);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query);
            var runtime = Interpreter.ParsePredicate(query, typeof(PlusEntity));
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity));
            foreach (var result in new[] { sync, asyncResult })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(result.Result.Compile()(entity));
            }
            foreach (var result in new[] { runtime, runtimeAsync })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(((Expression<Func<PlusEntity, bool>>)result.Result).Compile()(entity));
            }
        }

        [Theory]
        [InlineData("PInt + = 5")]
        [InlineData("PInt +")]
        [InlineData("+PInt = 5")]
        [InlineData("PInt ++ 1 = 5")]
        [InlineData("Boolean + 1 = 2")]
        [InlineData("DateTime + 1 = DateTime")]
        [InlineData("Custom + Custom = Custom")]
        [InlineData("Option + 1 = 2")]
        [InlineData("null + null = null")]
        [InlineData("PInt + 1")]
        [InlineData("String + 'x'")]
        [InlineData("PInt in [1+2]")]
        [InlineData("PDecimal + PDouble = 2")]
        [InlineData("PULong + PInt = 2")]
        [InlineData("PULong + (2147483647 + 1) = 2")]
        [InlineData("18446744073709551616 + 1 = 2")]
        public async Task ParsePredicate_Plus_Invalid_AllEntryPoints(string query)
        {
            var generic = Interpreter.ParsePredicate<PlusEntity>(query);
            var genericAsync = await Interpreter.ParsePredicateAsync<PlusEntity>(query);
            var runtime = Interpreter.ParsePredicate(query, typeof(PlusEntity));
            var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity));
            foreach (var result in new EvaluationResultBase[] { generic, genericAsync, runtime, runtimeAsync })
            {
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Exception);
            }
            Assert.Null(generic.Result);
            Assert.Null(genericAsync.Result);
            Assert.Null(runtime.Result);
            Assert.Null(runtimeAsync.Result);
            if (query == "Boolean + 1 = 2")
            {
                Assert.Contains("+", generic.InvalidOperators);
                Assert.Contains("index 9", generic.Exception.ToString());
            }
        }

        public static IEnumerable<object[]> PlusNumericPairs()
        {
            var names = new[] { "SByte", "Byte", "Short", "UShort", "Char", "Int", "UInt", "Long", "ULong", "Float", "Double", "Decimal" };
            var rows = new[]
            {
                "i i i i i i l l x f d m", "i i i i i i u l U f d m",
                "i i i i i i l l x f d m", "i i i i i i u l U f d m",
                "i i i i i i u l U f d m", "i i i i i i l l x f d m",
                "l u l u u l u l U f d m", "l l l l l l l l x f d m",
                "x U x U U x U x U f d m", "f f f f f f f f f f d x",
                "d d d d d d d d d d d x", "m m m m m m m m m x x m"
            };
            var types = new Dictionary<string, Type>
            {
                ["i"] = typeof(int), ["u"] = typeof(uint), ["l"] = typeof(long), ["U"] = typeof(ulong),
                ["f"] = typeof(float), ["d"] = typeof(double), ["m"] = typeof(decimal), ["x"] = null
            };
            for (var i = 0; i < names.Length; ++i)
            {
                var cells = rows[i].Split(' ');
                for (var j = 0; j < names.Length; ++j)
                    foreach (var pair in NullableNumericPairs(names[i], names[j], types[cells[j]]))
                        yield return pair;
            }
        }

        private static IEnumerable<object[]> NullableNumericPairs(string left, string right, Type type)
        {
            foreach (var leftNullable in new[] { false, true })
                foreach (var rightNullable in new[] { false, true })
                    yield return new object[]
                    {
                        (leftNullable ? "N" : "P") + left, (rightNullable ? "N" : "P") + right,
                        type, leftNullable || rightNullable
                    };
        }

        [Theory]
        [MemberData(nameof(PlusNumericPairs))]
        public async Task ParsePredicate_Plus_NumericPairMatrix(string left, string right, Type expected, bool nullable)
        {
            var entity = new PlusEntity();
            SetNumericOperands(entity, new[] { left, right });
            var query = $"{left} + {right} = 2";
            var sync = Interpreter.ParsePredicate<PlusEntity>(query);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query);
            foreach (var result in new[] { sync, asyncResult })
            {
                AssertNumericPair(result, entity, left, right, expected, nullable);
            }
        }

        private static void SetNumericOperands(PlusEntity entity, IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                var property = typeof(PlusEntity).GetProperty(name);
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                property.SetValue(entity, type == typeof(char) ? (object)(char)1 : Convert.ChangeType(1, type));
            }
        }

        private static void AssertNumericPair(EvaluationResult<PlusEntity, bool> result, PlusEntity entity, string left, string right, Type expected, bool nullable)
        {
            if (expected == null)
            {
                Assert.False(result.Succeeded);
                Assert.Contains("+", result.InvalidOperators);
                return;
            }
            Assert.True(result.Succeeded, result.Exception?.ToString());
            var sum = Assert.IsAssignableFrom<BinaryExpression>(((BinaryExpression)result.Result.Body).Left);
            Assert.Equal(ExpressionType.Add, sum.NodeType);
            Assert.Equal(nullable ? typeof(Nullable<>).MakeGenericType(expected) : expected, sum.Type);
            var inspector = new PlusTreeInspector();
            inspector.Visit(result.Result);
            Assert.False(inspector.HasInvocation);
            Assert.True(result.Result.Compile()(entity));
            if (!nullable) return;
            typeof(PlusEntity).GetProperty(left.StartsWith('N') ? left : right).SetValue(entity, null);
            Assert.False(result.Result.Compile()(entity));
            SetNumericOperands(entity, new[] { left, right }.Where(name => name.StartsWith('N')));
        }

        [Theory]
        [InlineData("2147483647", typeof(int))]
        [InlineData("2147483648", typeof(uint))]
        [InlineData("4294967295", typeof(uint))]
        [InlineData("4294967296", typeof(long))]
        [InlineData("9223372036854775807", typeof(long))]
        [InlineData("9223372036854775808", typeof(ulong))]
        [InlineData("18446744073709551615", typeof(ulong))]
        public void ParsePredicate_Plus_LiteralBoundaries(string literal, Type expected)
        {
            var result = Interpreter.ParsePredicate<PlusEntity>($"{literal} + 0 = {literal}");
            Assert.True(result.Succeeded, result.Exception?.ToString());
            Assert.Equal(expected, ((BinaryExpression)((BinaryExpression)result.Result.Body).Left).Type);
            Assert.True(result.Result.Compile()(new PlusEntity()));
        }

        [Theory]
        [InlineData("PUInt + 1 = 2", typeof(uint))]
        [InlineData("1 + PUInt = 2", typeof(uint))]
        [InlineData("PUInt + (1 + 2) = 4", typeof(uint))]
        [InlineData("NUInt + 1 = 2", typeof(uint?))]
        [InlineData("PULong + 4294967296 = 4294967297", typeof(ulong))]
        [InlineData("PUInt + (2147483647 + 1) = RLong", typeof(long))]
        public async Task ParsePredicate_Plus_ConstantPromotions(string query, Type expected)
        {
            var entity = new PlusEntity { PUInt = 1, NUInt = 1, PULong = 1, RLong = -2147483647 };
            var sync = Interpreter.ParsePredicate<PlusEntity>(query);
            var asyncResult = await Interpreter.ParsePredicateAsync<PlusEntity>(query);
            foreach (var result in new[] { sync, asyncResult })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, ((BinaryExpression)result.Result.Body).Left.Type);
                Assert.True(result.Result.Compile()(entity));
            }
        }

        [Fact]
        public void ParsePredicate_Plus_NullOverflowPrecisionAndAssociation()
        {
            var entity = new PlusEntity { PInt = int.MaxValue, RInt = int.MinValue, PDecimal = 0.1m, PDouble = 1e16 };
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("PInt + 1 = RInt").Result.Compile()(entity));
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("PDecimal + PDecimal = '0.2'").Result.Compile()(entity));
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("PDouble + 1 + 1 = PDouble").Result.Compile()(entity));
            Assert.False(Interpreter.ParsePredicate<PlusEntity>("PDouble + (1 + 1) = PDouble").Result.Compile()(entity));
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("NInt + 1 = null").Result.Compile()(entity));
            Assert.False(Interpreter.ParsePredicate<PlusEntity>("NInt + 1 = 5").Result.Compile()(entity));
            entity.NInt = 4;
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("NInt + 1 = 5").Result.Compile()(entity));
            Assert.True(Interpreter.ParsePredicate<PlusEntity>("String + 'x' = 'x'").Result.Compile()(entity));
            entity.PDecimal = decimal.MaxValue;
            var overflow = Interpreter.ParsePredicate<PlusEntity>("PDecimal + 1 = PDecimal");
            Assert.True(overflow.Succeeded);
            Assert.Throws<OverflowException>(() => overflow.Result.Compile()(entity));
        }

        [Fact]
        public void ParsePredicate_Plus_ByteValuesDoNotOverflow()
        {
            var result = Interpreter.ParsePredicate<PlusEntity>("PByte + RByte = 300");
            Assert.True(result.Succeeded, result.Exception?.ToString());
            var sum = (BinaryExpression)((BinaryExpression)result.Result.Body).Left;
            Assert.Equal(typeof(int), sum.Type);
            Assert.Equal(ExpressionType.Convert, sum.Left.NodeType);
            Assert.Equal(ExpressionType.Convert, sum.Right.NodeType);
            Assert.True(result.Result.Compile()(new PlusEntity { PByte = 200, RByte = 100 }));
        }

        [Fact]
        public void ParsePredicate_Plus_StringConversionAndTreeShape()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var entity = new PlusEntity { PDouble = 1.5, DateTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), Option = TestOptions.Option1 };
                var resolver = new VariableResolver();
                resolver.TryAdd("expected", "prefix:" + entity.PDouble + entity.Boolean + entity.DateTime + entity.Option);
                var result = Interpreter.ParsePredicate<PlusEntity>("'prefix:' + PDouble + Boolean + DateTime + Option = $expected", resolver);
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.True(result.Result.Compile()(entity));
                var inspector = new PlusTreeInspector();
                inspector.Visit(result.Result);
                Assert.True(inspector.ConcatCalls > 0);
                Assert.False(inspector.HasInvocation);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Theory]
        [InlineData("alias + $increment = $target")]
        [InlineData("$increment + alias = $target")]
        [InlineData("alias + 1 in $values")]
        [InlineData("String + $suffix = 'ab'")]
        [InlineData("$prefix + String = 'ba'")]
        public async Task ParsePredicate_Plus_MappingValidationAndVariables(string query)
        {
            var entity = new PlusEntity { PInt = 4, String = "a", Parent = new ParentEntity { Id = 2 } };
            var mapping = new Dictionary<string, string> { ["alias"] = "PInt" };
            foreach (var useAsync in new[] { false, true })
            {
                var resolver = new VariableResolver();
                resolver.TryAdd("increment", 2);
                resolver.TryAdd("target", 6);
                resolver.TryAdd("values", new[] { 5, 6 });
                resolver.TryAdd("suffix", "b");
                resolver.TryAdd("prefix", "b");
                var allowed = new[] { "alias", "String" };
                var valid = useAsync
                    ? await Interpreter.ParsePredicateAsync<PlusEntity>(query, resolver, allowed, mapping)
                    : Interpreter.ParsePredicate<PlusEntity>(query, resolver, allowed, mapping);
                Assert.True(valid.Succeeded, valid.Exception?.ToString());
                Assert.True(valid.Result.Compile()(entity));
                var runtime = useAsync
                    ? await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity), resolver, allowed, mapping)
                    : Interpreter.ParsePredicate(query, typeof(PlusEntity), resolver, allowed, mapping);
                Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
                Assert.True(((Expression<Func<PlusEntity, bool>>)runtime.Result).Compile()(entity));
                Assert.True(Interpreter.ParsePredicate<PlusEntity>("PInt + Parent.Id = 6").Result.Compile()(entity));
                await AssertRestrictedProperties(useAsync);
                await AssertMissingVariables(useAsync);
            }
        }

        private static async Task AssertRestrictedProperties(bool useAsync)
        {
            foreach (var query in new[] { "PInt + PDouble = 5", "PDouble + PInt = 5" })
            {
                var generic = useAsync
                    ? await Interpreter.ParsePredicateAsync<PlusEntity>(query, validProperties: new[] { "PInt" })
                    : Interpreter.ParsePredicate<PlusEntity>(query, validProperties: new[] { "PInt" });
                Assert.False(generic.Succeeded);
                Assert.Contains("PDouble", generic.InvalidProperties);
                var runtime = useAsync
                    ? await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity), validProperties: new[] { "PInt" })
                    : Interpreter.ParsePredicate(query, typeof(PlusEntity), validProperties: new[] { "PInt" });
                Assert.False(runtime.Succeeded);
                Assert.Contains("PDouble", runtime.InvalidProperties);
                Assert.Null(runtime.Result);
            }
        }

        private static async Task AssertMissingVariables(bool useAsync)
        {
            foreach (var query in new[] { "PInt + $missing = 5", "$missing + PInt = 5" })
            {
                var generic = useAsync
                    ? await Interpreter.ParsePredicateAsync<PlusEntity>(query)
                    : Interpreter.ParsePredicate<PlusEntity>(query);
                Assert.False(generic.Succeeded);
                Assert.Contains("missing", generic.InvalidVariables);
                var runtime = useAsync
                    ? await Interpreter.ParsePredicateAsync(query, typeof(PlusEntity))
                    : Interpreter.ParsePredicate(query, typeof(PlusEntity));
                Assert.False(runtime.Succeeded);
                Assert.Contains("missing", runtime.InvalidVariables);
                Assert.Null(runtime.Result);
            }
        }

        private sealed class PlusTreeInspector : ExpressionVisitor
        {
            public int ConcatCalls { get; private set; }
            public bool HasInvocation { get; private set; }
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.DeclaringType == typeof(string) && node.Method.Name == nameof(string.Concat)) ++ConcatCalls;
                return base.VisitMethodCall(node);
            }
            protected override Expression VisitInvocation(InvocationExpression node)
            {
                HasInvocation = true;
                return base.VisitInvocation(node);
            }
        }

        public class PlusEntity
        {
            public sbyte PSByte { get; set; }
            public byte PByte { get; set; }
            public short PShort { get; set; }
            public ushort PUShort { get; set; }
            public char PChar { get; set; }
            public int PInt { get; set; }
            public uint PUInt { get; set; }
            public long PLong { get; set; }
            public ulong PULong { get; set; }
            public float PFloat { get; set; }
            public double PDouble { get; set; }
            public decimal PDecimal { get; set; }
            public sbyte? NSByte { get; set; }
            public byte? NByte { get; set; }
            public short? NShort { get; set; }
            public ushort? NUShort { get; set; }
            public char? NChar { get; set; }
            public int? NInt { get; set; }
            public uint? NUInt { get; set; }
            public long? NLong { get; set; }
            public ulong? NULong { get; set; }
            public float? NFloat { get; set; }
            public double? NDouble { get; set; }
            public decimal? NDecimal { get; set; }
            public int RInt { get; set; }
            public byte RByte { get; set; }
            public long RLong { get; set; }
            public string String { get; set; }
            public bool Boolean { get; set; }
            public DateTime DateTime { get; set; }
            public DateTimeOffset Timestamp { get; set; }
            public TimeSpan Duration { get; set; }
            public TestOptions Option { get; set; }
            public ParentEntity Parent { get; set; }
            public CustomPlus Custom { get; set; }
        }

        public class CustomPlus
        {
            public static CustomPlus operator +(CustomPlus left, CustomPlus right) => left;
            public static CustomPlus operator -(CustomPlus left, CustomPlus right) => left;
            public static bool operator ==(CustomPlus left, CustomPlus right) => ReferenceEquals(left, right);
            public static bool operator !=(CustomPlus left, CustomPlus right) => !ReferenceEquals(left, right);
            public override bool Equals(object obj) => ReferenceEquals(this, obj);
            public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }
    }

    public partial class InterpreterAsyncTest
    {
        [Theory]
        [InlineData("PInt + $increment = $target")]
        [InlineData("($increment + PInt) = $target and Boolean = $flag")]
        [InlineData("not((PInt + $increment) != $target)")]
        [InlineData("String.contains('a' + $suffix)")]
        public async Task ParsePredicateAsync_Plus_AsyncOnlyResolver(string query)
        {
            using var source = new CancellationTokenSource();
            var resolver = new PlusResolver(source.Token);
            var result = await Interpreter.ParsePredicateAsync<InterpreterTest.PlusEntity>(query, resolver, cancellationToken: source.Token);
            Assert.True(result.Succeeded, result.Exception?.ToString());
            Assert.True(result.Result.Compile()(new InterpreterTest.PlusEntity { PInt = 4, String = "ab" }));
            Assert.All(resolver.Calls.Values, count => Assert.Equal(1, count));
        }

        [Fact]
        public async Task ParsePredicateAsync_Plus_Cancellation()
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var result = await Interpreter.ParsePredicateAsync<InterpreterTest.PlusEntity>(
                "PInt + $increment = 6", new PlusResolver(source.Token), cancellationToken: source.Token);
            Assert.False(result.Succeeded);
            Assert.Contains(nameof(OperationCanceledException), result.Exception.ToString());
        }

        private sealed class PlusResolver : VariableResolver
        {
            private readonly CancellationToken _token;
            public Dictionary<string, int> Calls { get; } = new Dictionary<string, int>();
            public PlusResolver(CancellationToken token) => _token = token;
            protected override async Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                Assert.Equal(_token, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                Calls[name] = Calls.TryGetValue(name, out var count) ? count + 1 : 1;
                var values = new Dictionary<string, object> { ["increment"] = 2, ["target"] = 6, ["flag"] = false, ["suffix"] = "b" };
                return new VariableInfo { Name = name, Resolved = values.TryGetValue(name, out var value), Value = value };
            }
        }
    }
}
