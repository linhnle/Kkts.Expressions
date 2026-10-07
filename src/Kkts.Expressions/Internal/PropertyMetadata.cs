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
        private static readonly ConditionalWeakTable<Type, ReadOnlyDictionary<string, string>>.CreateValueCallback Factory =
            CreatePropertyNames;

        public static IReadOnlyDictionary<string, string> GetPropertyNames(Type type)
        {
            return Cache.GetValue(type, Factory);
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
