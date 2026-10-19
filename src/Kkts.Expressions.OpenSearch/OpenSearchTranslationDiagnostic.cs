using System;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Describes why part of an expression could not be translated to OpenSearch Query DSL.
    /// Messages never contain captured or constant values.
    /// </summary>
    public sealed class OpenSearchTranslationDiagnostic : IEquatable<OpenSearchTranslationDiagnostic>
    {
        public OpenSearchTranslationDiagnostic(string code, string message, string memberPath = null, string expressionText = null)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A diagnostic code is required.", nameof(code));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A diagnostic message is required.", nameof(message));
            Code = code;
            Message = message;
            MemberPath = memberPath;
            ExpressionText = expressionText;
        }

        public string Code { get; }

        public string Message { get; }

        /// <summary>The CLR member path involved, such as <c>Customer.Name</c>, when applicable.</summary>
        public string MemberPath { get; }

        /// <summary>The offending sub-expression with constant values redacted, when applicable.</summary>
        public string ExpressionText { get; }

        public bool Equals(OpenSearchTranslationDiagnostic other)
        {
            return other != null &&
                Code == other.Code &&
                Message == other.Message &&
                MemberPath == other.MemberPath &&
                ExpressionText == other.ExpressionText;
        }

        public override bool Equals(object obj) => Equals(obj as OpenSearchTranslationDiagnostic);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Code.GetHashCode();
                hash = hash * 31 + Message.GetHashCode();
                hash = hash * 31 + (MemberPath?.GetHashCode() ?? 0);
                return hash * 31 + (ExpressionText?.GetHashCode() ?? 0);
            }
        }

        public override string ToString()
        {
            var text = Code + ": " + Message;
            if (MemberPath != null) text += " Member: " + MemberPath + ".";
            if (ExpressionText != null) text += " Expression: " + ExpressionText;
            return text;
        }
    }
}
