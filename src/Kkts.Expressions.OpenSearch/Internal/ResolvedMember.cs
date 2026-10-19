using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal sealed class NestedScope
    {
        public NestedScope(MemberChain chain, string path)
        {
            Chain = chain;
            Path = path;
        }

        public MemberChain Chain { get; }

        public string Path { get; }
    }

    /// <summary>The mapping-derived facts about one member chain; cached per mapping.</summary>
    internal sealed class ResolvedMember
    {
        public MemberChain Chain { get; set; }

        public string MemberPath => Chain.ToString();

        public string Path { get; set; }

        /// <summary>The field used for exact operations; <see langword="null"/> for text-only strings.</summary>
        public string ExactPath { get; set; }

        public Type MemberType { get; set; }

        public bool IsCollection { get; set; }

        /// <summary>The non-nullable scalar type of the member, or of its elements for collections.</summary>
        public Type ValueType { get; set; }

        public bool IsScalarValue { get; set; }

        public bool IsOrdinaryObject { get; set; }

        public bool IsStringLike { get; set; }

        public bool IsNested { get; set; }

        public string PresencePath { get; set; }

        /// <summary>Nested scopes strictly enclosing the member, outermost first.</summary>
        public IReadOnlyList<NestedScope> EnclosingNested { get; set; }

        public bool CaseInsensitive { get; set; }

        public EnumFormat EnumFormat { get; set; }

        public Func<object, JsonNode> Converter { get; set; }

        public StringFieldKind StringKind { get; set; }
    }
}
