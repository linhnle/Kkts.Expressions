using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Represents the success or failure of a filter operation with structured diagnostics.</summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    public sealed class FilterOperationResult<T>
    {
        private FilterOperationResult(T result, IEnumerable<ExpressionDiagnostic> diagnostics, bool isTruncated)
        {
            Result = result;
            Diagnostics = Array.AsReadOnly((diagnostics ?? Enumerable.Empty<ExpressionDiagnostic>()).ToArray());
            IsTruncated = isTruncated;
            Succeeded = Result != null && Diagnostics.Count == 0;
        }

        /// <summary>Whether the operation completed successfully without diagnostics.</summary>
        public bool Succeeded { get; }

        /// <summary>The successful result; null on failure.</summary>
        public T Result { get; }

        /// <summary>Structured diagnostics explaining why an operation failed.</summary>
        public IReadOnlyList<ExpressionDiagnostic> Diagnostics { get; }

        /// <summary>Whether additional diagnostics were omitted after reaching an implementation limit.</summary>
        public bool IsTruncated { get; }

        internal static FilterOperationResult<T> Success(T result)
        {
            if (ReferenceEquals(result, null)) throw new ArgumentNullException(nameof(result));
            return new FilterOperationResult<T>(
                result,
                Enumerable.Empty<ExpressionDiagnostic>(),
                false);
        }

        internal static FilterOperationResult<T> Failure(
            IEnumerable<ExpressionDiagnostic> diagnostics,
            bool isTruncated = false)
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            var values = diagnostics.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("A failed operation must include at least one diagnostic.", nameof(diagnostics));
            if (values.Any(diagnostic => diagnostic == null))
                throw new ArgumentException("Diagnostics cannot contain null.", nameof(diagnostics));
            return new FilterOperationResult<T>(default(T), values, isTruncated);
        }
    }
}
