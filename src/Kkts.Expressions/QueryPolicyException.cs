using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Represents a query rejected by an explicitly configured query policy.</summary>
    public sealed class QueryPolicyException : InvalidOperationException
    {
        public QueryPolicyException(IEnumerable<ExpressionDiagnostic> diagnostics)
            : base("The query violates the configured query policy.")
        {
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        /// <summary>Immutable policy diagnostics explaining why the query was rejected.</summary>
        public IReadOnlyList<ExpressionDiagnostic> Diagnostics { get; }
    }
}
