using System;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Internal;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>Configures how one member chain is indexed.</summary>
    public sealed class OpenSearchFieldBuilder
    {
        private readonly string _member;
        private readonly Type _valueType;
        private readonly bool _isStringLike;
        private readonly bool _isScalar;

        internal OpenSearchFieldBuilder(MemberChain chain, Type valueType, bool isScalar)
        {
            _member = chain.ToString();
            _valueType = valueType;
            _isScalar = isScalar;
            _isStringLike = TypeClassifier.IsStringLike(valueType);
        }

        internal FieldRegistration Registration { get; } = new FieldRegistration();

        /// <summary>Replaces the whole OpenSearch field path for this member, such as <c>meta.created</c>.</summary>
        public OpenSearchFieldBuilder Name(string path)
        {
            if (!FieldPaths.IsValidPath(path))
                throw new ArgumentException("A field path must be a non-empty dotted path without empty segments or whitespace.", nameof(path));
            if (Registration.Name != null) throw Conflict("name");
            Registration.Name = path;
            return this;
        }

        /// <summary>The field is a <c>keyword</c> field.</summary>
        public OpenSearchFieldBuilder Keyword() => StringField(StringFieldKind.Keyword);

        /// <summary>The field is <c>text</c> with the named keyword subfield used for exact operations.</summary>
        public OpenSearchFieldBuilder Text(string keywordSubfield = "keyword") => StringField(StringFieldKind.Text(keywordSubfield));

        /// <summary>The field is <c>text</c> without a keyword subfield; exact operations fail translation.</summary>
        public OpenSearchFieldBuilder TextOnly() => StringField(StringFieldKind.TextOnly);

        public OpenSearchFieldBuilder StringField(StringFieldKind kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (!_isStringLike) throw Mismatch("a string field kind", "string");
            if (Registration.StringKind != null) throw Conflict("string field kind");
            Registration.StringKind = kind;
            return this;
        }

        /// <summary>Exact string operations use OpenSearch case-insensitive matching.</summary>
        public OpenSearchFieldBuilder CaseInsensitive(bool caseInsensitive = true)
        {
            if (!_isStringLike) throw Mismatch("case-insensitive matching", "string");
            if (Registration.CaseInsensitive != null) throw Conflict("case sensitivity");
            Registration.CaseInsensitive = caseInsensitive;
            return this;
        }

        public OpenSearchFieldBuilder EnumFormat(EnumFormat format)
        {
            if (!_valueType.IsEnum) throw Mismatch("an enum format", "enum");
            if (Registration.EnumFormat != null) throw Conflict("enum format");
            Registration.EnumFormat = format;
            return this;
        }

        /// <summary>Formats values compared with this field; takes precedence over type converters.</summary>
        public OpenSearchFieldBuilder Converter<TValue>(Func<TValue, JsonNode> converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (typeof(TValue) != _valueType)
                throw new InvalidOperationException($"A converter for '{_member}' must accept '{_valueType}', not '{typeof(TValue)}'.");
            if (!_isScalar) throw Mismatch("a value converter", "scalar");
            if (Registration.Converter != null) throw Conflict("converter");
            Registration.Converter = value => converter((TValue)value);
            return this;
        }

        private InvalidOperationException Conflict(string setting) =>
            new InvalidOperationException($"The {setting} of '{_member}' is already configured.");

        private InvalidOperationException Mismatch(string setting, string kind) =>
            new InvalidOperationException($"'{_member}' is not a {kind} field, so {setting} cannot be configured.");
    }
}
