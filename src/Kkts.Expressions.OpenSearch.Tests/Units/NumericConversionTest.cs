using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class NumericConversionTest : PredicateTestBase
    {
        private static readonly ExpressionType[] Operators =
        {
            ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan,
            ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan, ExpressionType.GreaterThanOrEqual
        };

        private readonly ITestOutputHelper _output;

        public NumericConversionTest(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> Cases()
        {
            const double p53 = 9007199254740992d;
            yield return new object[] { nameof(Data.IntValue), typeof(float), new object[] { 16777216f, 16777218f, 16777220f, -16777216f, 2147483648f, -2147483648f, 0f, 0.5f, -0.5f, 8388607.5f, 1e10f, -1e10f } };
            yield return new object[] { nameof(Data.IntValue), typeof(double), new object[] { 1.5d, -1.5d, 2147483647d, 2147483647.5d, -2147483648.5d, 0d } };
            yield return new object[] { nameof(Data.LongValue), typeof(double), new object[] { p53, p53 + 2, p53 + 4, -p53, -p53 - 2, 9223372036854775807d, -9223372036854775808d, 0.5d, 1e300, -1e300 } };
            yield return new object[] { nameof(Data.LongValue), typeof(float), new object[] { 16777216f, 9.223372E18f, -9.223372E18f, 1099511627776f } };
            yield return new object[] { nameof(Data.ULongValue), typeof(double), new object[] { 18446744073709551615d, p53, p53 + 2, 0d, -1d, 0.5d } };
            yield return new object[] { nameof(Data.ULongValue), typeof(float), new object[] { 18446744073709551615f, 16777216f, 0f } };
            yield return new object[] { nameof(Data.UIntValue), typeof(float), new object[] { 4294967296f, 4294967040f, 16777216f, 0f, -1f } };
            yield return new object[] { nameof(Data.UIntValue), typeof(long), new object[] { 4294967296L, 4294967295L, 0L, -1L } };
            yield return new object[] { nameof(Data.IntValue), typeof(long), new object[] { 5000000000L, 2147483647L, -2147483648L, -2147483649L } };
            yield return new object[] { nameof(Data.ShortValue), typeof(float), new object[] { 1.5f, 32767f, -32768.5f } };
            yield return new object[] { nameof(Data.SByteValue), typeof(int), new object[] { -128, 127, 200, -200 } };
            yield return new object[] { nameof(Data.ByteValue), typeof(decimal), new object[] { 255m, 0m, 1.5m, -0.5m } };
            yield return new object[] { nameof(Data.LongValue), typeof(decimal), new object[] { 9223372036854775807m, 9223372036854775807.5m, -9223372036854775808.5m, 1.25m } };
            yield return new object[] { nameof(Data.NullableLong), typeof(double?), new object[] { p53, p53 + 2, 0d } };
            yield return new object[] { nameof(Data.NullableInteger), typeof(float?), new object[] { 16777216f, 0.5f } };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ConvertedComparisons_AgreeWithClr(string memberName, Type targetType, object[] constants)
        {
            var property = typeof(Data).GetProperty(memberName);
            var candidates = Candidates(property.PropertyType, constants).ToList();
            var checkedCount = 0;

            foreach (var constant in constants)
            {
                foreach (var op in Operators)
                {
                    foreach (var negate in new[] { false, true })
                    {
                        var predicate = Compare(property, targetType, op, constant, negate);
                        var result = Translate(predicate);
                        Assert.True(result.Succeeded, predicate + ": " + string.Join("; ", result.Diagnostics));
                        var compiled = predicate.Compile();
                        foreach (var candidate in candidates)
                        {
                            var data = new Data();
                            property.SetValue(data, candidate);
                            var expected = compiled(data);
                            var actual = Matches(result, Document(property, candidate));
                            if (expected != actual) _output.WriteLine(result.ToJsonString());
                            Assert.True(expected == actual, $"{predicate} with {memberName}={candidate ?? "null"}: CLR {expected}, query {actual}");
                            checkedCount++;
                        }
                    }
                }
            }

            Assert.True(checkedCount > 0);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ConvertedMembership_AgreesWithClr(string memberName, Type targetType, object[] constants)
        {
            var property = typeof(Data).GetProperty(memberName);
            var candidates = Candidates(property.PropertyType, constants).ToList();
            var lists = new List<Array>
            {
                ToArray(targetType, constants),
                ToArray(targetType, constants.Take(2).Concat(constants.Take(1)).ToArray()),
                ToArray(targetType, new object[0])
            };
            if (Nullable.GetUnderlyingType(targetType) != null)
                lists.Add(ToArray(targetType, new object[] { null }.Concat(constants.Take(1)).ToArray()));

            foreach (var list in lists)
            {
                foreach (var negate in new[] { false, true })
                {
                    var predicate = Contains(property, targetType, list, negate);
                    var result = Translate(predicate);
                    Assert.True(result.Succeeded, predicate + ": " + string.Join("; ", result.Diagnostics));
                    var compiled = predicate.Compile();
                    foreach (var candidate in candidates)
                    {
                        var data = new Data();
                        property.SetValue(data, candidate);
                        Assert.True(compiled(data) == Matches(result, Document(property, candidate)),
                            $"{predicate} [{string.Join(", ", list.Cast<object>())}] with {memberName}={candidate ?? "null"}: CLR {compiled(data)}\n{result.ToJsonString()}");
                    }
                }
            }
        }

        [Fact]
        public void RoundedIntegralEquality()
        {
            var result = Translate(x => x.LongValue == 9007199254740992d);

            Assert.True(Matches(result, Doc(("longValue", 9007199254740992L))));
            Assert.True(Matches(result, Doc(("longValue", 9007199254740993L))));
            Assert.False(Matches(result, Doc(("longValue", 9007199254740994L))));
            Assert.False(Matches(result, Doc(("longValue", 9007199254740991L))));
            Assert.Equal((double)9007199254740993L, 9007199254740992d);
        }

        [Fact]
        public void RoundedRangeBoundary()
        {
            var result = Translate(x => x.LongValue > 9007199254740992d);

            Assert.False(Matches(result, Doc(("longValue", 9007199254740993L))));
            Assert.True(Matches(result, Doc(("longValue", 9007199254740995L))));
            Assert.False((double)9007199254740993L > 9007199254740992d);
            Assert.True((double)9007199254740995L > 9007199254740992d);
        }

        [Fact]
        public void ConvertedMembership()
        {
            var values = new[] { 9007199254740992d, 1.5d };

            var result = Translate(x => values.Contains(x.LongValue));

            Assert.True(Matches(result, Doc(("longValue", 9007199254740992L))));
            Assert.True(Matches(result, Doc(("longValue", 9007199254740993L))));
            Assert.False(Matches(result, Doc(("longValue", 1L))));
            Assert.False(Matches(result, Doc(("longValue", 2L))));
        }

        [Fact]
        public void ExactConversions_KeepSimpleClauses()
        {
            AssertClause(x => x.IntValue == 1.0, "{\"term\":{\"intValue\":1}}");
            AssertClause(x => x.IntValue > 1.5, "{\"range\":{\"intValue\":{\"gte\":2}}}");
            AssertClause(x => x.IntValue == 1.5, "{\"match_none\":{}}");
            AssertClause(x => x.LongValue == 9007199254740992d,
                "{\"range\":{\"longValue\":{\"gte\":9007199254740992,\"lte\":9007199254740993}}}");
        }

        [Fact]
        public void UnprovableConversions_Fail()
        {
            AssertFails(x => x.Ratio > 1.5, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => (int)x.Score == 1, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => (int)x.LongValue == 1, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => (double)x.Price == 1.5, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => (int)x.NullableInteger == 1, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => (Status)x.IntValue == Status.Active, OpenSearchDiagnosticCodes.UnsupportedConversion);
            AssertFails(x => x.Grade > 'A', OpenSearchDiagnosticCodes.UnsupportedConversion);
            var mapping = OpenSearchMapping.For<Data>().Field(x => x.IntValue, f => f.Converter<int>(v => v.ToString())).Build();
            AssertFails(x => x.IntValue > 1.5, OpenSearchDiagnosticCodes.UnsupportedConversion, mapping);
            var diagnostic = AssertFails(x => x.Ratio > 1.5, OpenSearchDiagnosticCodes.UnsupportedConversion);
            Assert.Equal("Ratio", diagnostic.MemberPath);
        }

        [Fact]
        public void CharMembership_UsesStrings()
        {
            AssertClause(x => x.Grade == 'A', "{\"term\":{\"grade.keyword\":\"A\"}}");
            AssertClause(x => new[] { 'A', 'B' }.Contains(x.Grade), "{\"terms\":{\"grade.keyword\":[\"A\",\"B\"]}}");
        }

        private static Expression<Func<Data, bool>> Compare(PropertyInfo property, Type target, ExpressionType op, object constant, bool negate)
        {
            var x = Expression.Parameter(typeof(Data), "x");
            Expression body = Expression.MakeBinary(op, Expression.Convert(Expression.Property(x, property), target), Expression.Constant(constant, target));
            if (negate) body = Expression.Not(body);
            return Expression.Lambda<Func<Data, bool>>(body, x);
        }

        private static Expression<Func<Data, bool>> Contains(PropertyInfo property, Type target, Array list, bool negate)
        {
            var x = Expression.Parameter(typeof(Data), "x");
            Expression body = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { target },
                Expression.Constant(list), Expression.Convert(Expression.Property(x, property), target));
            if (negate) body = Expression.Not(body);
            return Expression.Lambda<Func<Data, bool>>(body, x);
        }

        private static Array ToArray(Type type, object[] values)
        {
            var array = Array.CreateInstance(type, values.Length);
            for (var i = 0; i < values.Length; i++) array.SetValue(values[i], i);
            return array;
        }

        private static Dictionary<string, JsonNode[]> Document(PropertyInfo property, object value)
        {
            var field = char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
            return value == null ? Doc() : Doc((field, JsonNode.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))));
        }

        private static IEnumerable<object> Candidates(Type propertyType, object[] constants)
        {
            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            var (min, max) = Range(type);
            var anchors = new HashSet<BigInteger> { min, max, 0, 1, -1 };
            foreach (var constant in constants)
            {
                var d = Convert.ToDouble(constant, System.Globalization.CultureInfo.InvariantCulture);
                if (Math.Abs(d) < 1e30) anchors.Add(new BigInteger(Math.Floor(d)));
            }

            var values = new SortedSet<BigInteger>();
            foreach (var anchor in anchors)
                for (var delta = -6; delta <= 6; delta++)
                {
                    var v = anchor + delta;
                    if (v >= min && v <= max) values.Add(v);
                }

            foreach (var v in values) yield return Convert.ChangeType(v.ToString(), type, System.Globalization.CultureInfo.InvariantCulture);
            if (type != propertyType) yield return null;
        }

        private static (BigInteger Min, BigInteger Max) Range(Type type)
        {
            if (type == typeof(int)) return (int.MinValue, int.MaxValue);
            if (type == typeof(long)) return (long.MinValue, long.MaxValue);
            if (type == typeof(ulong)) return (ulong.MinValue, ulong.MaxValue);
            if (type == typeof(uint)) return (uint.MinValue, uint.MaxValue);
            if (type == typeof(short)) return (short.MinValue, short.MaxValue);
            if (type == typeof(sbyte)) return (sbyte.MinValue, sbyte.MaxValue);
            if (type == typeof(byte)) return (byte.MinValue, byte.MaxValue);
            throw new NotSupportedException(type.Name);
        }
    }
}
