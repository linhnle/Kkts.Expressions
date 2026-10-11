using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionConversionContextTest
    {
        [Fact]
        public void Context_CopiesCultureAndFormatInputsIntoReadOnlySnapshots()
        {
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
            var formats = new List<string> { "yyyy-MM-dd" };
            var context = new ExpressionConversionContext(culture, formats);
            culture.NumberFormat.NumberDecimalSeparator = "!";
            formats[0] = "MM/dd/yyyy";
            formats.Add("dd.MM.yyyy");

            Assert.Equal(",", context.Culture.NumberFormat.NumberDecimalSeparator);
            Assert.Equal(new[] { "yyyy-MM-dd" }, context.DateTimeFormats);
            Assert.True(context.Culture.IsReadOnly);
            Assert.Throws<InvalidOperationException>(
                () => context.Culture.DateTimeFormat.ShortDatePattern = "MM/dd/yyyy");
        }

        [Fact]
        public void Schema_UsesFixedInvariantConversionContextByDefault()
        {
            var schema = ExpressionSchema.FromType<TestEntity>();

            Assert.Equal(CultureInfo.InvariantCulture.Name, schema.ConversionContext.Culture.Name);
            Assert.Equal(
                new[] { "d/M/yyyy", "d-M-yyyy", "yyyy/M/d", "yyyy-M-d", "M/d/yyyy", "M-d-yyyy" },
                schema.ConversionContext.DateTimeFormats);
        }

        [Fact]
        public void Context_RejectsMissingInputsAndInvalidFormats()
        {
            Assert.Throws<ArgumentNullException>(() => new ExpressionConversionContext(null, Array.Empty<string>()));
            Assert.Throws<ArgumentNullException>(() => new ExpressionConversionContext(CultureInfo.InvariantCulture, null));
            Assert.Throws<ArgumentException>(() => new ExpressionConversionContext(
                CultureInfo.InvariantCulture,
                new[] { "" }));
        }
    }
}
