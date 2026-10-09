using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionSemanticAnalysisTest
    {
        [Fact]
        public void AnalyzeExpression_ReportsIndependentUnknownAndIncompatibleOperands()
        {
            var result = Analyze("Price > 'abc' and UnknownField = 1");

            Assert.True(result.IsComplete);
            Assert.False(result.IsSemanticallyValid);
            Assert.Equal(new[]
            {
                ("incompatible-operand", 8, 5),
                ("unknown-property", 18, 12)
            }, result.SemanticDiagnostics.Select(diagnostic =>
                (diagnostic.Code, diagnostic.Start, diagnostic.Length)));

            var incompatible = result.SemanticDiagnostics[0];
            Assert.Equal(typeof(decimal), Assert.Single(incompatible.ExpectedTypes).ClrType);
            Assert.Equal(typeof(string), Assert.Single(incompatible.ActualTypes).ClrType);
            Assert.Empty(incompatible.Suggestions);
        }

        [Fact]
        public void AnalyzeExpression_DistinguishesRestrictedPropertiesAndDoesNotSuggestThem()
        {
            var result = Analyze("Secret = 1");
            var diagnostic = Assert.Single(result.SemanticDiagnostics);

            Assert.Equal("property-not-queryable", diagnostic.Code);
            Assert.Equal(0, diagnostic.Start);
            Assert.Equal(6, diagnostic.Length);
            Assert.Empty(diagnostic.ExpectedTypes);
            Assert.Empty(diagnostic.ActualTypes);
            Assert.Empty(diagnostic.Suggestions);
            Assert.DoesNotContain("decimal", diagnostic.Message, StringComparison.OrdinalIgnoreCase);

            var shadowingVariable = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("Secret", typeof(decimal))
            });
            var cannotFallback = Interpreter.AnalyzeExpression<Product>(
                "Secret = 1",
                Schema,
                shadowingVariable);
            Assert.Equal("property-not-queryable", Assert.Single(cannotFallback.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_DoesNotRevealRestrictedCanonicalNamesThroughAliasesOrAncestors()
        {
            var aliasSchema = ExpressionSchema.FromType<Product>(
                validProperties: new[] { "cost" },
                propertyMapping: new Dictionary<string, string> { ["cost"] = "Secret" },
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Secret", typeof(decimal), canQuery: false)
                });
            var deniedAlias = Interpreter.AnalyzeExpression<Product>("cost = 1", aliasSchema);
            var aliasDiagnostic = Assert.Single(deniedAlias.SemanticDiagnostics);
            Assert.Equal("property-not-queryable", aliasDiagnostic.Code);
            Assert.DoesNotContain("Secret", aliasDiagnostic.Message);
            Assert.Empty(aliasDiagnostic.ExpectedTypes);
            Assert.Empty(aliasDiagnostic.ActualTypes);
            Assert.Empty(aliasDiagnostic.Suggestions);

            var typo = Interpreter.AnalyzeExpression<Product>("costs = 1", aliasSchema);
            Assert.Empty(Assert.Single(typo.SemanticDiagnostics).Suggestions);

            var ancestorSchema = ExpressionSchema.FromType<Product>(
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Child", typeof(ProductChild), canQuery: false)
                });
            var deniedDescendant = Interpreter.AnalyzeExpression<Product>(
                "Child.Name = 'North'",
                ancestorSchema);
            var ancestorDiagnostic = Assert.Single(deniedDescendant.SemanticDiagnostics);
            Assert.Equal("property-not-queryable", ancestorDiagnostic.Code);
            Assert.Empty(ancestorDiagnostic.ExpectedTypes);
            Assert.Empty(ancestorDiagnostic.ActualTypes);
            Assert.Empty(ancestorDiagnostic.Suggestions);
        }

        [Fact]
        public void AnalyzeExpression_ResolvesNestedPropertiesAndAliases()
        {
            var valid = Analyze("Child.Name = 'North'");
            Assert.True(valid.IsSemanticallyValid, Messages(valid));
            Assert.True(Analyze("cHiLd.nAmE = 'North'").IsSemanticallyValid);

            var aliasSchema = ExpressionSchema.FromType<Product>(
                propertyMapping: new Dictionary<string, string> { ["itemName"] = "Child.Name" },
                validProperties: new[] { "itemName" });
            var alias = Interpreter.AnalyzeExpression<Product>(
                "itemName = 'North'",
                aliasSchema);
            Assert.True(alias.IsSemanticallyValid, Messages(alias));
            var mixedCaseAlias = Interpreter.AnalyzeExpression<Product>(
                "ITEMNAME = 'North'",
                aliasSchema);
            Assert.True(mixedCaseAlias.IsSemanticallyValid, Messages(mixedCaseAlias));

            var invalid = Analyze("Child.Unknown = 1");
            Assert.Equal("unknown-property", Assert.Single(invalid.SemanticDiagnostics).Code);

            var invalidBothSides = Analyze("UnknownLeft = UnknownRight");
            Assert.Equal(2, invalidBothSides.SemanticDiagnostics.Count);
            Assert.All(invalidBothSides.SemanticDiagnostics, diagnostic =>
                Assert.Equal("unknown-property", diagnostic.Code));
        }

        [Fact]
        public void AnalyzeExpression_ValidatesVariablesWithoutRuntimeValues()
        {
            var missingRoot = Analyze("$user.department = 'Sales'");
            Assert.Equal("undeclared-variable", Assert.Single(missingRoot.SemanticDiagnostics).Code);

            var declared = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(UserMetadata))
            });
            var valid = Interpreter.AnalyzeExpression<Product>(
                "$user.department = 'Sales'",
                Schema,
                declared);
            Assert.True(valid.IsSemanticallyValid, Messages(valid));

            var missingMember = Interpreter.AnalyzeExpression<Product>(
                "$user.missing = 'Sales'",
                Schema,
                declared);
            Assert.Equal("unknown-variable-member", Assert.Single(missingMember.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_ResolvesBareFallbackAndExactDottedVariableDeclarations()
        {
            var bareVariables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("minimum", typeof(decimal))
            });
            var bare = Interpreter.AnalyzeExpression<Product>(
                "Price > minimum",
                Schema,
                bareVariables);
            Assert.True(bare.IsSemanticallyValid, Messages(bare));

            var dottedVariables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("user.department", typeof(string))
            });
            var dotted = Interpreter.AnalyzeExpression<Product>(
                "$user.department = 'Sales'",
                Schema,
                dottedVariables);
            Assert.True(dotted.IsSemanticallyValid, Messages(dotted));

            var reflectedField = Interpreter.AnalyzeExpression<Product>(
                "$user.Rank = 1",
                Schema,
                new ExpressionVariableSchema(new[]
                {
                    ExpressionVariableDefinition.FromType("user", typeof(UserMetadata))
                }));
            Assert.True(reflectedField.IsSemanticallyValid, Messages(reflectedField));

            var builtin = Interpreter.AnalyzeExpression<Product>(
                "$now = '2020-01-01'",
                Schema);
            Assert.Equal("undeclared-variable", Assert.Single(builtin.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_UsesFullVariableSpanAndAcceptsDeclaredUnavailableValues()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(UserMetadata))
            });
            var expression = "$user.department = 'Sales'";
            var valid = Interpreter.AnalyzeExpression<Product>(expression, Schema, variables);
            Assert.True(valid.IsSemanticallyValid, Messages(valid));

            var undeclared = Analyze("$user.department = 'Sales'");
            var diagnostic = Assert.Single(undeclared.SemanticDiagnostics);
            Assert.Equal("undeclared-variable", diagnostic.Code);
            Assert.Equal(0, diagnostic.Start);
            Assert.Equal(16, diagnostic.Length);
        }

        [Fact]
        public void AnalyzeExpression_DoesNotInvokeRuntimeVariableResolver()
        {
            var resolver = new CountingResolver();
            var runtime = Interpreter.ParsePredicate<Product>("Price = $minimum", resolver);
            Assert.True(runtime.Succeeded);
            Assert.Equal(1, resolver.ResolutionCount);

            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("minimum", typeof(decimal))
            });
            var analysis = Interpreter.AnalyzeExpression<Product>(
                "Price = $minimum",
                Schema,
                variables);

            Assert.True(analysis.IsSemanticallyValid, Messages(analysis));
            Assert.Equal(1, resolver.ResolutionCount);
        }

        [Fact]
        public void AnalyzeExpression_BindsEntityAndVariableMetadataWithoutCallingGetters()
        {
            var schema = ExpressionSchema.FromType<GetterProduct>();
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(ThrowingUser))
            });

            var result = Interpreter.AnalyzeExpression<GetterProduct>(
                "Name = $user.department",
                schema,
                variables);

            Assert.True(result.IsSemanticallyValid, Messages(result));
            Assert.Equal(0, GetterProduct.GetterCalls);
            Assert.Equal(0, ThrowingUser.GetterCalls);
        }

        [Fact]
        public void AnalyzeExpression_InspectsButDoesNotInvokeCustomOperators()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("left", typeof(CustomOperatorValue)),
                new ExpressionVariableDefinition("right", typeof(CustomOperatorValue))
            });

            var result = Interpreter.AnalyzeExpression<Product>(
                "$left = $right",
                Schema,
                variables);

            Assert.True(result.IsSemanticallyValid, Messages(result));
            Assert.Equal(0, CustomOperatorValue.OperatorCalls);
        }

        [Fact]
        public void AnalyzeExpression_RejectsInapplicableStringFunctionOnNumericMember()
        {
            var result = Analyze("Price.contains('text')");
            var diagnostic = Assert.Single(result.SemanticDiagnostics);

            Assert.Equal("operator-not-applicable", diagnostic.Code);
            Assert.Equal(6, diagnostic.Start);
            Assert.Equal(8, diagnostic.Length);
            Assert.Equal(typeof(string), Assert.Single(diagnostic.ExpectedTypes).ClrType);
            Assert.Equal(typeof(decimal), Assert.Single(diagnostic.ActualTypes).ClrType);
        }

        [Theory]
        [InlineData("Active > true")]
        [InlineData("Name > 'North'")]
        public void AnalyzeExpression_RejectsComparisonsThatPredicateConstructionCannotBuild(string expression)
        {
            var result = Analyze(expression);
            var diagnostic = Assert.Single(result.SemanticDiagnostics);

            Assert.Equal("operator-not-applicable", diagnostic.Code);
            Assert.False(Interpreter.ParsePredicate<Product>(expression).Succeeded);
        }

        [Theory]
        [InlineData("Price = '1.25'")]
        [InlineData("Discount = null")]
        [InlineData("Discount = ''")]
        [InlineData("Name.contains('North')")]
        [InlineData("Price in [1, 2.5]")]
        [InlineData("Price in {1, 2}")]
        [InlineData("Price in (1, 2)")]
        [InlineData("Price in [-1, 2]")]
        [InlineData("Price not in []")]
        [InlineData("Discount in [null, '1.25']")]
        [InlineData("Active in ['true', false]")]
        [InlineData("Name in ['O\\'Reilly']")]
        [InlineData("Price + 1 > 2")]
        [InlineData("Price + 1 > 2 and Active")]
        [InlineData("Price > Quantity")]
        [InlineData("Price = Quantity")]
        [InlineData("Huge > 1")]
        [InlineData("Huge + 1 > 2")]
        [InlineData("true and false")]
        [InlineData("not (Price = 1 or Discount = null)")]
        [InlineData("!!Active")]
        [InlineData("Price + 1 + 2 = 4")]
        [InlineData("Created = '2020-02-29'")]
        [InlineData("CreatedOffset = '2020-02-29T00:00:00+00:00'")]
        public void AnalyzeExpression_AcceptsRepresentativeSupportedTypesAndOperators(string expression)
        {
            var result = Analyze(expression);
            Assert.True(result.IsSemanticallyValid, Messages(result));
            Assert.Empty(result.SemanticDiagnostics);
        }

        [Fact]
        public void AnalyzeExpression_ReportsEveryInvalidListElement()
        {
            var result = Analyze("Price in [1, 'bad', 'also-bad']");

            Assert.Equal(2, result.SemanticDiagnostics.Count);
            Assert.All(result.SemanticDiagnostics, diagnostic =>
                Assert.Equal("incompatible-operand", diagnostic.Code));
            Assert.Equal(
                new[] { "'bad'", "'also-bad'" },
                result.SemanticDiagnostics.Select(diagnostic =>
                    "Price in [1, 'bad', 'also-bad']".Substring(diagnostic.Start, diagnostic.Length)));
        }

        [Fact]
        public void AnalyzeExpression_UsesDeclaredCollectionElementTypeForMembership()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("values", typeof(decimal[])),
                new ExpressionVariableDefinition("nullableValues", typeof(decimal?[]))
            });

            var valid = Interpreter.AnalyzeExpression<Product>(
                "Price in $values and Discount in $nullableValues",
                Schema,
                variables);
            Assert.True(valid.IsSemanticallyValid, Messages(valid));

            var incompatible = Interpreter.AnalyzeExpression<Product>(
                "Discount in $values",
                Schema,
                variables);
            Assert.Equal("operator-not-applicable", Assert.Single(incompatible.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_IsDeterministicAcrossAmbientCulturesAndParallelCalls()
        {
            var context = new ExpressionConversionContext(
                CultureInfo.InvariantCulture,
                new[] { "yyyy-M-d" });
            var schema = ExpressionSchema.FromType<Product>(conversionContext: context);
            const string expression = "Created = '2020-2-29' and Pric = 1";
            var originalCulture = CultureInfo.CurrentCulture;
            ExpressionSemanticAnalysisResult first;
            ExpressionSemanticAnalysisResult second;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                first = Interpreter.AnalyzeExpression<Product>(expression, schema);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                second = Interpreter.AnalyzeExpression<Product>(expression, schema);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            var expected = DiagnosticFingerprint(first);
            Assert.Equal(expected, DiagnosticFingerprint(second));

            var incompleteEdit = Interpreter.AnalyzeExpression<Product>("Price > ", schema);
            Assert.False(incompleteEdit.IsComplete);
            var afterEdit = Interpreter.AnalyzeExpression<Product>(expression, schema);
            Assert.Equal(expected, DiagnosticFingerprint(afterEdit));

            var leadingWhitespace = Interpreter.AnalyzeExpression<Product>(
                "  " + expression,
                schema);
            Assert.Equal(
                first.SemanticDiagnostics[0].Start + 2,
                leadingWhitespace.SemanticDiagnostics[0].Start);
            Assert.All(leadingWhitespace.Tokens, token =>
            {
                Assert.InRange(token.Start, 0, expression.Length + 2);
                Assert.InRange(token.Start + token.Length, token.Start, expression.Length + 2);
            });
            Assert.All(leadingWhitespace.Diagnostics, diagnostic =>
            {
                Assert.InRange(diagnostic.Start, 0, expression.Length + 2);
                Assert.InRange(
                    diagnostic.Start + diagnostic.Length,
                    diagnostic.Start,
                    expression.Length + 2);
            });

            var results = new string[16];
            Parallel.For(0, results.Length, index =>
            {
                results[index] = DiagnosticFingerprint(
                    Interpreter.AnalyzeExpression<Product>(expression, schema));
            });
            Assert.All(results, actual => Assert.Equal(expected, actual));
        }

        [Theory]
        [InlineData("Huge + 1 > 2", true)]
        [InlineData("Huge + -1 > 2", false)]
        [InlineData("Price + 1.0 > 2", false)]
        [InlineData("Discount + Price > 2", true)]
        [InlineData("Huge + Quantity > 1", false)]
        [InlineData("Character + 1 > 2", true)]
        public void AnalyzeExpression_MatchesRuntimeNumericPromotion(string expression, bool expectedSuccess)
        {
            var analysis = Analyze(expression);
            var predicate = Interpreter.ParsePredicate<Product>(expression);

            Assert.Equal(expectedSuccess, analysis.IsSemanticallyValid);
            Assert.Equal(expectedSuccess, predicate.Succeeded);
        }

        [Fact]
        public void AnalyzeExpression_DiagnosesTemporalConversionsDependingOnMachineDefaults()
        {
            var dateOnly = Analyze("Created = '12:30'");
            var dateOnlyDiagnostic = Assert.Single(dateOnly.SemanticDiagnostics);
            Assert.Equal("context-dependent-conversion", dateOnlyDiagnostic.Code);
            Assert.Equal(typeof(DateTime), Assert.Single(dateOnlyDiagnostic.ExpectedTypes).ClrType);

            var offsetMissing = Analyze("CreatedOffset = '2020-02-29T00:00:00'");
            var offsetDiagnostic = Assert.Single(offsetMissing.SemanticDiagnostics);
            Assert.Equal("context-dependent-conversion", offsetDiagnostic.Code);
            Assert.Equal(typeof(DateTimeOffset), Assert.Single(offsetDiagnostic.ExpectedTypes).ClrType);

            var invalidDate = Analyze("Created = '2020-02-30'");
            Assert.Equal("incompatible-operand", Assert.Single(invalidDate.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_ProvidesOnlyUniquePermittedPropertyCorrections()
        {
            const string typo = "Pric = 1";
            var result = Analyze(typo);
            var diagnostic = Assert.Single(result.SemanticDiagnostics);

            Assert.Equal("unknown-property", diagnostic.Code);
            var suggestion = Assert.Single(diagnostic.Suggestions);
            Assert.Equal("Price", suggestion.ReplacementText);
            Assert.Equal(0, suggestion.Start);
            Assert.Equal(4, suggestion.Length);
            var correctedExpression = typo
                .Remove(suggestion.Start, suggestion.Length)
                .Insert(suggestion.Start, suggestion.ReplacementText);
            Assert.True(Analyze(correctedExpression).IsSemanticallyValid);

            var ambiguousSchema = ExpressionSchema.FromType<AmbiguousProduct>();
            var ambiguous = Interpreter.AnalyzeExpression<AmbiguousProduct>(
                "Pric = 1",
                ambiguousSchema);
            Assert.Empty(Assert.Single(ambiguous.SemanticDiagnostics).Suggestions);

            var shortName = Analyze("P = 1");
            Assert.Empty(Assert.Single(shortName.SemanticDiagnostics).Suggestions);

            var restrictedName = Analyze("Secre = 1");
            Assert.Empty(Assert.Single(restrictedName.SemanticDiagnostics).Suggestions);

            var nested = Analyze("Child.Nam = 'North'");
            var nestedSuggestion = Assert.Single(Assert.Single(nested.SemanticDiagnostics).Suggestions);
            Assert.Equal("Child.Name", nestedSuggestion.ReplacementText);
        }

        [Fact]
        public void AnalyzeExpression_PreservesSpacedPathsAndUtf16Positions()
        {
            var spacedPath = Analyze("Child . Name = 'North'");
            Assert.True(spacedPath.IsSemanticallyValid, Messages(spacedPath));

            var text = "Name = '😀' and Pric = 1";
            var recovered = Analyze(text);
            var suggestion = Assert.Single(
                Assert.Single(recovered.SemanticDiagnostics).Suggestions);
            Assert.Equal(16, suggestion.Start);
            Assert.Equal(4, suggestion.Length);
        }

        [Fact]
        public void AnalyzeExpression_PreservesSyntaxAndSuppressesMissingOperandCascades()
        {
            var result = Analyze("Price > ");

            Assert.False(result.IsComplete);
            Assert.False(result.IsSemanticallyValid);
            Assert.Contains(result.SyntaxDiagnostics, diagnostic => diagnostic.Code == "missing-operand");
            Assert.Empty(result.SemanticDiagnostics);
        }

        [Fact]
        public void AnalyzeExpression_DiagnosesNonBooleanPredicateRoot()
        {
            var result = Analyze("1 + 2");
            var diagnostic = Assert.Single(result.SemanticDiagnostics);

            Assert.True(result.IsComplete);
            Assert.False(result.IsSemanticallyValid);
            Assert.Equal("predicate-result-not-boolean", diagnostic.Code);
            Assert.Equal((0, 5), (diagnostic.Start, diagnostic.Length));
            Assert.Equal(typeof(bool), Assert.Single(diagnostic.ExpectedTypes).ClrType);
            Assert.Equal(typeof(int), Assert.Single(diagnostic.ActualTypes).ClrType);
        }

        [Fact]
        public void AnalyzeExpression_GenericAndRuntimeTypeOverloadsReturnEquivalentResults()
        {
            const string expression = "Price > 'abc' and UnknownField = 1";
            var generic = Interpreter.AnalyzeExpression<Product>(expression, Schema);
            var runtime = Interpreter.AnalyzeExpression(expression, typeof(Product), Schema);

            Assert.Equal(generic.IsComplete, runtime.IsComplete);
            Assert.Equal(generic.IsSemanticallyValid, runtime.IsSemanticallyValid);
            Assert.Equal(
                generic.Tokens.Select(token => (token.Kind, token.Start, token.Length)),
                runtime.Tokens.Select(token => (token.Kind, token.Start, token.Length)));
            Assert.Equal(
                generic.Diagnostics.Select(diagnostic => (diagnostic.Kind, diagnostic.Code, diagnostic.Start, diagnostic.Length)),
                runtime.Diagnostics.Select(diagnostic => (diagnostic.Kind, diagnostic.Code, diagnostic.Start, diagnostic.Length)));
        }

        [Fact]
        public void AnalyzeExpression_ValidatesIndependentSuffixAfterSyntaxRecovery()
        {
            var result = Analyze("Price = ) and UnknownField = 1");

            Assert.False(result.IsComplete);
            Assert.Contains(result.SyntaxDiagnostics, diagnostic => diagnostic.Code == "unexpected-token");
            Assert.Contains(result.SemanticDiagnostics, diagnostic =>
                diagnostic.Code == "unknown-property" && diagnostic.Start == 14);
        }

        [Fact]
        public void AnalyzeExpression_RecoversIndependentClausesInsideUnclosedGroups()
        {
            var result = Analyze("(Price = 1 and UnknownField = 2");

            Assert.False(result.IsComplete);
            Assert.Contains(result.SyntaxDiagnostics, diagnostic =>
                diagnostic.Code == "unmatched-delimiter");
            var semantic = Assert.Single(result.SemanticDiagnostics);
            Assert.Equal("unknown-property", semantic.Code);
            Assert.Equal("UnknownField", "(Price = 1 and UnknownField = 2)"
                .Substring(semantic.Start, semantic.Length));
        }

        [Fact]
        public void AnalyzeExpression_RecoversNestedScopesAndIncompletePathsWithoutCascades()
        {
            var nested = Analyze("((Price = 1 and UnknownField = 2");
            Assert.Contains(nested.SemanticDiagnostics, diagnostic =>
                diagnostic.Code == "unknown-property");

            var propertyPath = Analyze("Price = 1 and Child.");
            Assert.False(propertyPath.IsComplete);
            Assert.Empty(propertyPath.SemanticDiagnostics);

            var variablePath = Analyze("Price = 1 and $user.");
            Assert.False(variablePath.IsComplete);
            Assert.Empty(variablePath.SemanticDiagnostics);

            var quoted = Analyze("Name = 'and or' and UnknownField = 1");
            Assert.Equal("unknown-property", Assert.Single(quoted.SemanticDiagnostics).Code);
        }

        [Fact]
        public void AnalyzeExpression_DoesNotEmitSemanticCascadesForUnterminatedLiteral()
        {
            var result = Analyze("Price = 'abc");

            Assert.False(result.IsComplete);
            Assert.Contains(result.SyntaxDiagnostics, diagnostic => diagnostic.Code == "unterminated-string");
            Assert.Empty(result.SemanticDiagnostics);
        }

        [Fact]
        public void AnalyzeExpression_RejectsWrongSchemaTypeAndInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Interpreter.AnalyzeExpression<Product>("Price = 1", null));
            Assert.Throws<ArgumentNullException>(() =>
                Interpreter.AnalyzeExpression<Product>(null, Schema));
            Assert.Throws<ArgumentNullException>(() =>
                Interpreter.AnalyzeExpression("Price = 1", null, Schema));
            Assert.Throws<ArgumentException>(() =>
                Interpreter.AnalyzeExpression<OtherProduct>("Price = 1", Schema));
        }

        [Fact]
        public void ReadmeQuickStart_UsesImplementedPublicApi()
        {
            var schema = ExpressionSchema.FromType<ReadmeData>(
                validProperties: new[] { "Id", "Name", "IsEnabled" });

            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("minimum", typeof(int))
            });

            var analysis = Interpreter.AnalyzeExpression<ReadmeData>(
                "Id > 'abc' and UnknownField = $minimum",
                schema,
                variables);

            Assert.Equal(
                new[] { "incompatible-operand", "unknown-property" },
                analysis.SemanticDiagnostics.Select(diagnostic => diagnostic.Code));
        }

        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>(
            properties: new[]
            {
                new ExpressionPropertyDefinition(nameof(Product.Secret), typeof(decimal), canQuery: false)
            });

        private static ExpressionSemanticAnalysisResult Analyze(string expression) =>
            Interpreter.AnalyzeExpression<Product>(expression, Schema);

        private static string Messages(ExpressionSemanticAnalysisResult result) =>
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}({diagnostic.Start},{diagnostic.Length}): {diagnostic.Message}"));

        private static string DiagnosticFingerprint(ExpressionSemanticAnalysisResult result) =>
            string.Join("|", result.Diagnostics.Select(diagnostic =>
                string.Join(",",
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Start,
                    diagnostic.Length,
                    string.Join("+", diagnostic.ExpectedTypes.Select(type => type.ClrType?.FullName ?? type.Kind.ToString())),
                    string.Join("+", diagnostic.ActualTypes.Select(type => type.ClrType?.FullName ?? type.Kind.ToString())),
                    string.Join("+", diagnostic.Suggestions.Select(suggestion =>
                        $"{suggestion.Start}:{suggestion.Length}:{suggestion.ReplacementText}")))));

        public sealed class Product
        {
            public decimal Price { get; set; }
            public decimal? Discount { get; set; }
            public int Quantity { get; set; }
            public ulong Huge { get; set; }
            public char Character { get; set; }
            public string Name { get; set; }
            public decimal Secret { get; set; }
            public bool Active { get; set; }
            public DateTime Created { get; set; }
            public DateTimeOffset CreatedOffset { get; set; }
            public ProductChild Child { get; set; }
        }

        public sealed class ProductChild
        {
            public string Name { get; set; }
        }

        public sealed class UserMetadata
        {
            public string Department { get; set; }
            public int Rank;
        }

        public sealed class AmbiguousProduct
        {
            public int Price { get; set; }
            public int Pricy { get; set; }
        }

        public sealed class OtherProduct
        {
            public decimal Price { get; set; }
        }

        public sealed class ReadmeData
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsEnabled { get; set; }
        }

        public sealed class GetterProduct
        {
            public static int GetterCalls;

            public string Name
            {
                get
                {
                    GetterCalls++;
                    throw new InvalidOperationException("Entity getter executed during analysis.");
                }
            }
        }

        public sealed class ThrowingUser
        {
            public static int GetterCalls;

            public string Department
            {
                get
                {
                    GetterCalls++;
                    throw new InvalidOperationException("Variable getter executed during analysis.");
                }
            }
        }

        public sealed class CustomOperatorValue
        {
            public static int OperatorCalls;

            public static bool operator ==(CustomOperatorValue left, CustomOperatorValue right)
            {
                OperatorCalls++;
                throw new InvalidOperationException("Custom operator executed during analysis.");
            }

            public static bool operator !=(CustomOperatorValue left, CustomOperatorValue right) =>
                !(left == right);

            public override bool Equals(object obj) => ReferenceEquals(this, obj);

            public override int GetHashCode() => 0;
        }

        private sealed class CountingResolver : VariableResolver
        {
            public int ResolutionCount { get; private set; }

            protected override Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                ResolutionCount++;
                return Task.FromResult(new VariableInfo
                {
                    Name = name,
                    Resolved = true,
                    Value = 1m
                });
            }
        }
    }
}
