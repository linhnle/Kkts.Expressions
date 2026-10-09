using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionSemanticAnalysisResultTest
    {
        [Fact]
        public void Result_PreservesSyntaxSnapshotAndCreatesReadOnlyCombinedDiagnostics()
        {
            var syntax = Interpreter.AnalyzeExpression("Unknown = 1");
            var semanticDiagnostics = new List<ExpressionDiagnostic>
            {
                new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    "unknown-property",
                    "Unknown property.",
                    0,
                    7)
            };
            var result = new ExpressionSemanticAnalysisResult(syntax, semanticDiagnostics, false);
            semanticDiagnostics.Clear();

            Assert.Same(syntax, result.SyntaxAnalysis);
            Assert.Same(syntax.Tokens, result.Tokens);
            Assert.Same(syntax.Diagnostics, result.SyntaxDiagnostics);
            Assert.Empty(result.SyntaxDiagnostics);
            Assert.Single(result.SemanticDiagnostics);
            Assert.Single(result.Diagnostics);
            Assert.False(result.IsSemanticallyValid);
            Assert.True(result.IsComplete);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionDiagnostic>)result.SemanticDiagnostics).Clear());
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionDiagnostic>)result.Diagnostics).Clear());
        }

        [Fact]
        public void Result_CombinesAndOrdersSyntaxAndSemanticDiagnostics()
        {
            var syntax = Interpreter.AnalyzeExpression("Id = )");
            var semantic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "unknown-property",
                "Unknown property.",
                0,
                2);
            var result = new ExpressionSemanticAnalysisResult(syntax, new[] { semantic }, false);

            Assert.Equal(
                new[] { (ExpressionDiagnosticKind.Semantic, 0, "unknown-property"), (ExpressionDiagnosticKind.Syntax, 5, "unexpected-token") },
                result.Diagnostics.Select(diagnostic => (diagnostic.Kind, diagnostic.Start, diagnostic.Code)));
            Assert.False(result.IsComplete);
            Assert.False(result.IsSemanticallyValid);
        }

        [Fact]
        public void TypeAndSuggestionContractsExposeStructuredImmutableData()
        {
            var expected = ExpressionTypeInfo.ForClr(typeof(decimal), ExpressionNullability.NonNullable);
            var actual = ExpressionTypeInfo.ForClr(typeof(string));
            var types = new List<ExpressionTypeInfo> { expected };
            var suggestions = new List<ExpressionCorrectionSuggestion>
            {
                new ExpressionCorrectionSuggestion("Use Price.", "Price", 0, 4)
            };
            var diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "incompatible-operand",
                "Expected a decimal value.",
                8,
                5,
                types,
                new[] { actual },
                suggestions);
            types.Clear();
            suggestions.Clear();

            Assert.Equal(ExpressionTypeKind.Clr, expected.Kind);
            Assert.Equal(typeof(decimal), expected.ClrType);
            Assert.Equal(ExpressionNullability.NonNullable, expected.Nullability);
            Assert.Equal(ExpressionTypeKind.Null, ExpressionTypeInfo.Null().Kind);
            Assert.Null(ExpressionTypeInfo.Null().ClrType);
            Assert.Equal(ExpressionTypeKind.Unknown, ExpressionTypeInfo.Unknown().Kind);
            Assert.Single(diagnostic.ExpectedTypes);
            Assert.Single(diagnostic.ActualTypes);
            Assert.Single(diagnostic.Suggestions);
            Assert.Equal("Price", diagnostic.Suggestions[0].ReplacementText);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionTypeInfo>)diagnostic.ExpectedTypes).Clear());
        }
    }
}
