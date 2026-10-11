using System;
using System.Collections;

namespace Kkts.Expressions.Internal
{
    internal static class QueryPolicyFieldMetadata
    {
        internal static bool IsScalar(Type type)
        {
            if (type == null) return false;
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive ||
                type.IsEnum ||
                NumericOperands.IsNumeric(type) ||
                type == typeof(string) ||
                type == typeof(Guid) ||
                type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(TimeSpan);
        }

        internal static bool IsCollection(Type type)
        {
            return type != null &&
                type != typeof(string) &&
                typeof(IEnumerable).IsAssignableFrom(type);
        }

        internal static ComparisonOperator NormalizeComparisonOperator(string operatorName)
        {
            return operatorName.GetComparisonOperator();
        }
    }
}
