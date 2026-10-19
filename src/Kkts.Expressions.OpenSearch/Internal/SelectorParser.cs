using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal static class SelectorParser
    {
        public static MemberChain Parse(LambdaExpression selector, string argumentName)
        {
            if (selector == null) throw new ArgumentNullException(argumentName);
            if (selector.Parameters.Count != 1)
                throw new ArgumentException("A selector must have exactly one parameter.", argumentName);

            var members = new List<MemberInfo>();
            var current = selector.Body;
            while (current is MemberExpression member)
            {
                if (member.Expression == null || !(member.Member is PropertyInfo || member.Member is FieldInfo))
                    throw new ArgumentException("A selector must be a chain of instance properties or fields, such as x => x.Customer.Name.", argumentName);
                members.Insert(0, member.Member);
                current = member.Expression;
            }

            if (current != selector.Parameters[0])
                throw new ArgumentException("A selector must be a chain of properties or fields rooted at its own parameter, such as x => x.Customer.Name.", argumentName);
            if (members.Count == 0)
                throw new ArgumentException("A selector must access at least one property or field.", argumentName);
            for (var i = 0; i < members.Count - 1; i++)
            {
                var type = MemberChain.GetMemberType(members[i]);
                if (TypeClassifier.IsCollection(type))
                    throw new ArgumentException("A selector cannot access members of a collection; configure element members through Nested(...).", argumentName);
                if (!TypeClassifier.IsOrdinaryObject(type))
                    throw new ArgumentException("A selector can only access members of objects, not members of scalar values such as string or Nullable<T>.", argumentName);
            }

            return new MemberChain(members);
        }
    }
}
