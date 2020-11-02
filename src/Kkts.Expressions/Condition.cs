using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions
{
    public class ConditionBase
    {
        internal ConditionBase()
        {
        }

        public OrderByClause OrderByClause { get; internal set; }

        public ErrorInfo Error { get; internal set; }

        public bool IsValid { get; internal set; }

        public sealed class ErrorInfo
        {
            internal ErrorInfo() { }

            public ICollection<Exception> Exceptions { get; internal set; }

            public EvaluationResultBase EvaluationResult { get; internal set; }
        }
    }

    public class Condition : ConditionBase
    {
        internal Condition()
        {
        }

        public ICollection<LambdaExpression> Predicates { get; internal set; }
    }

    public class Condition<T> : ConditionBase
    {
        internal Condition()
        {
        }

        public ICollection<Expression<Func<T, bool>>> Predicates { get; internal set; }

        public static explicit operator Condition<T>(Condition op)
        {
            return op is null ? null : new Condition<T>
            {
                Error = op.Error,
                IsValid = op.IsValid,
                OrderByClause = op.OrderByClause,
                Predicates = op.Predicates.Cast<Expression<Func<T, bool>>>().ToList()
            };
        }
    }
}
