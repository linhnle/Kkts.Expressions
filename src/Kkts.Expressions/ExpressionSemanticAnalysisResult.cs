using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Identifies whether a diagnostic originated in syntax or semantic analysis.</summary>
    public enum ExpressionDiagnosticKind
    {
        Syntax,
        Semantic
    }

    /// <summary>Describes the certainty and runtime type information for an expression value.</summary>
    public enum ExpressionTypeKind
    {
        Clr,
        Null,
        Unknown
    }

    /// <summary>An immutable type description used in semantic diagnostics.</summary>
    public sealed class ExpressionTypeInfo
    {
        private ExpressionTypeInfo(ExpressionTypeKind kind, Type clrType, ExpressionNullability nullability, Type elementType)
        {
            Kind = kind;
            ClrType = clrType;
            Nullability = nullability;
            ElementType = elementType;
        }

        /// <summary>Whether the value has a known CLR type, is a null literal, or is unknown.</summary>
        public ExpressionTypeKind Kind { get; }

        /// <summary>The CLR type for <see cref="ExpressionTypeKind.Clr"/> values; otherwise null.</summary>
        public Type ClrType { get; }

        /// <summary>The nullability known from the type or declaration.</summary>
        public ExpressionNullability Nullability { get; }

        /// <summary>The collection element type, when statically known.</summary>
        public Type ElementType { get; }

        internal static ExpressionTypeInfo ForClr(
            Type clrType,
            ExpressionNullability nullability = ExpressionNullability.Unknown,
            Type elementType = null)
        {
            if (clrType == null) throw new ArgumentNullException(nameof(clrType));
            return new ExpressionTypeInfo(ExpressionTypeKind.Clr, clrType, nullability, elementType);
        }

        internal static ExpressionTypeInfo Null() =>
            new ExpressionTypeInfo(ExpressionTypeKind.Null, null, ExpressionNullability.Nullable, null);

        internal static ExpressionTypeInfo Unknown() =>
            new ExpressionTypeInfo(ExpressionTypeKind.Unknown, null, ExpressionNullability.Unknown, null);
    }

    /// <summary>A source replacement that is safe to offer for a semantic diagnostic.</summary>
    public sealed class ExpressionCorrectionSuggestion
    {
        internal ExpressionCorrectionSuggestion(string message, string replacementText, int start, int length)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            ReplacementText = replacementText ?? throw new ArgumentNullException(nameof(replacementText));
            Start = start;
            Length = length;
        }

        /// <summary>An actionable explanation of the replacement.</summary>
        public string Message { get; }

        /// <summary>The text to insert in place of the source span.</summary>
        public string ReplacementText { get; }

        /// <summary>The zero-based UTF-16 offset into the original input.</summary>
        public int Start { get; }

        /// <summary>The UTF-16 replacement length in the original input.</summary>
        public int Length { get; }
    }

    /// <summary>A positioned syntax or semantic diagnostic with optional type and correction details.</summary>
    public sealed class ExpressionDiagnostic
    {
        internal ExpressionDiagnostic(
            ExpressionDiagnosticKind kind,
            string code,
            string message,
            int start,
            int length,
            IEnumerable<ExpressionTypeInfo> expectedTypes = null,
            IEnumerable<ExpressionTypeInfo> actualTypes = null,
            IEnumerable<ExpressionCorrectionSuggestion> suggestions = null)
        {
            Kind = kind;
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Start = start;
            Length = length;
            ExpectedTypes = Array.AsReadOnly((expectedTypes ?? Enumerable.Empty<ExpressionTypeInfo>()).ToArray());
            ActualTypes = Array.AsReadOnly((actualTypes ?? Enumerable.Empty<ExpressionTypeInfo>()).ToArray());
            Suggestions = Array.AsReadOnly((suggestions ?? Enumerable.Empty<ExpressionCorrectionSuggestion>()).ToArray());
        }

        /// <summary>Whether this diagnostic was produced by syntax or semantic analysis.</summary>
        public ExpressionDiagnosticKind Kind { get; }

        /// <summary>A stable machine-readable diagnostic code.</summary>
        public string Code { get; }

        /// <summary>A human-readable diagnostic message.</summary>
        public string Message { get; }

        /// <summary>The zero-based UTF-16 offset into the original input.</summary>
        public int Start { get; }

        /// <summary>The UTF-16 source span length; zero denotes an end-of-input caret.</summary>
        public int Length { get; }

        /// <summary>Types expected by the relevant semantic rule, when known.</summary>
        public IReadOnlyList<ExpressionTypeInfo> ExpectedTypes { get; }

        /// <summary>Types found in the source, when known.</summary>
        public IReadOnlyList<ExpressionTypeInfo> ActualTypes { get; }

        /// <summary>Conservative, permission-safe source replacements, when available.</summary>
        public IReadOnlyList<ExpressionCorrectionSuggestion> Suggestions { get; }

        internal static ExpressionDiagnostic FromSyntax(ExpressionSyntaxDiagnostic diagnostic)
        {
            return new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Syntax,
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Start,
                diagnostic.Length);
        }
    }

    /// <summary>An immutable syntax and metadata-based semantic analysis snapshot.</summary>
    public sealed class ExpressionSemanticAnalysisResult
    {
        internal ExpressionSemanticAnalysisResult(
            ExpressionAnalysisResult syntaxAnalysis,
            IEnumerable<ExpressionDiagnostic> semanticDiagnostics,
            bool isSemanticallyValid)
        {
            SyntaxAnalysis = syntaxAnalysis ?? throw new ArgumentNullException(nameof(syntaxAnalysis));
            SemanticDiagnostics = Array.AsReadOnly((semanticDiagnostics ?? Enumerable.Empty<ExpressionDiagnostic>()).ToArray());
            Diagnostics = Array.AsReadOnly(
                syntaxAnalysis.Diagnostics.Select(ExpressionDiagnostic.FromSyntax)
                    .Concat(SemanticDiagnostics)
                    .OrderBy(diagnostic => diagnostic.Start)
                    .ThenBy(diagnostic => diagnostic.Length)
                    .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                    .GroupBy(diagnostic => (diagnostic.Code, diagnostic.Start, diagnostic.Length))
                    .Select(group => group.First())
                    .ToArray());
            IsSemanticallyValid = isSemanticallyValid &&
                syntaxAnalysis.IsComplete &&
                SemanticDiagnostics.Count == 0;
        }

        /// <summary>The existing immutable syntax-analysis snapshot.</summary>
        public ExpressionAnalysisResult SyntaxAnalysis { get; }

        /// <summary>Classified source tokens, identical to the syntax snapshot's tokens.</summary>
        public IReadOnlyList<ExpressionToken> Tokens => SyntaxAnalysis.Tokens;

        /// <summary>Original syntax diagnostics, unchanged from the syntax snapshot.</summary>
        public IReadOnlyList<ExpressionSyntaxDiagnostic> SyntaxDiagnostics => SyntaxAnalysis.Diagnostics;

        /// <summary>Semantic diagnostics with structured types and safe corrections.</summary>
        public IReadOnlyList<ExpressionDiagnostic> SemanticDiagnostics { get; }

        /// <summary>Combined syntax and semantic diagnostics in deterministic source order.</summary>
        public IReadOnlyList<ExpressionDiagnostic> Diagnostics { get; }

        /// <summary>Whether the expression is syntactically complete, as defined by syntax analysis.</summary>
        public bool IsComplete => SyntaxAnalysis.IsComplete;

        /// <summary>Whether complete syntax has no semantic errors and a Boolean predicate result.</summary>
        public bool IsSemanticallyValid { get; }
    }
}
