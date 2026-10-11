using System;
using System.Collections.Generic;
using System.Globalization;
using Kkts.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class SemanticAnalysisDocumentationExamplesTest
    {
        [Fact]
        public void GuideMetadataAndAnalysisExamplesCompileAndRun()
        {
            var schema = ProductSchemaExample.Create();
            var variables = VariableSchemaExample.Create();
            var analysis = Interpreter.AnalyzeExpression<Product>(
                "Price > $minimum and $user.Department = 'Sales' and buyer = 'North'",
                schema,
                variables);

            Assert.True(analysis.IsSemanticallyValid, analysis.Diagnostics.Count.ToString());

            var denied = Interpreter.AnalyzeExpression<Product>(
                "InternalCost = 1",
                schema,
                variables);
            Assert.Equal(
                "property-not-queryable",
                Assert.Single(denied.SemanticDiagnostics).Code);
        }

        [Fact]
        public void GuideConversionContextExampleCompilesAndRuns()
        {
            var context = new ExpressionConversionContext(
                CultureInfo.InvariantCulture,
                new[] { "yyyy-M-d", "yyyy/M/d", "d/M/yyyy" });
            var schema = ExpressionSchema.FromType<Product>(
                conversionContext: context);

            var analysis = Interpreter.AnalyzeExpression<Product>(
                "Created = '2020-2-29'",
                schema);

            Assert.True(analysis.IsSemanticallyValid, analysis.Diagnostics.Count.ToString());
        }

        public sealed class Product
        {
            public decimal Price { get; set; }
            public decimal? Discount { get; set; }
            public string Name { get; set; } = "";
            public decimal InternalCost { get; set; }
            public Customer Customer { get; set; } = new Customer();
            public DateTime Created { get; set; }
        }

        public sealed class Customer
        {
            public string Name { get; set; } = "";
        }

        public sealed class UserMetadata
        {
            public string Department { get; set; } = "";
        }

        public static class ProductSchemaExample
        {
            public static ExpressionSchema Create()
            {
                return ExpressionSchema.FromType<Product>(
                    validProperties: new[] { "Price", "Discount", "Name", "InternalCost", "buyer" },
                    propertyMapping: new Dictionary<string, string>
                    {
                        ["buyer"] = "Customer.Name"
                    },
                    properties: new[]
                    {
                        new ExpressionPropertyDefinition(
                            "InternalCost", typeof(decimal), canQuery: false),
                        new ExpressionPropertyDefinition(
                            "Name", typeof(string),
                            nullability: ExpressionNullability.NonNullable)
                    });
            }
        }

        public static class VariableSchemaExample
        {
            public static ExpressionVariableSchema Create()
            {
                return new ExpressionVariableSchema(new[]
                {
                    ExpressionVariableDefinition.FromType("user", typeof(UserMetadata)),
                    new ExpressionVariableDefinition("minimum", typeof(decimal)),
                    new ExpressionVariableDefinition("prices", typeof(decimal[])),
                    new ExpressionVariableDefinition(
                        "explicitUser",
                        typeof(object),
                        members: new[]
                        {
                            new ExpressionVariableDefinition("department", typeof(string))
                        })
                });
            }
        }
    }
}
