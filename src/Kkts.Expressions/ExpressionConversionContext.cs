using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>
    /// Immutable culture and date-format inputs for deterministic semantic literal analysis.
    /// </summary>
    public sealed class ExpressionConversionContext
    {
        private static readonly string[] DefaultDateFormats =
        {
            "d/M/yyyy", "d-M-yyyy", "yyyy/M/d", "yyyy-M-d", "M/d/yyyy", "M-d-yyyy"
        };

        internal static readonly ExpressionConversionContext Default =
            new ExpressionConversionContext(CultureInfo.InvariantCulture, DefaultDateFormats);

        public ExpressionConversionContext(CultureInfo culture, IEnumerable<string> dateTimeFormats)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));
            if (dateTimeFormats == null) throw new ArgumentNullException(nameof(dateTimeFormats));

            Culture = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
            var formats = dateTimeFormats
                .Select(format => string.IsNullOrWhiteSpace(format)
                    ? throw new ArgumentException("Date/time formats cannot be null or whitespace.", nameof(dateTimeFormats))
                    : format)
                .ToArray();
            DateTimeFormats = Array.AsReadOnly(formats);
        }

        /// <summary>A read-only clone of the supplied culture.</summary>
        public CultureInfo Culture { get; }

        /// <summary>A read-only snapshot of supported explicit date/time formats.</summary>
        public IReadOnlyList<string> DateTimeFormats { get; }
    }
}
