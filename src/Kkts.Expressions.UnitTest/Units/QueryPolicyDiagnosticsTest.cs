using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyDiagnosticsTest
    {
        [Fact]
        public void ExpressionDiagnostic_SnapshotsPolicyMetadataAndInputPath()
        {
            var diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "query-policy-in-items-exceeded",
                "The collection contains too many items.",
                12,
                1,
                configuredLimit: 2,
                observedValue: 3,
                observedValueIsLowerBound: true,
                inputPath: "Filters[2].Value");

            Assert.Equal(2, diagnostic.ConfiguredLimit);
            Assert.Equal(3, diagnostic.ObservedValue);
            Assert.True(diagnostic.ObservedValueIsLowerBound);
            Assert.Equal("Filters[2].Value", diagnostic.InputPath);
            Assert.Equal(0, (int)ExpressionDiagnosticKind.Syntax);
            Assert.Equal(1, (int)ExpressionDiagnosticKind.Semantic);
        }

        [Fact]
        public void ExpressionDiagnostic_RejectsNegativeLimitMetadata()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "query-policy-test",
                "Invalid limit.",
                0,
                0,
                configuredLimit: -1));

            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "query-policy-test",
                "Invalid observed value.",
                0,
                0,
                observedValue: -1));
        }

        [Fact]
        public void EvaluationResultBase_SnapshotsDiagnosticsAndGenericConversionPreservesThem()
        {
            var diagnostics = new List<ExpressionDiagnostic> { CreateDiagnostic() };
            var result = new EvaluationResult { Diagnostics = diagnostics };
            diagnostics.Clear();

            var generic = result.ToGeneric<object, bool>();

            Assert.Single(result.Diagnostics);
            Assert.Single(generic.Diagnostics);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionDiagnostic>)generic.Diagnostics).Clear());
            Assert.Empty(new EvaluationResult().Diagnostics);
        }

        [Fact]
        public void QueryPolicyException_SnapshotsDiagnostics()
        {
            var diagnostics = new List<ExpressionDiagnostic> { CreateDiagnostic() };
            var exception = new QueryPolicyException(diagnostics);
            diagnostics.Clear();

            Assert.Single(exception.Diagnostics);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ExpressionDiagnostic>)exception.Diagnostics).Clear());
            Assert.Throws<ArgumentNullException>(() => new QueryPolicyException(null));
        }

        [Fact]
        public void SemanticResult_ReportsIntentionalTruncationWithoutChangingDefaults()
        {
            var syntax = Interpreter.AnalyzeExpression("Id = 1");
            var complete = new ExpressionSemanticAnalysisResult(syntax, null, true);
            var truncated = new ExpressionSemanticAnalysisResult(syntax, new[] { CreateDiagnostic() }, false, true);

            Assert.False(complete.IsTruncated);
            Assert.True(truncated.IsTruncated);
            Assert.False(truncated.IsSemanticallyValid);
        }

        private static ExpressionDiagnostic CreateDiagnostic()
        {
            return new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "query-policy-expression-length-exceeded",
                "The expression exceeds the configured limit.",
                0,
                1,
                configuredLimit: 10,
                observedValue: 11);
        }
    }
}
