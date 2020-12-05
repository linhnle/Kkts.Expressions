using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    public static class OrderByInfoExtensions
    {
        public static EvaluationResult<OrderByClause> TryBuildOrderByClause(this OrderByInfo orderByInfo, Type type, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
        {
            if (orderByInfo is null) throw new ArgumentNullException(nameof(orderByInfo));
            if (type is null) throw new ArgumentNullException(nameof(type));

            var arg = new BuildArgument
            {
                ValidProperties = validProperties,
                EvaluationType = type,
                PropertyMapping = propertyMapping
            };

            return OrderByParser.Parse(new[] { orderByInfo }, arg);
        }

        public static EvaluationResult<OrderByClause> TryBuildOrderByClause<T>(this OrderByInfo orderByInfo, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
        {
            return TryBuildOrderByClause(orderByInfo, typeof(T), validProperties, propertyMapping);
        }

        public static EvaluationResult<OrderByClause> TryBuildOrderByClause(this IEnumerable<OrderByInfo> orderByInfos, Type type, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
        {
            if (orderByInfos is null) throw new ArgumentNullException(nameof(orderByInfos));
            if (type is null) throw new ArgumentNullException(nameof(type));

            var arg = new BuildArgument
            {
                ValidProperties = validProperties,
                EvaluationType = type,
                PropertyMapping = propertyMapping
            };

            return OrderByParser.Parse(orderByInfos.ToArray(), arg);
        }

        public static EvaluationResult<OrderByClause> TryBuildOrderByClause<T>(this IEnumerable<OrderByInfo> orderByInfos, IEnumerable<string> validProperties = null, IDictionary<string, string> propertyMapping = null)
        {
            return TryBuildOrderByClause(orderByInfos, typeof(T), validProperties, propertyMapping);
        }
    }
}
