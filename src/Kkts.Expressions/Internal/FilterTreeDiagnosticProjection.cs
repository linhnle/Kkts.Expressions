using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal static class FilterTreeDiagnosticProjection
    {
        internal static string AppendPointer(string parent, string segment)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            return parent + "/" + segment.Replace("~", "~0").Replace("/", "~1");
        }

        internal static ExpressionDiagnostic Project(ExpressionDiagnostic diagnostic, string inputPathPrefix = null)
        {
            if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
            var prefix = inputPathPrefix ?? string.Empty;
            if (prefix.Length > 0 && prefix[0] != '/')
                throw new ArgumentException("The input path prefix must be an RFC 6901 pointer.", nameof(inputPathPrefix));

            var path = diagnostic.InputPath ?? string.Empty;
            var inputPath = prefix.Length == 0
                ? path
                : path.Length == 0 ? prefix : prefix + path;
            var restricted = IsRestrictedFieldDiagnostic(diagnostic.Code);
            return new ExpressionDiagnostic(
                diagnostic.Kind,
                diagnostic.Code,
                diagnostic.Code == "property-not-queryable"
                    ? "The field is not permitted for querying."
                    : diagnostic.Message,
                diagnostic.Start,
                diagnostic.Length,
                restricted ? Array.Empty<ExpressionTypeInfo>() : diagnostic.ExpectedTypes,
                restricted ? Array.Empty<ExpressionTypeInfo>() : diagnostic.ActualTypes,
                restricted ? Array.Empty<ExpressionCorrectionSuggestion>() : diagnostic.Suggestions,
                diagnostic.ConfiguredLimit,
                diagnostic.ObservedValue,
                diagnostic.ObservedValueIsLowerBound,
                inputPath);
        }

        internal static IReadOnlyList<ExpressionDiagnostic> Project(
            IEnumerable<ExpressionDiagnostic> diagnostics,
            string inputPathPrefix = null)
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            var projected = new List<ExpressionDiagnostic>();
            foreach (var diagnostic in diagnostics)
                projected.Add(Project(diagnostic, inputPathPrefix));
            return Array.AsReadOnly(projected.ToArray());
        }

        private static bool IsRestrictedFieldDiagnostic(string code) =>
            code == "property-not-queryable" ||
            code == "query-policy-navigation-depth-exceeded" ||
            code == "query-policy-collection-access-denied" ||
            code == "query-policy-operator-denied";
    }
}
