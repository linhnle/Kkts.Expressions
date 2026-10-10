using Kkts.Expressions;

internal sealed class SampleMetadata
{
    internal SampleMetadata()
    {
        var schema = ExpressionSchema.FromType<Product>(
            validProperties: new[] { "Price", "Status", "CreatedAt", "Customer.Name" });
        Context = new ExpressionQueryContext(schema, new QueryPolicy(
            maxExpressionLength: 4096, maxParenthesisDepth: 16, maxAtomicConditions: 32,
            maxInItems: 16, maxNavigationDepth: 1, allowCollectionAccess: false,
            allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
            {
                ["Price"] = new[]
                {
                    ComparisonOperator.Equal, ComparisonOperator.NotEqual, ComparisonOperator.GreaterThan,
                    ComparisonOperator.GreaterThanOrEqual, ComparisonOperator.LessThan,
                    ComparisonOperator.LessThanOrEqual, ComparisonOperator.In
                }
            }));
        Variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
            new ExpressionVariableDefinition("startOfMonth", typeof(DateTime))
        });
        Hints = new ExpressionValueSuggestionSchema(schema, new[]
        {
            new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Status", new[]
            {
                new ExpressionValueSuggestion(FilterValue.String("Active")),
                new ExpressionValueSuggestion(FilterValue.String("Pending")),
                new ExpressionValueSuggestion(FilterValue.String("Closed"))
            })
        });
    }

    internal ExpressionQueryContext Context { get; }
    internal ExpressionVariableSchema Variables { get; }
    internal ExpressionValueSuggestionSchema Hints { get; }

    internal sealed class Product
    {
        public decimal Price { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public Customer Customer { get; set; } = new();
    }

    internal sealed class Customer { public string Name { get; set; } = ""; }
}
