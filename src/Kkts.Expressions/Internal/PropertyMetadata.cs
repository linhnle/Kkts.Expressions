using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Kkts.Expressions.Internal
{
    internal static class PropertyMetadata
    {
        private static readonly ConditionalWeakTable<Type, ReadOnlyDictionary<string, string>> Cache =
            new ConditionalWeakTable<Type, ReadOnlyDictionary<string, string>>();
        private static readonly ConditionalWeakTable<Type, ReadOnlyDictionary<string, Type>> MemberTypes =
            new ConditionalWeakTable<Type, ReadOnlyDictionary<string, Type>>();
        private static readonly ConditionalWeakTable<Type, ReadOnlyDictionary<string, string>>.CreateValueCallback Factory =
            CreatePropertyNames;

        public static IReadOnlyDictionary<string, string> GetPropertyNames(Type type)
        {
            return Cache.GetValue(type, Factory);
        }

        public static IReadOnlyDictionary<string, Type> GetMemberTypes(Type type)
        {
            return MemberTypes.GetValue(type, CreateMemberTypes);
        }

        private static ReadOnlyDictionary<string, Type> CreateMemberTypes(Type type)
        {
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Select(member => new
                {
                    Member = member,
                    Type = GetMemberType(member)
                })
                .Where(item => item.Type != null)
                .ToDictionary(item => item.Member.Name, item => item.Type, StringComparer.OrdinalIgnoreCase);
            return new ReadOnlyDictionary<string, Type>(members);
        }

        private static Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo field && !field.IsStatic) return field.FieldType;
            if (member is PropertyInfo property &&
                property.CanRead &&
                property.GetIndexParameters().Length == 0 &&
                property.GetMethod != null &&
                !property.GetMethod.IsStatic)
                return property.PropertyType;
            return null;
        }

        private static ReadOnlyDictionary<string, string> CreatePropertyNames(Type type)
        {
            var properties = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(member => member is PropertyInfo property && property.CanRead || member is FieldInfo)
                .ToDictionary(member => member.Name, member => member.Name, StringComparer.OrdinalIgnoreCase);
            return new ReadOnlyDictionary<string, string>(properties);
        }
    }
}
