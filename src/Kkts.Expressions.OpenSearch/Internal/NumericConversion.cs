using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal readonly struct SourceInterval
    {
        public SourceInterval(BigInteger lower, BigInteger upper)
        {
            Lower = lower;
            Upper = upper;
        }

        public BigInteger Lower { get; }

        public BigInteger Upper { get; }
    }

    /// <summary>
    /// A chain of conversions applied to a field operand, such as <c>(double)x.LongValue</c>. Integral sources are rewritten
    /// into exact source-value intervals by searching the source domain with CLR conversion semantics.
    /// </summary>
    internal sealed class NumericConversion
    {
        private readonly IReadOnlyList<Type> _steps;

        private NumericConversion(Type sourceType, IReadOnlyList<Type> steps)
        {
            SourceType = sourceType;
            _steps = steps;
            TargetType = steps.Count == 0 ? sourceType : steps[steps.Count - 1];
            if (!IsIdentity)
            {
                ValueConversions.TryGetIntegralRange(sourceType, out var min, out var max);
                Min = min;
                Max = max;
            }
        }

        public Type SourceType { get; }

        public Type TargetType { get; }

        /// <summary>Only nullable lifting: values compare exactly as the field's own values.</summary>
        public bool IsIdentity => _steps.Count == 0;

        public BigInteger Min { get; }

        public BigInteger Max { get; }

        public static bool TryCreate(Type memberValueType, IReadOnlyList<UnaryExpression> conversionsInnerFirst, out NumericConversion conversion, out string error)
        {
            conversion = null;
            error = null;
            var steps = new List<Type>();
            var source = TypeClassifier.Underlying(memberValueType);
            var current = source;
            foreach (var node in conversionsInnerFirst)
            {
                // The C# compiler represents built-in integral-to-decimal conversions as calls to decimal.op_Implicit.
                if (node.Method != null && !(node.Method.DeclaringType == typeof(decimal) && node.Method.Name == "op_Implicit"))
                {
                    error = "user-defined conversion operators cannot be translated";
                    return false;
                }

                if (TypeClassifier.IsNullable(node.Operand.Type) && !TypeClassifier.IsNullable(node.Type))
                {
                    error = "converting a nullable field to a non-nullable type throws for null values in .NET and cannot be translated";
                    return false;
                }

                var target = TypeClassifier.Underlying(node.Type);
                if (target == current) continue;
                if (!IsSupportedStep(current, target))
                {
                    error = $"no exact OpenSearch rewrite exists for a conversion from {current.Name} to {target.Name}";
                    return false;
                }

                steps.Add(target);
                current = target;
            }

            conversion = new NumericConversion(source, steps);
            return true;
        }

        private static bool IsSupportedStep(Type from, Type to)
        {
            if (to.IsEnum) return false;
            var fromNumeric = from.IsEnum ? Enum.GetUnderlyingType(from) : from;
            if (!ValueConversions.TryGetIntegralRange(fromNumeric, out var fromMin, out var fromMax)) return false;
            if (to == typeof(float) || to == typeof(double) || to == typeof(decimal)) return true;
            if (to == fromNumeric) return true;
            return ValueConversions.TryGetIntegralRange(to, out var toMin, out var toMax) && toMin <= fromMin && fromMax <= toMax;
        }

        /// <summary>The converted value the CLR produces for source value <paramref name="source"/>.</summary>
        public object Apply(BigInteger source)
        {
            object value = ValueConversions.FromBigInteger(source, SourceType);
            foreach (var step in _steps)
            {
                if (!ValueConversions.TryConvert(value, step, false, out value))
                    throw new InvalidOperationException("A validated conversion step failed.");
            }

            return value;
        }

        public object ToSourceValue(BigInteger source) => ValueConversions.FromBigInteger(source, SourceType);

        public int Compare(BigInteger source, object target) => ((IComparable)Apply(source)).CompareTo(target);

        /// <summary>Source values v with converted(v) == target, or null when none exist.</summary>
        public SourceInterval? Equal(object target)
        {
            var lower = FirstWhere(v => Compare(v, target) >= 0);
            if (lower == null) return null;
            var above = FirstWhere(v => Compare(v, target) > 0);
            var upper = above == null ? Max : above.Value - 1;
            return lower.Value <= upper ? new SourceInterval(lower.Value, upper) : (SourceInterval?)null;
        }

        public SourceInterval? Compare(ExpressionType op, object target)
        {
            switch (op)
            {
                case ExpressionType.Equal:
                    return Equal(target);
                case ExpressionType.LessThan:
                    return Below(FirstWhere(v => Compare(v, target) >= 0));
                case ExpressionType.LessThanOrEqual:
                    return Below(FirstWhere(v => Compare(v, target) > 0));
                case ExpressionType.GreaterThan:
                    return From(FirstWhere(v => Compare(v, target) > 0));
                case ExpressionType.GreaterThanOrEqual:
                    return From(FirstWhere(v => Compare(v, target) >= 0));
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        /// <summary>Unions intervals, coalescing overlapping or adjacent ones, in ascending order.</summary>
        public static IReadOnlyList<SourceInterval> Union(IEnumerable<SourceInterval> intervals)
        {
            var result = new List<SourceInterval>();
            foreach (var interval in intervals.OrderBy(i => i.Lower).ThenBy(i => i.Upper))
            {
                if (result.Count > 0 && interval.Lower <= result[result.Count - 1].Upper + 1)
                {
                    var last = result[result.Count - 1];
                    result[result.Count - 1] = new SourceInterval(last.Lower, BigInteger.Max(last.Upper, interval.Upper));
                }
                else
                {
                    result.Add(interval);
                }
            }

            return result;
        }

        private SourceInterval? Below(BigInteger? firstExcluded)
        {
            if (firstExcluded == null) return new SourceInterval(Min, Max);
            return firstExcluded.Value == Min ? (SourceInterval?)null : new SourceInterval(Min, firstExcluded.Value - 1);
        }

        private SourceInterval? From(BigInteger? first) => first == null ? (SourceInterval?)null : new SourceInterval(first.Value, Max);

        // Binary search for the smallest source value satisfying a predicate that is monotone over the source domain.
        private BigInteger? FirstWhere(Func<BigInteger, bool> predicate)
        {
            if (!predicate(Max)) return null;
            var low = Min;
            var high = Max;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (predicate(middle)) high = middle;
                else low = middle + 1;
            }

            return low;
        }
    }
}
