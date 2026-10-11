using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class AutocompleteDocumentationExamplesTest
    {
        [Fact]
        public void GuideMetadataAndOptionsExamplesCompileAndRemainAdvisory()
        {
            var options = new ExpressionCompletionOptions(maxResults: 50);
            var schema = new QuerySchema<Product>()
                .Field("Status", product => product.Status)
                .Field("Price", product => product.Price)
                .Build();
            var hints = new ExpressionValueSuggestionSchema(schema, new[]
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>(
                    "Status",
                    new[]
                    {
                        new ExpressionValueSuggestion(FilterValue.String("Active")),
                        new ExpressionValueSuggestion(FilterValue.String("Pending")),
                        new ExpressionValueSuggestion(FilterValue.String("Closed"))
                    })
            });
            Assert.Equal(50, options.MaxResults);
            Assert.Equal(3, hints.Fields["Status"].Count);
            Assert.True(Interpreter.AnalyzeExpression<Product>("Status = 'Unlisted'", schema).IsSemanticallyValid);
            var result = Interpreter.CompleteExpression(
                "Status = ", 9, schema, valueSuggestions: hints);
            var active = result.Items.First(item => item.InsertionText == "'Active'");
            var edited = "Status = ".Substring(0, active.Start) + active.InsertionText +
                "Status = ".Substring(active.Start + active.Length);
            Assert.Equal("Status = 'Active'", edited);
        }

        [Fact]
        public void GuideSliceExamplePreservesExactSurroundingText()
        {
            var item = new ExpressionCompletionItem(
                ExpressionCompletionKind.Value, "Active", "'Active'", 9, 6);
            Assert.Equal("Status = 'Active' and Price > 1",
                Apply("Status = 'AcZZ' and Price > 1", item));
        }

        [Fact]
        public void GuideNestedComputedAndVariableExamplesUseOnlyDeclaredMetadata()
        {
            var legacy = ExpressionSchema.FromType<NavigationProduct>(
                validProperties: new[] { "Customer.Name" });
            var nested = Interpreter.CompleteExpression("Customer.", 9, legacy);
            Assert.Equal("Customer.Name", Assert.Single(nested.Items).InsertionText);

            var projected = new QuerySchema<Product>()
                .Field("total", product => product.Price * 2, displayName: "Total")
                .Build();
            var totals = Interpreter.CompleteExpression("tot", 3, projected);
            var item = Assert.Single(totals.Items);
            Assert.Equal("Total", item.Label);
            Assert.Equal("total", item.InsertionText);
            Assert.Equal(typeof(decimal), item.TypeInfo.ClrType);

            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
                new ExpressionVariableDefinition("startOfMonth", typeof(DateTime))
            });
            var schema = new QuerySchema<NavigationProduct>()
                .Field("CreatedAt", product => product.CreatedAt).Build();
            Assert.Equal(new[] { "$startOfMonth", "$utcnow" },
                Interpreter.CompleteExpression("CreatedAt > $", 13, schema, variables).Items
                    .Select(candidate => candidate.InsertionText));
        }

        [Fact]
        public void ReadmeConfiguredCompletionExampleCompiles()
        {
            var completionSchema = new QuerySchema<Data>()
                .Field("Name", data => data.Name, allowedOperators: new[] { ComparisonOperator.Equal })
                .Build();
            var hints = new ExpressionValueSuggestionSchema(completionSchema, new[]
            {
                new KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>("Name", new[]
                {
                    new ExpressionValueSuggestion(FilterValue.String("Active"))
                })
            });
            var completion = Interpreter.CompleteExpression(
                "Name = ", 7, completionSchema, valueSuggestions: hints);
            Assert.Contains(completion.Items, item => item.InsertionText == "'Active'");
        }

        private static string Apply(string text, ExpressionCompletionItem item) =>
            text.Substring(0, item.Start) + item.InsertionText +
            text.Substring(item.Start + item.Length);

        public sealed class Product
        {
            public string Status { get; set; } = "";
            public decimal Price { get; set; }
        }

        public sealed class NavigationProduct
        {
            public Customer Customer { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public sealed class Customer
        {
            public string Name { get; set; }
        }

        public sealed class Data { public string Name { get; set; } = ""; }
    }
}
