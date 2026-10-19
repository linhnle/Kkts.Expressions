using System;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal sealed class FieldRegistration
    {
        public string Name { get; set; }

        public StringFieldKind StringKind { get; set; }

        public bool? CaseInsensitive { get; set; }

        public EnumFormat? EnumFormat { get; set; }

        public Func<object, JsonNode> Converter { get; set; }
    }
}
