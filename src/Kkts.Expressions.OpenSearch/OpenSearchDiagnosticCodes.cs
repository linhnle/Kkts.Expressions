namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Stable diagnostic codes reported by OpenSearch translation.
    /// </summary>
    public static class OpenSearchDiagnosticCodes
    {
        public const string UnsupportedExpression = "opensearch-unsupported-expression";
        public const string UnsupportedMethod = "opensearch-unsupported-method";
        public const string UnmappedField = "opensearch-unmapped-field";
        public const string TextFieldExactMatch = "opensearch-text-field-exact-match";
        public const string UnsupportedValue = "opensearch-unsupported-value";
        public const string FlattenedCollection = "opensearch-flattened-collection";
        public const string ResultWindowExceeded = "opensearch-result-window-exceeded";
        public const string InvalidCondition = "opensearch-invalid-condition";
        public const string UnsupportedConversion = "opensearch-unsupported-conversion";
        public const string PresenceMarkerRequired = "opensearch-presence-marker-required";
    }
}
