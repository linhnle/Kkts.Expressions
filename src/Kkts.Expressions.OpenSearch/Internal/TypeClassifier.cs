using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal static class TypeClassifier
    {
        private static readonly HashSet<Type> BuiltInScalars = new HashSet<Type>
        {
            typeof(bool), typeof(char), typeof(string),
            typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid)
        };

        public static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

        public static bool IsNullable(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        /// <summary>A value type or a type that maps to one indexed value, as opposed to an object or collection.</summary>
        public static bool IsScalar(Type type, ICollection<Type> convertedTypes = null)
        {
            var underlying = Underlying(type);
            if (BuiltInScalars.Contains(underlying) || underlying.IsEnum) return true;
            if (convertedTypes != null && convertedTypes.Contains(underlying)) return true;
            // Unknown structs (for example DateOnly) are values, not objects with indexed members.
            return underlying.IsValueType || underlying.IsPrimitive;
        }

        public static bool IsIntegral(Type type)
        {
            type = Underlying(type);
            return type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
        }

        public static bool IsFloatingPoint(Type type)
        {
            type = Underlying(type);
            return type == typeof(float) || type == typeof(double);
        }

        public static bool IsNumeric(Type type) => IsIntegral(type) || IsFloatingPoint(type) || Underlying(type) == typeof(decimal);

        public static bool IsStringLike(Type type)
        {
            type = Underlying(type);
            return type == typeof(string) || type == typeof(char);
        }

        public static Type GetCollectionElementType(Type type)
        {
            if (type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();
            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerable != null) return enumerable.GetGenericArguments()[0];
            return typeof(IEnumerable).IsAssignableFrom(type) ? typeof(object) : null;
        }

        public static bool IsCollection(Type type) => GetCollectionElementType(type) != null;

        /// <summary>An object whose members are indexed as sub-fields: not a scalar and not a collection.</summary>
        public static bool IsOrdinaryObject(Type type, ICollection<Type> convertedTypes = null)
        {
            return !IsScalar(type, convertedTypes) && !IsCollection(type);
        }
    }
}
