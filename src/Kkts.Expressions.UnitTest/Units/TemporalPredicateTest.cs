using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class TemporalPredicateTest
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "th-TH" })
            {
                foreach (var op in new[] { ComparisonOperator.Equal, ComparisonOperator.In })
                {
                    foreach (var nullable in new[] { false, true })
                    {
                        var dateProperty = nullable ? "NullableDate" : "Date";
                        var offsetProperty = nullable ? "NullableOffset" : "Offset";
                        yield return new object[] { culture, op, dateProperty, "20261009", new DateTime(2026, 10, 9) };
                        yield return new object[] { culture, op, dateProperty, "20260109T082646Z",
                            new DateTimeOffset(2026, 1, 9, 8, 26, 46, TimeSpan.Zero).LocalDateTime };
                        yield return new object[] { culture, op, dateProperty, "20260709T152646.1234567+0700",
                            new DateTimeOffset(2026, 7, 9, 15, 26, 46, TimeSpan.FromHours(7)).AddTicks(1234567).LocalDateTime };
                        yield return new object[] { culture, op, offsetProperty, "20261009T152646.427+0700",
                            new DateTimeOffset(2026, 10, 9, 15, 26, 46, 427, TimeSpan.FromHours(7)) };
                        yield return new object[] { culture, op, offsetProperty, "20261009T052646-0300",
                            new DateTimeOffset(2026, 10, 9, 5, 26, 46, TimeSpan.FromHours(-3)) };
                        yield return new object[] { culture, op, offsetProperty, "20261009T082646Z",
                            new DateTimeOffset(2026, 10, 9, 8, 26, 46, TimeSpan.Zero) };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task CompactTemporalValues_AllPredicateEntryPoints(
            string culture, ComparisonOperator op, string property, string text, object expected)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var symbol = op == ComparisonOperator.Equal ? "=" : "in";
                var query = op == ComparisonOperator.Equal
                    ? $"{property} = '{text}'"
                    : $"{property} in ['{text}']";
                var expressions = new List<Expression<Func<TemporalEntity, bool>>>();
                foreach (var result in new[]
                {
                    Interpreter.ParsePredicate<TemporalEntity>(query),
                    await Interpreter.ParsePredicateAsync<TemporalEntity>(query)
                })
                {
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    expressions.Add(result.Result);
                }
                foreach (var result in new[]
                {
                    Interpreter.ParsePredicate(query, typeof(TemporalEntity)),
                    await Interpreter.ParsePredicateAsync(query, typeof(TemporalEntity))
                })
                {
                    Assert.True(result.Succeeded, result.Exception?.ToString());
                    expressions.Add((Expression<Func<TemporalEntity, bool>>)result.Result);
                }

                var value = op == ComparisonOperator.Equal ? text : $"'{text}'";
                expressions.Add(Interpreter.BuildPredicate<TemporalEntity>(property, op, value));
                expressions.Add(await Interpreter.BuildPredicateAsync<TemporalEntity>(property, op, value));
                expressions.Add((Expression<Func<TemporalEntity, bool>>)Interpreter.BuildPredicate(property, op, value, typeof(TemporalEntity)));
                expressions.Add((Expression<Func<TemporalEntity, bool>>)await Interpreter.BuildPredicateAsync(property, op, value, typeof(TemporalEntity)));
                var filter = new Filter { Property = property, Operator = symbol, Value = value };
                expressions.Add(filter.BuildPredicate<TemporalEntity>());
                expressions.Add(await filter.BuildPredicateAsync<TemporalEntity>());
                expressions.Add((Expression<Func<TemporalEntity, bool>>)filter.BuildPredicate(typeof(TemporalEntity)));
                expressions.Add((Expression<Func<TemporalEntity, bool>>)await filter.BuildPredicateAsync(typeof(TemporalEntity)));

                var matching = new TemporalEntity();
                var nonmatching = new TemporalEntity();
                var member = typeof(TemporalEntity).GetProperty(property);
                member.SetValue(matching, expected);
                member.SetValue(nonmatching, expected is DateTime date ? (object)date.AddDays(1) : ((DateTimeOffset)expected).AddDays(1));
                foreach (var expression in expressions)
                {
                    var visitor = new TemporalConstants();
                    visitor.Visit(expression);
                    Assert.NotEmpty(visitor.Values);
                    Assert.All(visitor.Values, actual =>
                    {
                        Assert.Equal(expected.GetType(), actual.GetType());
                        if (expected is DateTime expectedDate)
                        {
                            var actualDate = Assert.IsType<DateTime>(actual);
                            Assert.Equal(expectedDate.Ticks, actualDate.Ticks);
                            Assert.Equal(expectedDate.Kind, actualDate.Kind);
                        }
                        else
                        {
                            var expectedOffset = Assert.IsType<DateTimeOffset>(expected);
                            var actualOffset = Assert.IsType<DateTimeOffset>(actual);
                            Assert.Equal(expectedOffset.Ticks, actualOffset.Ticks);
                            Assert.Equal(expectedOffset.Offset, actualOffset.Offset);
                        }
                    });
                    var predicate = expression.Compile();
                    Assert.True(predicate(matching));
                    Assert.False(predicate(nonmatching));
                    if (property.StartsWith("Nullable", StringComparison.Ordinal))
                        Assert.False(predicate(new TemporalEntity()));
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        private sealed class TemporalConstants : ExpressionVisitor
        {
            public List<object> Values { get; } = new List<object>();

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is DateTime || node.Value is DateTimeOffset) Values.Add(node.Value);
                if (node.Value is Array values)
                {
                    foreach (var value in values)
                        if (value is DateTime || value is DateTimeOffset) Values.Add(value);
                }
                return base.VisitConstant(node);
            }
        }

        public sealed class TemporalEntity
        {
            public DateTime Date { get; set; }
            public DateTime? NullableDate { get; set; }
            public DateTimeOffset Offset { get; set; }
            public DateTimeOffset? NullableOffset { get; set; }
        }
    }
}
