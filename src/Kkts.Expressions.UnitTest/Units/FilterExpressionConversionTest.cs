using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterExpressionConversionTest
    {
        [Fact]
        public void ParseAndFormatPreserveNestedLogicAndDeterministicOrder()
        {
            var schema = ExpressionSchema.FromType<TestEntity>();
            const string expression =
                "(String = 'Active' or Integer >= 3) and not (IntegerNullable in [1, null, 1])";

            var parsed = FilterExpression.Parse(expression, schema);

            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + "@" + diagnostic.Start + ": " + diagnostic.Message)));
            Assert.Equal(FilterNodeKind.And, parsed.Result.Kind);
            Assert.Equal(FilterNodeKind.Or, parsed.Result.Children[0].Kind);
            Assert.Equal(FilterNodeKind.Not, parsed.Result.Children[1].Kind);
            var formatted = FilterExpression.Format(parsed.Result, schema);
            Assert.True(formatted.Succeeded);
            var reparsed = FilterExpression.Parse(formatted.Result, schema);
            Assert.True(reparsed.Succeeded);
            Assert.Equal(formatted.Result, FilterExpression.Format(reparsed.Result, schema).Result);
        }

        [Fact]
        public void ParseConvertsTheRepresentativeNestedNotAndGroupExpression()
        {
            var parsed = FilterExpression.Parse(
                "(Integer = 1 or Integer = 2) and not (Integer = 3 or (Integer = 4 and Integer = 5))",
                ExpressionSchema.FromType<TestEntity>());

            Assert.True(parsed.Succeeded);
            Assert.Equal(FilterNodeKind.And, parsed.Result.Kind);
            Assert.Equal(FilterNodeKind.Or, parsed.Result.Children[0].Kind);
            Assert.Equal(FilterNodeKind.Not, parsed.Result.Children[1].Kind);
            Assert.Equal(FilterNodeKind.Or, parsed.Result.Children[1].Child.Kind);
            Assert.Equal(FilterNodeKind.And, parsed.Result.Children[1].Child.Children[1].Kind);
        }

        [Fact]
        public void ParsePreservesExplicitReferencesAndTypedScalars()
        {
            var schema = ExpressionSchema.FromType<TestEntity>();
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("user.id", typeof(int))
            });

            var parsed = FilterExpression.Parse(
                "Integer = $user.id and String = '$user.id' and Boolean = true",
                schema,
                variables);

            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + "@" + diagnostic.Start + ": " + diagnostic.Message)));
            Assert.Equal(FilterValueKind.Variable, parsed.Result.Children[0].Value.Kind);
            Assert.Equal(FilterValueKind.String, parsed.Result.Children[1].Value.Kind);
            Assert.Equal(FilterValueKind.Boolean, parsed.Result.Children[2].Value.Kind);
        }

        [Theory]
        [InlineData("Integer + 1 > 3")]
        [InlineData("Integer = Id")]
        [InlineData("1 < Integer")]
        [InlineData("Integer in Integer")]
        [InlineData("$enabled")]
        [InlineData("true")]
        public void UnsupportedExpressionShapesHaveExplicitSourceDiagnostics(string expression)
        {
            var result = FilterExpression.Parse(expression, ExpressionSchema.FromType<TestEntity>());

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("filter-conversion-unsupported", Assert.Single(result.Diagnostics).Code);
            Assert.True(result.Diagnostics[0].Length > 0);
        }

        [Fact]
        public void UnsupportedConversionDoesNotChangeExistingExpressionParsing()
        {
            var parsed = Interpreter.ParsePredicate<TestEntity>("Integer + 1 > 3");

            Assert.True(parsed.Succeeded);
        }

        [Fact]
        public void FunctionAndBooleanFieldSubsetIsRepresentable()
        {
            var parsed = FilterExpression.Parse(
                "String.contains('x') and Boolean",
                ExpressionSchema.FromType<TestEntity>());

            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + "@" + diagnostic.Start + ": " + diagnostic.Message)));
            Assert.Equal("contains", parsed.Result.Children[0].Operator);
            Assert.Equal(FilterValueKind.Boolean, parsed.Result.Children[1].Value.Kind);
            Assert.True(parsed.Result.Children[1].Value.BooleanValue);
        }

        [Fact]
        public void FormattingRejectsPrefixLookingMembershipLiterals()
        {
            var tree = FilterNode.Condition(
                "String",
                "in",
                FilterValue.Collection(new[] { FilterValue.String("$name") }));

            var result = FilterExpression.Format(tree, ExpressionSchema.FromType<TestEntity>());

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("filter-conversion-unsupported", diagnostic.Code);
            Assert.Equal("/value/0", diagnostic.InputPath);

            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("name", "not the literal"));
            var direct = tree.TryBuildPredicate<TestEntity>(resolver);
            Assert.True(direct.Succeeded);
            Assert.True(direct.Result.Compile()(new TestEntity { String = "$name" }));
        }

        [Fact]
        public void FormattingRetainsNotGroupsAndDuplicateLeafOrder()
        {
            var tree = FilterNode.Not(FilterNode.Or(new[]
            {
                FilterNode.Condition("Integer", "=", FilterValue.Number("1")),
                FilterNode.Condition("Integer", "=", FilterValue.Number("1"))
            }));

            var formatted = FilterExpression.Format(tree, ExpressionSchema.FromType<TestEntity>());

            Assert.True(formatted.Succeeded);
            Assert.Equal("not (Integer = 1 or Integer = 1)", formatted.Result);
        }

        [Fact]
        public void ConversionPreservesEscapedStringsAndNormalizesExistingAliases()
        {
            var schema = ExpressionSchema.FromType<TestEntity>();
            var parsed = FilterExpression.Parse(
                "String.contain('O\\'Brien\\\\x') and Integer == 3",
                schema);

            Assert.True(parsed.Succeeded);
            Assert.Equal("contains", parsed.Result.Children[0].Operator);
            Assert.Equal("O'Brien\\\\x", parsed.Result.Children[0].Value.Text);
            var formatted = FilterExpression.Format(parsed.Result, schema);
            Assert.True(formatted.Succeeded);
            var reparsed = FilterExpression.Parse(formatted.Result, schema);
            Assert.True(reparsed.Succeeded);
            Assert.Equal(parsed.Result.Children[0].Value.Text, reparsed.Result.Children[0].Value.Text);
            Assert.Contains("Integer = 3", formatted.Result);
        }

        [Fact]
        public void NumericFormattingPreservesUnsignedExtremesAndRejectsLostDecimalPrecision()
        {
            var schema = ExpressionSchema.FromType<NumericLiteralEntity>();
            var maximum = FilterNode.Condition(
                "Unsigned",
                "=",
                FilterValue.Number("18446744073709551615"));
            var maximumText = FilterExpression.Format(maximum, schema);
            Assert.True(maximumText.Succeeded);
            Assert.Equal(
                "18446744073709551615",
                FilterExpression.Parse(maximumText.Result, schema).Result.Value.Text);

            var precise = FilterNode.Condition(
                "Precise",
                "=",
                FilterValue.Number("0.1234567890123456789012345678"));
            var rejected = FilterExpression.Format(precise, schema);
            Assert.False(rejected.Succeeded);
            Assert.Null(rejected.Result);
            Assert.Equal("filter-conversion-unsupported", Assert.Single(rejected.Diagnostics).Code);
        }

        [Fact]
        public void MetadataConversionDoesNotInvokeEntityGettersOnAnyPath()
        {
            GetterProbeEntity.GetterCalls = 0;
            var schema = ExpressionSchema.FromType<GetterProbeEntity>(
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Denied", typeof(int), canQuery: false)
                });

            var valid = FilterExpression.Parse("Value = $id", schema, new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("id", typeof(int))
            }));
            var unsupported = FilterExpression.Parse("Value + 1 > 3", schema);
            var denied = FilterExpression.Parse("Denied = 1", schema);
            if (valid.Succeeded) FilterExpression.Format(valid.Result, schema, new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("id", typeof(int))
            }));

            Assert.True(valid.Succeeded);
            Assert.False(unsupported.Succeeded);
            Assert.False(denied.Succeeded);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal(0, GetterProbeEntity.GetterCalls);
        }

        [Fact]
        public void ContextConversionEnforcesTreeAndTextPolicyIndependently()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy(maxExpressionLength: 30).WithMaxFilterTreeDepth(1));

            var parsed = context.ParseFilterTree("Integer = 1 and Boolean");
            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + "@" + diagnostic.Start + ": " + diagnostic.Message)));
            var formatted = context.FormatFilterTree(parsed.Result);
            Assert.True(formatted.Succeeded);

            var tooDeep = context.ParseFilterTree("not (not (Integer = 1))");
            Assert.False(tooDeep.Succeeded);
            Assert.Equal("query-policy-filter-tree-depth-exceeded",
                Assert.Single(tooDeep.Diagnostics).Code);
        }

        [Fact]
        public void ContextFormattingCannotEvadeSuppliedTreeOrOutputParenthesisLimits()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy(maxParenthesisDepth: 0).WithMaxFilterTreeDepth(2));
            var tree = FilterNode.And(new[]
            {
                FilterNode.Or(new[]
                {
                    FilterNode.Condition("Integer", "=", FilterValue.Number("1")),
                    FilterNode.Condition("Integer", "=", FilterValue.Number("2"))
                }),
                FilterNode.Condition("Boolean", "=", FilterValue.Boolean(true))
            });

            var output = context.FormatFilterTree(tree);

            Assert.False(output.Succeeded);
            Assert.Null(output.Result);
            Assert.Equal(
                "query-policy-parenthesis-depth-exceeded",
                Assert.Single(output.Diagnostics).Code);
        }

        [Fact]
        public void ContextParsingUsesTextParenthesisPolicySeparateFromTreeDepth()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy(maxParenthesisDepth: 0).WithMaxFilterTreeDepth(3));

            var parsed = context.ParseFilterTree("(Integer = 1)");

            Assert.False(parsed.Succeeded);
            Assert.Equal(
                "query-policy-parenthesis-depth-exceeded",
                Assert.Single(parsed.Diagnostics).Code);
        }

        [Fact]
        public void ConversionDoesNotResolveVariables()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("id", typeof(int))
            });
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<TestEntity>(), new QueryPolicy());

            var parsed = context.ParseFilterTree("Integer = $id", variables);
            var formatted = context.FormatFilterTree(parsed.Result, variables);

            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + "@" + diagnostic.Start + ": " + diagnostic.Message)));
            Assert.True(formatted.Succeeded);
            Assert.Contains("$id", formatted.Result);
        }

        [Fact]
        public void ConvertedPredicatesMatchExpressionAndHandwrittenPredicates()
        {
            var schema = ExpressionSchema.FromType<TestEntity>();
            const string expression =
                "(Integer >= 3 or String.contains('x')) and not (IntegerNullable in [1, null])";
            var converted = FilterExpression.Parse(expression, schema);
            Assert.True(converted.Succeeded);
            var built = converted.Result.BuildPredicate<TestEntity>();
            var parsed = Interpreter.ParsePredicate<TestEntity>(expression);
            Assert.True(parsed.Succeeded);
            var expected = new[]
            {
                new TestEntity { Integer = 4, IntegerNullable = 2, String = "" },
                new TestEntity { Integer = 0, IntegerNullable = 2, String = "x marks" },
                new TestEntity { Integer = 4, IntegerNullable = 1, String = "" },
                new TestEntity { Integer = 0, IntegerNullable = null, String = "x" }
            };

            var convertedIds = expected.Where(built.Compile()).Select(entity => Array.IndexOf(expected, entity)).ToArray();
            var parsedIds = expected.Where(parsed.Result.Compile()).Select(entity => Array.IndexOf(expected, entity)).ToArray();
            var handwrittenIds = expected.Where(entity =>
                    (entity.Integer >= 3 || entity.String.Contains("x")) &&
                    !(entity.IntegerNullable == 1 || entity.IntegerNullable == null))
                .Select(entity => Array.IndexOf(expected, entity))
                .ToArray();

            Assert.Equal(handwrittenIds, convertedIds);
            Assert.Equal(handwrittenIds, parsedIds);
            var formatted = FilterExpression.Format(converted.Result, schema);
            Assert.True(formatted.Succeeded);
            Assert.True(FilterExpression.Parse(formatted.Result, schema).Succeeded);
        }

        [Fact]
        public void TypedLiteralRoundTripsCoverEnumGuidAndExplicitTemporalValues()
        {
            var schema = ExpressionSchema.FromType<TypedLiteralEntity>();
            var expression =
                "Option = 'Option2' and Guid = '00112233-4455-6677-8899-aabbccddeeff' " +
                "and DateTime = '2024-01-03T12:30:00.0000000' " +
                "and DateTimeOffset = '2024-01-03T12:30:00.0000000+02:00' " +
                "and Duration = '-1.02:03:04.005' and Character = 'x'";

            var parsed = FilterExpression.Parse(expression, schema);

            Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(diagnostic =>
                diagnostic.Code + ": " + diagnostic.Message)));
            var formatted = FilterExpression.Format(parsed.Result, schema);
            Assert.True(formatted.Succeeded, string.Join("; ", formatted.Diagnostics.Select(diagnostic =>
                diagnostic.Code + ": " + diagnostic.Message)));
            var reparsed = FilterExpression.Parse(formatted.Result, schema);
            Assert.True(reparsed.Succeeded);
            Assert.Equal(
                parsed.Result.Children.Select(node => node.Value.Kind),
                reparsed.Result.Children.Select(node => node.Value.Kind));
            Assert.Equal(
                parsed.Result.Children.Select(node => node.Value.Text),
                reparsed.Result.Children.Select(node => node.Value.Text));
            var expected = new TypedLiteralEntity
            {
                Option = TypedOption.Option2,
                Guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                DateTime = new DateTime(2024, 1, 3, 12, 30, 0),
                DateTimeOffset = new DateTimeOffset(2024, 1, 3, 12, 30, 0, TimeSpan.FromHours(2)),
                Duration = TimeSpan.Parse("-1.02:03:04.005", CultureInfo.InvariantCulture),
                Character = 'x'
            };
            var treePredicate = parsed.Result.BuildPredicate<TypedLiteralEntity>().Compile();
            var textPredicate = Interpreter.ParsePredicate<TypedLiteralEntity>(formatted.Result);
            Assert.True(textPredicate.Succeeded);
            Assert.Equal(treePredicate(expected), textPredicate.Result.Compile()(expected));
            Assert.True(treePredicate(expected));
        }

        public enum TypedOption
        {
            Option1,
            Option2
        }

        public sealed class TypedLiteralEntity
        {
            public TypedOption Option { get; set; }
            public Guid Guid { get; set; }
            public DateTime DateTime { get; set; }
            public DateTimeOffset DateTimeOffset { get; set; }
            public TimeSpan Duration { get; set; }
            public char Character { get; set; }
        }

        public sealed class NumericLiteralEntity
        {
            public ulong Unsigned { get; set; }
            public decimal Precise { get; set; }
        }

        public sealed class GetterProbeEntity
        {
            private int _value;
            public static int GetterCalls;

            public int Value
            {
                get
                {
                    GetterCalls++;
                    return _value;
                }
                set => _value = value;
            }

            public int Denied { get; set; }
        }
    }
}
