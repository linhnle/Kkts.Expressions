using System;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Describes how a string field is mapped in OpenSearch, which determines the field used for exact operations.
    /// </summary>
    public sealed class StringFieldKind : IEquatable<StringFieldKind>
    {
        private StringFieldKind(bool isKeyword, string keywordSubfield, bool isTextOnly)
        {
            IsKeyword = isKeyword;
            KeywordSubfield = keywordSubfield;
            IsTextOnly = isTextOnly;
        }

        /// <summary>A <c>keyword</c> field; exact operations target the field itself.</summary>
        public static StringFieldKind Keyword { get; } = new StringFieldKind(true, null, false);

        /// <summary>A <c>text</c> field without a keyword subfield; exact operations fail translation.</summary>
        public static StringFieldKind TextOnly { get; } = new StringFieldKind(false, null, true);

        /// <summary>The OpenSearch dynamic-mapping default: <c>text</c> with a <c>keyword</c> subfield.</summary>
        public static StringFieldKind Default { get; } = Text("keyword");

        public bool IsKeyword { get; }

        public bool IsTextOnly { get; }

        /// <summary>The keyword subfield name for text fields, or <see langword="null"/>.</summary>
        public string KeywordSubfield { get; }

        /// <summary>A <c>text</c> field whose exact operations target the named keyword subfield.</summary>
        public static StringFieldKind Text(string keywordSubfield = "keyword")
        {
            if (!Internal.FieldPaths.IsValidSegment(keywordSubfield))
                throw new ArgumentException("A keyword subfield must be a non-empty name without dots or whitespace.", nameof(keywordSubfield));
            return new StringFieldKind(false, keywordSubfield, false);
        }

        internal string GetExactPath(string fieldPath)
        {
            if (IsKeyword) return fieldPath;
            if (IsTextOnly) return null;
            return fieldPath + "." + KeywordSubfield;
        }

        public bool Equals(StringFieldKind other)
        {
            return other != null && IsKeyword == other.IsKeyword && IsTextOnly == other.IsTextOnly && KeywordSubfield == other.KeywordSubfield;
        }

        public override bool Equals(object obj) => Equals(obj as StringFieldKind);

        public override int GetHashCode() => (IsKeyword ? 1 : 0) ^ (IsTextOnly ? 2 : 0) ^ (KeywordSubfield?.GetHashCode() ?? 0);

        public override string ToString() => IsKeyword ? "keyword" : IsTextOnly ? "text" : "text+" + KeywordSubfield;
    }
}
