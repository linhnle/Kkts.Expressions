using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    [CollectionDefinition("Temporal format mutations", DisableParallelization = true)]
    public class TemporalFormatMutationCollection
    {
    }

    [Collection("Temporal format mutations")]
    public class TemporalStringParsingTest
    {
        public static IEnumerable<object[]> MachineCases()
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "th-TH" })
            {
                yield return new object[] { culture, "2026-10-09", new DateTime(2026, 10, 9), null };
                yield return new object[] { culture, "20261009", new DateTime(2026, 10, 9), null };
                foreach (var compact in new[] { false, true })
                {
                    for (var precision = 0; precision <= 7; ++precision)
                    {
                        var fraction = "1234567".Substring(0, precision);
                        var ticks = precision == 0 ? 0 : long.Parse(fraction.PadRight(7, '0'), CultureInfo.InvariantCulture);
                        var date = new DateTime(2026, 10, 9, 15, 26, 46).AddTicks(ticks);
                        var prefix = compact ? "20261009T152646" : "2026-10-09T15:26:46";
                        if (precision > 0) prefix += "." + fraction;
                        yield return new object[] { culture, prefix, date, null };
                        yield return new object[] { culture, prefix + "Z", date, TimeSpan.Zero };
                        yield return new object[] { culture, prefix + (compact ? "+0700" : "+07:00"), date, TimeSpan.FromHours(7) };
                        yield return new object[] { culture, prefix + (compact ? "-0330" : "-03:30"), date, TimeSpan.FromMinutes(-210) };
                    }
                }
                yield return new object[] { culture, "20261009T082646Z", new DateTime(2026, 10, 9, 8, 26, 46), TimeSpan.Zero };
                yield return new object[] { culture, "2026-10-09T08:26:46.427Z", new DateTime(2026, 10, 9, 8, 26, 46, 427), TimeSpan.Zero };
                yield return new object[] { culture, "20261009T152646.427+0700", new DateTime(2026, 10, 9, 15, 26, 46, 427), TimeSpan.FromHours(7) };
                yield return new object[] { culture, "20261009T152646+1400", new DateTime(2026, 10, 9, 15, 26, 46), TimeSpan.FromHours(14) };
                yield return new object[] { culture, "20261009T152646-1400", new DateTime(2026, 10, 9, 15, 26, 46), TimeSpan.FromHours(-14) };
                yield return new object[] { culture, "20240229T123000", new DateTime(2024, 2, 29, 12, 30, 0), null };
            }
        }

        [Theory]
        [MemberData(nameof(MachineCases))]
        public void MachineFallback_PreservesCalendarPrecisionAndTimezone(
            string culture, string text, DateTime calendar, TimeSpan? offset)
        {
            WithCulture(culture, () =>
            {
                var expectedOffset = new DateTimeOffset(calendar, offset ?? TimeZoneInfo.Local.GetUtcOffset(calendar));
                var expectedDate = offset.HasValue ? expectedOffset.LocalDateTime : calendar;
                Assert.True(MachineDateTimeParser.TryParseDateTime(text, out var date));
                AssertDate(expectedDate, date);
                Assert.True(MachineDateTimeParser.TryParseDateTimeOffset(text, out var zoned));
                AssertOffset(expectedOffset, zoned);
                Assert.True(MachineDateTimeParser.IsValid(text));
            });
        }

        [Theory]
        [MemberData(nameof(MachineCases))]
        public void PublicApis_AddFormatsWithoutChangingLegacyResults(
            string culture, string text, DateTime calendar, TimeSpan? offset)
        {
            WithCulture(culture, () =>
            {
                var expectedOffset = new DateTimeOffset(calendar, offset ?? TimeZoneInfo.Local.GetUtcOffset(calendar));
                var expectedDate = offset.HasValue ? expectedOffset.LocalDateTime : calendar;
                if (LegacyDate(text, null, out var legacyDate)) expectedDate = legacyDate;
                if (LegacyOffset(text, null, out var legacyOffset)) expectedOffset = legacyOffset;
                AssertDate(expectedDate, text.ToDateTime());
                Assert.True(text.TryParseDateTime(out var date));
                AssertDate(expectedDate, date);
                AssertDate(expectedDate, text.Cast<DateTime>());
                AssertDate(expectedDate, Assert.IsType<DateTime>(text.Cast(typeof(DateTime?))));
                Assert.True(text.TryCast<DateTime>(out var castDate));
                AssertDate(expectedDate, castDate);
                Assert.True(text.TryCast(typeof(DateTime?), out var boxedDate));
                AssertDate(expectedDate, Assert.IsType<DateTime>(boxedDate));

                AssertOffset(expectedOffset, text.ToDateTimeOffset());
                Assert.True(text.TryParseDateTimeOffset(out var zoned));
                AssertOffset(expectedOffset, zoned);
                AssertOffset(expectedOffset, text.Cast<DateTimeOffset>());
                AssertOffset(expectedOffset, Assert.IsType<DateTimeOffset>(text.Cast(typeof(DateTimeOffset?))));
                Assert.True(text.TryCast<DateTimeOffset>(out var castOffset));
                AssertOffset(expectedOffset, castOffset);
                Assert.True(text.TryCast(typeof(DateTimeOffset?), out var boxedOffset));
                AssertOffset(expectedOffset, Assert.IsType<DateTimeOffset>(boxedOffset));
            });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("not a date")]
        [InlineData("20260230")]
        [InlineData("20261009T256146Z")]
        [InlineData("20261009T152646+1500")]
        [InlineData("20261009T152646+1401")]
        [InlineData("20261009T152646-1401")]
        [InlineData("20261009T152646+0760")]
        [InlineData("20261009T152646+070")]
        [InlineData("20261009T152646+07:00")]
        [InlineData("20261009T152646.")]
        [InlineData("20261009T152646.12345678Z")]
        [InlineData("20261009T152646z")]
        [InlineData("20261009t152646Z")]
        [InlineData("20261009T1526")]
        [InlineData("2026-W41-5")]
        [InlineData("2026-282")]
        [InlineData("/Date(1728432000000)/")]
        [InlineData("\"2026-10-09T08:26:46Z\"")]
        public void InvalidFallback_DoesNotRepairOrDecodeInputs(string text)
        {
            Assert.False(MachineDateTimeParser.TryParseDateTime(text, out var date));
            Assert.Equal(DateTime.MinValue, date);
            Assert.False(MachineDateTimeParser.TryParseDateTimeOffset(text, out var zoned));
            Assert.Equal(DateTimeOffset.MinValue, zoned);
            Assert.False(MachineDateTimeParser.IsValid(text));
            WithCulture("en-US", () =>
            {
                // Inputs outside the guaranteed subset may still be accepted by legacy .NET parsing.
                if (!LegacyDate(text, null, out _))
                {
                    var error = Assert.Throws<FormatException>(() => text.ToDateTime());
                    Assert.Equal(text == null ? "Can not parse null value to DateTime" :
                        $"String '{text}' was not recognized as a valid DateTime.", error.Message);
                    Assert.False(text.TryParseDateTime(out var result));
                    Assert.Equal(DateTime.MinValue, result);
                }
                if (!LegacyOffset(text, null, out _))
                {
                    var error = Assert.Throws<FormatException>(() => text.ToDateTimeOffset());
                    Assert.Equal(text == null ? "Can not parse null value to DateTimeOffset" :
                        $"String '{text}' was not recognized as a valid DateTimeOffset.", error.Message);
                    Assert.False(text.TryParseDateTimeOffset(out var result));
                    Assert.Equal(DateTimeOffset.MinValue, result);
                }
            });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t")]
        public void NullableCasts_RetainBlankInputContract(string text)
        {
            foreach (var type in new[] { typeof(DateTime?), typeof(DateTimeOffset?) })
            {
                Assert.Null(text.Cast(type));
                Assert.True(text.TryCast(type, out var result));
                Assert.Null(result);
            }
            Assert.Throws<FormatException>(() => text.Cast<DateTime>());
            Assert.Throws<FormatException>(() => text.Cast<DateTimeOffset>());
            Assert.False(text.TryCast<DateTime>(out var date));
            Assert.Equal(DateTime.MinValue, date);
            Assert.False(text.TryCast<DateTimeOffset>(out var offset));
            Assert.Equal(DateTimeOffset.MinValue, offset);
        }

        [Fact]
        public void CurrentCulture_StillWinsOverSuppliedProvider()
        {
            WithCulture("en-US", () =>
            {
                var provider = CultureInfo.GetCultureInfo("fr-FR");
                var expected = new DateTime(2026, 1, 2);
                AssertDate(expected, "01/02/2026".ToDateTime(provider));
                AssertOffset(new DateTimeOffset(expected), "01/02/2026".ToDateTimeOffset(provider));
            });
        }

        [Fact]
        public void CustomFormats_UseProviderAndRetainPrecedenceAndMutability()
        {
            var original = StringExtensions.DateTimeFormats.ToArray();
            try
            {
                WithCulture("en-US", () =>
                {
                    Assert.Equal(new[] { "d/M/yyyy", "d-M-yyyy", "yyyy/M/d", "yyyy-M-d", "M/d/yyyy", "M-d-yyyy" }, original);
                    const string text = "date:09 octobre 2026";
                    const string format = "'date:'dd MMMM yyyy";
                    var provider = CultureInfo.GetCultureInfo("fr-FR");
                    Assert.False(text.TryParseDateTime(out _, provider));
                    StringExtensions.DateTimeFormats.Add(format);
                    AssertDate(new DateTime(2026, 10, 9), text.ToDateTime(provider));
                    AssertOffset(new DateTimeOffset(new DateTime(2026, 10, 9)), text.ToDateTimeOffset(provider));
                    StringExtensions.DateTimeFormats.Remove(format);
                    Assert.False(text.TryParseDateTime(out _, provider));
                    Assert.False(text.TryParseDateTimeOffset(out _, provider));

                    StringExtensions.DateTimeFormats.Add("yyyyddMM");
                    AssertDate(new DateTime(2026, 9, 10), "20261009".ToDateTime());
                    AssertOffset(new DateTimeOffset(new DateTime(2026, 9, 10)), "20261009".ToDateTimeOffset());
                    StringExtensions.DateTimeFormats.Remove("yyyyddMM");
                    AssertDate(new DateTime(2026, 10, 9), "20261009".ToDateTime());
                    StringExtensions.DateTimeFormats.Clear();
                    AssertDate(new DateTime(2026, 10, 9), "20261009".ToDateTime());
                });
            }
            finally
            {
                StringExtensions.DateTimeFormats.Clear();
                foreach (var format in original) StringExtensions.DateTimeFormats.Add(format);
            }
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("fr-FR")]
        [InlineData("th-TH")]
        public void DefaultFormats_PreserveLegacyResults(string culture)
        {
            WithCulture(culture, () =>
            {
                foreach (var format in StringExtensions.DateTimeFormats)
                {
                    var text = new DateTime(2026, 10, 9).ToString(format, CultureInfo.InvariantCulture);
                    Assert.True(LegacyDate(text, null, out var date));
                    AssertDate(date, text.ToDateTime());
                    Assert.True(LegacyOffset(text, null, out var offset));
                    AssertOffset(offset, text.ToDateTimeOffset());
                }
            });
        }

        [Fact]
        public void SemanticMachineFormats_IgnoreAmbientCultureAndGlobalFormatMutations()
        {
            var schema = ExpressionSchema.FromType<TemporalEntity>(
                conversionContext: new ExpressionConversionContext(CultureInfo.InvariantCulture, Array.Empty<string>()));
            var original = StringExtensions.DateTimeFormats.ToArray();
            try
            {
                foreach (var culture in new[] { "en-US", "fr-FR", "th-TH" })
                {
                    WithCulture(culture, () =>
                    {
                        StringExtensions.DateTimeFormats.Clear();
                        StringExtensions.DateTimeFormats.Add("'today'HH");
                        foreach (var text in new[] { "20261009", "20261009T152646", "20261009T082646Z" })
                        {
                            Assert.True(Interpreter.AnalyzeExpression<TemporalEntity>(
                                $"Date = '{text}'", schema).IsSemanticallyValid);
                        }
                        foreach (var text in new[] { "20261009T082646Z", "20261009T152646+0700" })
                        {
                            Assert.True(Interpreter.AnalyzeExpression<TemporalEntity>(
                                $"Offset = '{text}'", schema).IsSemanticallyValid);
                        }
                        var missingOffset = Interpreter.AnalyzeExpression<TemporalEntity>(
                            "Offset = '20261009T152646'", schema);
                        Assert.Equal("context-dependent-conversion", Assert.Single(missingOffset.SemanticDiagnostics).Code);
                        var invalid = Interpreter.AnalyzeExpression<TemporalEntity>("Date = '20260230'", schema);
                        Assert.Equal("incompatible-operand", Assert.Single(invalid.SemanticDiagnostics).Code);
                    });
                }
                var customSchema = ExpressionSchema.FromType<TemporalEntity>(
                    conversionContext: new ExpressionConversionContext(
                        CultureInfo.GetCultureInfo("fr-FR"), new[] { "'date:'dd-MMMM-yyyy" }));
                Assert.True(Interpreter.AnalyzeExpression<TemporalEntity>(
                    "Date = 'date:09-octobre-2026'", customSchema).IsSemanticallyValid);
            }
            finally
            {
                StringExtensions.DateTimeFormats.Clear();
                foreach (var format in original) StringExtensions.DateTimeFormats.Add(format);
            }
        }

        private sealed class TemporalEntity
        {
            public DateTime Date { get; set; }
            public DateTimeOffset Offset { get; set; }
        }

        private static bool LegacyDate(string text, IFormatProvider provider, out DateTime result) =>
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
            DateTime.TryParseExact(text, StringExtensions.DateTimeFormats.ToArray(),
                provider ?? CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        private static bool LegacyOffset(string text, IFormatProvider provider, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result) ||
            DateTimeOffset.TryParseExact(text, StringExtensions.DateTimeFormats.ToArray(),
                provider ?? CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        private static void AssertDate(DateTime expected, DateTime actual)
        {
            Assert.Equal(expected.Ticks, actual.Ticks);
            Assert.Equal(expected.Kind, actual.Kind);
        }

        private static void AssertOffset(DateTimeOffset expected, DateTimeOffset actual)
        {
            Assert.Equal(expected.Ticks, actual.Ticks);
            Assert.Equal(expected.Offset, actual.Offset);
            Assert.Equal(expected.UtcTicks, actual.UtcTicks);
        }

        private static void WithCulture(string culture, Action action)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
