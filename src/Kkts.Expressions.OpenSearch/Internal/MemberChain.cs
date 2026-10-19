using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>
    /// A sequence of property or field accesses starting at the mapped root type. Crossing a collection is implied when
    /// a member's type is a collection and the next member belongs to its element type.
    /// </summary>
    internal sealed class MemberChain : IEquatable<MemberChain>
    {
        public static readonly MemberChain Empty = new MemberChain(Array.Empty<MemberInfo>());

        private readonly int _hash;

        public MemberChain(IReadOnlyList<MemberInfo> members)
        {
            Members = members;
            var hash = 17;
            foreach (var member in members) hash = unchecked(hash * 31 + Identity(member).GetHashCode());
            _hash = hash;
        }

        public IReadOnlyList<MemberInfo> Members { get; }

        public int Count => Members.Count;

        public MemberInfo Last => Members[Members.Count - 1];

        public Type MemberType => GetMemberType(Last);

        public MemberChain Append(MemberInfo member) => new MemberChain(Members.Concat(new[] { member }).ToArray());

        public MemberChain Concat(MemberChain other) => new MemberChain(Members.Concat(other.Members).ToArray());

        public MemberChain Prefix(int count) => new MemberChain(Members.Take(count).ToArray());

        public bool StartsWith(MemberChain prefix)
        {
            if (prefix.Count > Count) return false;
            for (var i = 0; i < prefix.Count; i++)
                if (!Identity(prefix.Members[i]).Equals(Identity(Members[i]))) return false;
            return true;
        }

        public static Type GetMemberType(MemberInfo member)
        {
            return member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        }

        public bool Equals(MemberChain other)
        {
            if (other == null || other._hash != _hash || other.Count != Count) return false;
            for (var i = 0; i < Count; i++)
                if (!Identity(Members[i]).Equals(Identity(other.Members[i]))) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as MemberChain);

        public override int GetHashCode() => _hash;

        public override string ToString() => string.Join(".", Members.Select(m => m.Name));

        // Members obtained through derived types differ only by ReflectedType, so compare declarations.
        private static (Type, Module, int) Identity(MemberInfo member) => (member.DeclaringType, member.Module, member.MetadataToken);
    }
}
