using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// The all-or-nothing outcome of a translation: either a value and no diagnostics, or diagnostics and no value.
    /// </summary>
    public sealed class OpenSearchTranslationResult<TOutput> where TOutput : JsonNode
    {
        private static readonly IReadOnlyList<OpenSearchTranslationDiagnostic> NoDiagnostics =
            new ReadOnlyCollection<OpenSearchTranslationDiagnostic>(Array.Empty<OpenSearchTranslationDiagnostic>());

        private OpenSearchTranslationResult(TOutput value, IReadOnlyList<OpenSearchTranslationDiagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
        }

        public bool Succeeded => Value != null;

        /// <summary>The translated DSL, or <see langword="null"/> when translation failed.</summary>
        public TOutput Value { get; }

        public IReadOnlyList<OpenSearchTranslationDiagnostic> Diagnostics { get; }

        /// <summary>Serializes the translated DSL. Throws when translation failed.</summary>
        public string ToJsonString() => GetValueOrThrow().ToJsonString();

        public TOutput GetValueOrThrow()
        {
            if (!Succeeded) throw new OpenSearchTranslationException(Diagnostics);
            return Value;
        }

        internal static OpenSearchTranslationResult<TOutput> Success(TOutput value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new OpenSearchTranslationResult<TOutput>(value, NoDiagnostics);
        }

        internal static OpenSearchTranslationResult<TOutput> Failure(IEnumerable<OpenSearchTranslationDiagnostic> diagnostics)
        {
            var list = diagnostics.ToList();
            if (list.Count == 0) throw new ArgumentException("A failed translation requires diagnostics.", nameof(diagnostics));
            return new OpenSearchTranslationResult<TOutput>(null, new ReadOnlyCollection<OpenSearchTranslationDiagnostic>(list));
        }
    }
}
