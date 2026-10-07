using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
    public class EvaluationResultBase
    {
        public bool Succeeded { get; internal set; }

        public IEnumerable<string> InvalidProperties { get; internal set; }

        public IEnumerable<string> InvalidOperators { get; internal set; }

        public IEnumerable<string> InvalidVariables { get; internal set; }

        public IEnumerable<string> InvalidOrderByDirections { get; internal set; }

        public IEnumerable<string> InvalidValues { get; internal set; }

        public Exception Exception { get; internal set; }
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
                InvalidOrderByDirections = InvalidOrderByDirections
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
