using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Thrown by throwing translation APIs; carries the same diagnostics as the failed result.
    /// </summary>
    public sealed class OpenSearchTranslationException : Exception
    {
        public OpenSearchTranslationException(IReadOnlyList<OpenSearchTranslationDiagnostic> diagnostics)
            : base(CreateMessage(diagnostics))
        {
            Diagnostics = diagnostics ?? Array.Empty<OpenSearchTranslationDiagnostic>();
        }

        public IReadOnlyList<OpenSearchTranslationDiagnostic> Diagnostics { get; }

        private static string CreateMessage(IReadOnlyList<OpenSearchTranslationDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return "The expression could not be translated to an OpenSearch query.";
            return "The expression could not be translated to an OpenSearch query. " +
                string.Join(" ", diagnostics.Select(d => d.Code + ": " + d.Message));
        }
    }
}
