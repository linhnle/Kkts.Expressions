using System;
using System.Linq;
using System.Text.Json;
using Kkts.Expressions;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreeJsonTest
    {
        [Fact]
        public void DecodesRequestedNestedJsonShapeAndPreservesConditionValues()
        {
            const string json = "{\"and\":[{\"or\":[{\"field\":\"Status\",\"op\":\"=\",\"value\":\"Active\"},{\"field\":\"Priority\",\"op\":\">=\",\"value\":3}]},{\"not\":{\"field\":\"Department\",\"op\":\"in\",\"value\":[\"Archived\",\"External\"]}}]}";

            var result = FilterTreeJson.TryDeserialize(json);

            Assert.True(result.Succeeded);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(FilterNodeKind.And, result.Result.Kind);
            Assert.Equal(FilterNodeKind.Or, result.Result.Children[0].Kind);
            Assert.Equal("Status", result.Result.Children[0].Children[0].Field);
            Assert.Equal(FilterValueKind.String, result.Result.Children[0].Children[0].Value.Kind);
            Assert.Equal(FilterValueKind.Number, result.Result.Children[0].Children[1].Value.Kind);
            Assert.Equal("3", result.Result.Children[0].Children[1].Value.Text);
            Assert.Equal(FilterNodeKind.Not, result.Result.Children[1].Kind);
            Assert.Equal(new[] { "Archived", "External" },
                result.Result.Children[1].Child.Value.Items.Select(item => item.Text));
        }

        [Theory]
        [InlineData("{\"and\":[]}", "filter-tree-empty-group", "/and")]
        [InlineData("{\"or\":[]}", "filter-tree-empty-group", "/or")]
        [InlineData("{\"and\":[],\"or\":[]}", "filter-tree-invalid-shape", "")]
        [InlineData("{\"Field\":\"Id\",\"op\":\"=\",\"value\":1}", "filter-tree-unknown-member", "/Field")]
        [InlineData("{\"field\":\"Id\",\"field\":\"Name\",\"op\":\"=\",\"value\":1}", "filter-tree-duplicate-member", "/field")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\"}", "filter-tree-invalid-shape", "/value")]
        [InlineData("{\"not\":[]}", "filter-tree-invalid-shape", "/not")]
        [InlineData("{\"not\":{}}", "filter-tree-invalid-shape", "/not")]
        [InlineData("{\"and\":[null]}", "filter-tree-null-child", "/and/0")]
        [InlineData("{\"and\":[1]}", "filter-tree-invalid-shape", "/and/0")]
        [InlineData("{\"field\":\"Id\",\"op\":\"matches\",\"value\":1}", "filter-tree-unknown-operator", "/op")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":[[1]]}", "filter-tree-invalid-value", "/value/0")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":[1]}", "filter-tree-invalid-value", "/value")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":{\"anything\":1}}", "filter-tree-invalid-value", "/value/anything")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":{\"variable\":\"$ids\"}}", "filter-tree-invalid-value", "/value/variable")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":{\"variable\":\"ids\",\"variable\":\"more\"}}", "filter-tree-duplicate-member", "/value/variable")]
        public void RejectsMalformedShapesWithStableCodesAndJsonPointers(
            string json,
            string expectedCode,
            string expectedPath)
        {
            var result = FilterTreeJson.TryDeserialize(json);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(expectedCode, diagnostic.Code);
            Assert.Equal(expectedPath, diagnostic.InputPath);
            Assert.Equal(0, diagnostic.Start);
            Assert.Equal(0, diagnostic.Length);
        }

        [Fact]
        public void DistinguishesMissingNullLiteralStringsAndVariableReferences()
        {
            var nullResult = FilterTreeJson.TryDeserialize("{\"field\":\"Id\",\"op\":\"=\",\"value\":null}");
            var textResult = FilterTreeJson.TryDeserialize("{\"field\":\"Name\",\"op\":\"=\",\"value\":\"$user.name\"}");
            var variableResult = FilterTreeJson.TryDeserialize("{\"field\":\"Name\",\"op\":\"=\",\"value\":{\"variable\":\"user.name\"}}");

            Assert.True(nullResult.Succeeded);
            Assert.Equal(FilterValueKind.Null, nullResult.Result.Value.Kind);
            Assert.True(textResult.Succeeded);
            Assert.Equal(FilterValueKind.String, textResult.Result.Value.Kind);
            Assert.Equal("$user.name", textResult.Result.Value.Text);
            Assert.True(variableResult.Succeeded);
            Assert.Equal(FilterValueKind.Variable, variableResult.Result.Value.Kind);
            Assert.Equal("user.name", variableResult.Result.Value.Text);
        }

        [Fact]
        public void PreservesExactNumbersCollectionOrderDuplicatesAndNulls()
        {
            const string json = "{\"field\":\"Id\",\"op\":\"in\",\"value\":[18446744073709551615,1.2345678901234567890123456789,null,1,{\"variable\":\"ids\"}]}";

            var result = FilterTreeJson.TryDeserialize(json);

            Assert.True(result.Succeeded);
            Assert.Equal(
                new[] { "18446744073709551615", "1.2345678901234567890123456789", null, "1", "ids" },
                result.Result.Value.Items.Select(item => item.Kind == FilterValueKind.Null ? null : item.Text));
            Assert.Equal(
                new[] { FilterValueKind.Number, FilterValueKind.Number, FilterValueKind.Null, FilterValueKind.Number, FilterValueKind.Variable },
                result.Result.Value.Items.Select(item => item.Kind));
        }

        [Fact]
        public void NormalizesOperatorCaseAndWhitespaceUsingExistingRules()
        {
            var result = FilterTreeJson.TryDeserialize(
                "{\"field\":\"Id\",\"op\":\" IN \",\"value\":[1]}");

            Assert.True(result.Succeeded);
            Assert.Equal("in", result.Result.Operator);
            Assert.Equal(
                "{\"field\":\"Id\",\"op\":\"in\",\"value\":[1]}",
                FilterTreeJson.Serialize(result.Result));
        }

        [Fact]
        public void RoundTripsEveryValueKindAndPreservesEscapingAndRepeatedNodes()
        {
            var repeated = FilterNode.Condition("Name", "=", FilterValue.String("quote\" slash\\ line\n"));
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("NullValue", "=", FilterValue.Null),
                FilterNode.Condition("Enabled", "=", FilterValue.Boolean(true)),
                FilterNode.Condition("Amount", ">=", FilterValue.Number("18446744073709551615")),
                repeated,
                FilterNode.Condition("UserId", "=", FilterValue.Variable("user.id")),
                FilterNode.Condition("Ids", "in", FilterValue.Collection(new[]
                {
                    FilterValue.Null,
                    FilterValue.Number("-0.0000000000000000001"),
                    FilterValue.String("dup"),
                    FilterValue.String("dup"),
                    FilterValue.Variable("user.id")
                })),
                repeated
            });

            var json = FilterTreeJson.Serialize(tree);
            var result = FilterTreeJson.TryDeserialize(json);

            Assert.True(result.Succeeded);
            Assert.Equal(7, result.Result.Children.Count);
            Assert.Same(repeated, tree.Children[3]);
            Assert.Same(repeated, tree.Children[6]);
            Assert.Equal(tree.Children[3].Value.Kind, tree.Children[6].Value.Kind);
            Assert.Equal(FilterValueKind.Null, result.Result.Children[0].Value.Kind);
            Assert.Equal(FilterValueKind.Boolean, result.Result.Children[1].Value.Kind);
            Assert.Equal("18446744073709551615", result.Result.Children[2].Value.Text);
            Assert.Equal("quote\" slash\\ line\n", result.Result.Children[3].Value.Text);
            Assert.Equal(FilterValueKind.Variable, result.Result.Children[4].Value.Kind);
            Assert.Equal(
                new[] { FilterValueKind.Null, FilterValueKind.Number, FilterValueKind.String, FilterValueKind.String, FilterValueKind.Variable },
                result.Result.Children[5].Value.Items.Select(item => item.Kind));
            Assert.Equal("dup", result.Result.Children[5].Value.Items[2].Text);
            Assert.Equal("dup", result.Result.Children[5].Value.Items[3].Text);
            Assert.Equal(json, FilterTreeJson.Serialize(result.Result));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":1} trailing")]
        [InlineData("{\"field\":\"Id\",\"op\":\"=\",\"value\":1} {\"field\":\"Name\",\"op\":\"=\",\"value\":\"A\"}")]
        public void InvalidJsonReturnsNoPartialTree(string json)
        {
            var result = FilterTreeJson.TryDeserialize(json);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("filter-tree-invalid-json", Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void JsonPointersEscapeMemberNamesAndProjectTreePathsUnderConditionOptions()
        {
            var escaped = FilterTreeJson.TryDeserialize("{\"and/or~member\":[]}");
            var nested = FilterTreeJson.TryDeserialize(
                "{\"and\":[{\"field\":\"Id\",\"op\":\"in\",\"value\":[1,[2]]}]}");
            var root = FilterTreeJson.TryDeserialize("{");

            Assert.Equal("/and~1or~0member", Assert.Single(escaped.Diagnostics).InputPath);
            Assert.Equal("/and/0/value/1", Assert.Single(nested.Diagnostics).InputPath);
            Assert.Equal(string.Empty, Assert.Single(root.Diagnostics).InputPath);
            Assert.Equal(
                "/FilterTree/and/0/value/1",
                FilterTreeDiagnosticProjection.Project(nested.Diagnostics[0], "/FilterTree").InputPath);
            Assert.Equal(
                "/FilterTree",
                FilterTreeDiagnosticProjection.Project(root.Diagnostics[0], "/FilterTree").InputPath);
        }

        [Fact]
        public void RestrictedFieldProjectionSuppressesNamesTypesAndSuggestions()
        {
            var diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "property-not-queryable",
                "Canonical.Secret has CLR type SecretType.",
                0,
                0,
                expectedTypes: new[] { ExpressionTypeInfo.ForClr(typeof(string)) },
                actualTypes: new[] { ExpressionTypeInfo.ForClr(typeof(int)) },
                suggestions: new[] { new ExpressionCorrectionSuggestion("Try this.", "Canonical.Secret", 0, 1) },
                inputPath: "/field");

            var projected = FilterTreeDiagnosticProjection.Project(diagnostic, "/FilterTree");

            Assert.Equal("/FilterTree/field", projected.InputPath);
            Assert.Equal("The field is not permitted for querying.", projected.Message);
            Assert.Empty(projected.ExpectedTypes);
            Assert.Empty(projected.ActualTypes);
            Assert.Empty(projected.Suggestions);
            Assert.DoesNotContain("Canonical.Secret", projected.Message);
            Assert.DoesNotContain("SecretType", projected.Message);
        }

        [Fact]
        public void SystemTextJsonUsesTheDedicatedConverterWithoutChangingLegacyDtos()
        {
            var tree = FilterNode.Condition("Name", "=", FilterValue.String("A\"B"));

            var json = JsonSerializer.Serialize(tree);
            var roundTrip = JsonSerializer.Deserialize<FilterNode>(json);

            Assert.Equal("{\"field\":\"Name\",\"op\":\"=\",\"value\":\"A\\u0022B\"}", json);
            Assert.Equal(FilterNodeKind.Condition, roundTrip.Kind);
            Assert.Equal("A\"B", roundTrip.Value.Text);
            Assert.Contains(
                "\"Property\"",
                JsonSerializer.Serialize(new Filter { Property = "Name", Operator = "=", Value = "A" }));

            var deep = tree;
            for (var i = 0; i < 80; i++) deep = FilterNode.Not(deep);
            var options = new JsonSerializerOptions { MaxDepth = 256 };
            var deepJson = JsonSerializer.Serialize(deep, options);
            Assert.NotNull(JsonSerializer.Deserialize<FilterNode>(deepJson, options));
        }

        [Fact]
        public void ReadmeNestedTreeExampleExecutesAndRoundTrips()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Or(new[]
                {
                    FilterNode.Condition("Status", "=", FilterValue.String("Active")),
                    FilterNode.Condition("Priority", ">=", FilterValue.Number("3"))
                }),
                FilterNode.Not(FilterNode.Condition(
                    "Department",
                    "in",
                    FilterValue.Collection(new[]
                    {
                        FilterValue.String("Archived"),
                        FilterValue.String("External")
                    })))
            });

            var json = FilterTreeJson.Serialize(tree);
            var decoded = FilterTreeJson.TryDeserialize(json);

            Assert.True(decoded.Succeeded);
            Assert.Equal(FilterNodeKind.And, decoded.Result.Kind);
            Assert.Equal(FilterNodeKind.Or, decoded.Result.Children[0].Kind);
            Assert.Equal(FilterNodeKind.Not, decoded.Result.Children[1].Kind);
        }

        [Fact]
        public void FacadeProcessesTreesBeyondDefaultSerializerDepthIteratively()
        {
            var tree = FilterNode.Condition("Id", "=", FilterValue.Number("1"));
            for (var i = 0; i < 128; i++) tree = FilterNode.Not(tree);

            var json = FilterTreeJson.Serialize(tree);
            var result = FilterTreeJson.TryDeserialize(json);
            var actualDepth = 0;
            var current = result.Result;
            while (current.Kind == FilterNodeKind.Not)
            {
                actualDepth++;
                current = current.Child;
            }

            Assert.True(result.Succeeded);
            Assert.Equal(128, actualDepth);
            Assert.Equal(FilterNodeKind.Condition, current.Kind);
        }
    }
}
