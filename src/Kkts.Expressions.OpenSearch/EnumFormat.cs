namespace Kkts.Expressions.OpenSearch
{
    /// <summary>How enum values are written into queries; must match how documents are indexed.</summary>
    public enum EnumFormat
    {
        /// <summary>The enum's underlying numeric value.</summary>
        Number,

        /// <summary>The enum member name, such as <c>"Active"</c>.</summary>
        Name
    }
}
