using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
    public class EvaluationResultBase
    {
        private IReadOnlyList<ExpressionDiagnostic> _diagnostics = Array.AsReadOnly(new ExpressionDiagnostic[0]);

        public bool Succeeded { get; internal set; }

        public IEnumerable<string> InvalidProperties { get; internal set; }

        public IEnumerable<string> InvalidOperators { get; internal set; }

        public IEnumerable<string> InvalidVariables { get; internal set; }

        public IEnumerable<string> InvalidOrderByDirections { get; internal set; }

        public IEnumerable<string> InvalidValues { get; internal set; }

        public Exception Exception { get; internal set; }

        /// <summary>Immutable policy or validation diagnostics associated with this result.</summary>
        public IReadOnlyList<ExpressionDiagnostic> Diagnostics
        {
            get => _diagnostics;
            internal set => _diagnostics = Array.AsReadOnly(
                (value ?? Enumerable.Empty<ExpressionDiagnostic>()).ToArray());
        }
    }

    public class EvaluationResult : EvaluationResultBase
    {
        public LambdaExpression Result { get; internal set; }

        internal EvaluationResult<T, TResult> ToGeneric<T, TResult>()
        {
            return new EvaluationResult<T, TResult>
            {
                Result = (Expression<Func<T, TResult>>)Result,
                Succeeded = Succeeded,
                Exception = Exception,
                InvalidProperties = InvalidProperties,
                InvalidOperators = InvalidOperators,
                InvalidVariables = InvalidVariables,
                InvalidValues = InvalidValues,
                InvalidOrderByDirections = InvalidOrderByDirections,
                Diagnostics = Diagnostics
            };
        }
    }

    public class EvaluationResult<T> : EvaluationResultBase
    {
        public T Result { get; internal set; }
    }

    public class EvaluationResult<T, TResult> : EvaluationResultBase
    {
        public Expression<Func<T, TResult>> Result { get; internal set; }
    }
}
