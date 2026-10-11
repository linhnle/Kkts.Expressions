using System;
using System.Collections.Generic;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionPermissionTest
    {
        [Fact]
        public void PublicFieldsUseOnlyExposedFilterableResultMetadata()
        {
            var schema = new QuerySchema<Product>()
                .Field("total", product => product.Price * 2, displayName: "Total", description: "Public total")
                .Field("sortOnly", product => product.Secret, canFilter: false, description: "hidden")
                .Field("disabled", product => product.Price, allowedOperators: Array.Empty<ComparisonOperator>())
                .Build();
            var result = Complete("|", schema);
            var item = Assert.Single(result.Items);
            Assert.Equal("Total", item.Label);
            Assert.Equal("total", item.InsertionText);
            Assert.Equal("Public total", item.Description);
            Assert.Equal(typeof(decimal), item.TypeInfo.ClrType);
            Assert.Empty(Complete("total.|", schema).Items);
            Assert.Empty(Complete("|", new QuerySchema<Product>().Build()).Items);
        }

        [Fact]
        public void BothAllowlistsAndCanonicalAncestorDenialsApply()
        {
            var schema = ExpressionSchema.FromType<Product>(
                validProperties: new[] { "Price", "Customer.Name", "buyer" },
                propertyMapping: new Dictionary<string, string> { ["buyer"] = "Customer.Name" },
                properties: new[] { new ExpressionPropertyDefinition("Customer", typeof(Customer), canQuery: false) });
            Assert.Equal(new[] { "Price" }, Complete("|", schema).Items.Select(item => item.InsertionText));
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<Product>(),
                new QueryPolicy(), new[] { "Price" });
            Assert.Equal(new[] { "Price" }, Complete("|", context.Schema, context).Items.Select(item => item.InsertionText));
        }

        [Theory]
        [InlineData("Customer.|", "Customer.Name")]
        [InlineData("Cu|stomer.Name = 'A'", "Customer.Name")]
        [InlineData("Customer.Na|ZZ = 'A'", "Customer.Name")]
        [InlineData("buyer.|", null)]
        [InlineData("Customer.Parent.|", "Customer.Parent.Name")]
        public void PathsAreRequestedLevelAndAliasesNeverRewritePrefixes(string marked, string expected)
        {
            var schema = ExpressionSchema.FromType<Product>(
                propertyMapping: new Dictionary<string, string> { ["buyer"] = "Customer.Name" });
            var items = Complete(marked, schema).Items;
            if (expected == null) Assert.Empty(items);
            else Assert.Equal(expected, Assert.Single(items).InsertionText);
        }

        [Fact]
        public void NavigationAndCollectionRestrictionsUseCanonicalPaths()
        {
            var schema = ExpressionSchema.FromType<Product>(
                propertyMapping: new Dictionary<string, string> { ["buyer"] = "Customer.Name" });
            var context = new ExpressionQueryContext(schema,
                new QueryPolicy(maxNavigationDepth: 0, allowCollectionAccess: false));
            Assert.Empty(Complete("Customer.|", schema, context).Items);
            Assert.Empty(Complete("Customers.|", schema, context).Items);
            Assert.DoesNotContain(Complete("|", schema, context).Items, item => item.InsertionText == "buyer");
        }

        [Fact]
        public void SurvivingOperandConstrainsFieldCompatibilityAndEditPreservesText()
        {
            var schema = ExpressionSchema.FromType<Product>();
            const string text = "NaZZ = 'A' and Price > 1";
            var item = Assert.Single(Complete("Na|ZZ = 'A' and Price > 1", schema).Items);
            Assert.Equal(0, item.Start);
            Assert.Equal(4, item.Length);
            Assert.Equal("Name = 'A' and Price > 1",
                text.Substring(0, item.Start) + item.InsertionText + text.Substring(item.Start + item.Length));
            Assert.DoesNotContain(Complete("Price > |", schema).Items,
                candidate => candidate.InsertionText == "Name");
        }

        [Fact]
        public void RecognizedEmptyAndUnknownReceiverAreDistinct()
        {
            var schema = ExpressionSchema.FromType<Product>();
            Assert.Equal(ExpressionCompletionStatus.NoMatches, Complete("Zzz|", schema).Status);
            var unknown = Complete("Unknown |", schema);
            Assert.Equal(ExpressionCompletionStatus.ContextUnavailable, unknown.Status);
            var diagnostic = Assert.Single(unknown.Diagnostics);
            Assert.Equal("completion-context-unavailable", diagnostic.Code);
            Assert.Equal(8, diagnostic.Start);
            Assert.Empty(Complete("'random| text'", schema).Items);
            Assert.Single(Complete("Price = ) and Na|", schema).Items);
        }

        internal static ExpressionCompletionResult Complete(
            string marked, ExpressionSchema schema, ExpressionQueryContext context = null)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var syntax = Interpreter.AnalyzeExpression(text);
            var slot = ExpressionCompletionContext.Recognize(text, position, schema, null, syntax, context);
            var candidates = new ExpressionCompletionCandidates(text, schema, null, syntax, slot, context);
            candidates.AddFields();
            return candidates.Shape();
        }

        public class Product
        {
            public decimal Price { get; set; }
            public string Name { get; set; }
            public string Secret => throw new InvalidOperationException("Getter must not run.");
            public Customer Customer { get; set; }
            public List<Customer> Customers { get; set; }
        }

        public class Customer
        {
            public string Name { get; set; }
            public Customer Parent { get; set; }
        }
    }
}
