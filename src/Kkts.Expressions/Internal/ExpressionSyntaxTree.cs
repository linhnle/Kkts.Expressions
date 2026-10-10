using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
    internal enum ExpressionSyntaxNodeKind
    {
        Property,
        Variable,
        Literal,
        Unary,
        Binary,
        Function,
        Group,
        List
    }

    internal sealed class ExpressionSyntaxNode
    {
        internal ExpressionSyntaxNode(
            ExpressionSyntaxNodeKind kind,
            int start,
            int length,
            string text = null,
            string normalizedText = null,
            char delimiter = '\0',
            IEnumerable<ExpressionSyntaxNode> children = null)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Kind = kind;
            Start = start;
            Length = length;
            Text = text;
            NormalizedText = normalizedText;
            Delimiter = delimiter;
            Children = Array.AsReadOnly((children ?? Enumerable.Empty<ExpressionSyntaxNode>()).ToArray());
        }

        internal ExpressionSyntaxNodeKind Kind { get; }
        internal int Start { get; }
        internal int Length { get; }
        internal int End => Start + Length;
        internal string Text { get; }
        internal string NormalizedText { get; }
        internal char Delimiter { get; }
        internal IReadOnlyList<ExpressionSyntaxNode> Children { get; }
    }

    internal sealed class ExpressionSyntaxTree
    {
        private ExpressionSyntaxTree(
            string source,
            ExpressionAnalysisResult analysis,
            ExpressionSyntaxNode root)
        {
            Source = source;
            Analysis = analysis;
            Root = root;
        }

        internal string Source { get; }
        internal ExpressionAnalysisResult Analysis { get; }
        internal ExpressionSyntaxNode Root { get; }

        internal static ExpressionSyntaxTree Parse(string source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var analysis = new ExpressionAnalyzer(source).Analyze();
            if (!analysis.IsComplete)
                return new ExpressionSyntaxTree(source, analysis, null);

            var parser = new SyntaxTreeParser(source, analysis.Tokens);
            var root = parser.Parse();
            return new ExpressionSyntaxTree(source, analysis, root);
        }

        private sealed class SyntaxTreeParser
        {
            private readonly string _source;
            private readonly IReadOnlyList<ExpressionToken> _tokens;
            private int _index;

            internal SyntaxTreeParser(string source, IReadOnlyList<ExpressionToken> tokens)
            {
                _source = source;
                _tokens = tokens;
            }

            internal ExpressionSyntaxNode Parse()
            {
                var root = ParseExpression(0);
                if (_index != _tokens.Count)
                    throw SyntaxError(_tokens[_index]);
                return root;
            }

            private ExpressionSyntaxNode ParseExpression(int minimumPrecedence)
            {
                var left = ParsePrefix();
                while (_index < _tokens.Count)
                {
                    if (IsPunctuation(".") && _index + 2 < _tokens.Count &&
                        _tokens[_index + 1].Kind == ExpressionTokenKind.Operator &&
                        IsPunctuationAt(_index + 2, "("))
                    {
                        var opToken = _tokens[_index + 1];
                        var opText = Text(opToken);
                        if (3 < minimumPrecedence) break;
                        _index += 3;
                        var argument = ParseExpression(0);
                        var end = argument.End;
                        if (!IsPunctuation(")")) throw SyntaxError(CurrentToken);
                        end = _tokens[_index++].Start + _tokens[_index - 1].Length;
                        left = new ExpressionSyntaxNode(
                            ExpressionSyntaxNodeKind.Function,
                            left.Start,
                            end - left.Start,
                            opText,
                            ExpressionGrammar.NormalizeOperator(opText),
                            children: new[] { left, argument });
                        continue;
                    }

                    if (CurrentToken.Kind != ExpressionTokenKind.Operator) break;
                    var op = CurrentToken;
                    var sourceOperator = Text(op);
                    var precedence = ExpressionGrammar.GetBinaryPrecedence(sourceOperator);
                    if (precedence < minimumPrecedence) break;
                    ++_index;

                    ExpressionSyntaxNode right;
                    if (Interpreter.IsMembership(sourceOperator) && IsListStart(CurrentToken))
                        right = ParseList();
                    else
                        right = ParseExpression(precedence + 1);

                    left = new ExpressionSyntaxNode(
                        ExpressionSyntaxNodeKind.Binary,
                        left.Start,
                        right.End - left.Start,
                        sourceOperator,
                        ExpressionGrammar.NormalizeOperator(sourceOperator),
                        children: new[] { left, right });
                }
                return left;
            }

            private ExpressionSyntaxNode ParsePrefix()
            {
                var token = CurrentToken;
                var text = Text(token);
                if (token.Kind == ExpressionTokenKind.Operator &&
                    (text == "!" || text.Equals("not", StringComparison.OrdinalIgnoreCase)))
                {
                    ++_index;
                    ExpressionSyntaxNode operand;
                    if (IsPunctuation("("))
                    {
                        var open = _tokens[_index++];
                        var grouped = ParseExpression(0);
                        if (!IsPunctuation(")")) throw SyntaxError(CurrentToken);
                        var close = _tokens[_index++];
                        operand = new ExpressionSyntaxNode(
                            ExpressionSyntaxNodeKind.Group,
                            open.Start,
                            close.Start + close.Length - open.Start,
                            delimiter: '(',
                            children: new[] { grouped });
                        return new ExpressionSyntaxNode(
                            ExpressionSyntaxNodeKind.Unary,
                            token.Start,
                            close.Start + close.Length - token.Start,
                            text,
                            ExpressionGrammar.NormalizeOperator(text),
                            children: new[] { operand });
                    }
                    operand = ParseExpression(5);
                    return new ExpressionSyntaxNode(
                        ExpressionSyntaxNodeKind.Unary,
                        token.Start,
                        operand.End - token.Start,
                        text,
                        ExpressionGrammar.NormalizeOperator(text),
                        children: new[] { operand });
                }

                if (IsPunctuation("("))
                {
                    var open = _tokens[_index++];
                    var child = ParseExpression(0);
                    if (!IsPunctuation(")")) throw SyntaxError(CurrentToken);
                    var close = _tokens[_index++];
                    return new ExpressionSyntaxNode(
                        ExpressionSyntaxNodeKind.Group,
                        open.Start,
                        close.Start + close.Length - open.Start,
                        delimiter: '(',
                        children: new[] { child });
                }

                if (IsListStart(token)) return ParseList();
                if (token.Kind == ExpressionTokenKind.Punctuation || token.Kind == ExpressionTokenKind.Unknown)
                    throw SyntaxError(token);
                ++_index;
                var kind = token.Kind == ExpressionTokenKind.Property
                    ? ExpressionSyntaxNodeKind.Property
                    : token.Kind == ExpressionTokenKind.Variable
                        ? ExpressionSyntaxNodeKind.Variable
                        : ExpressionSyntaxNodeKind.Literal;
                return new ExpressionSyntaxNode(kind, token.Start, token.Length, text);
            }

            private ExpressionSyntaxNode ParseList()
            {
                var open = _tokens[_index++];
                var openText = Text(open);
                var closeText = openText == "[" ? "]" : openText == "{" ? "}" : ")";
                var children = new List<ExpressionSyntaxNode>();
                while (!IsPunctuation(closeText))
                {
                    if (_index >= _tokens.Count) throw SyntaxError(open);
                    children.Add(ParseExpression(0));
                    if (IsPunctuation(",")) ++_index;
                    else if (!IsPunctuation(closeText)) throw SyntaxError(CurrentToken);
                }
                var close = _tokens[_index++];
                return new ExpressionSyntaxNode(
                    ExpressionSyntaxNodeKind.List,
                    open.Start,
                    close.Start + close.Length - open.Start,
                    delimiter: openText[0],
                    children: children);
            }

            private ExpressionToken CurrentToken =>
                _index < _tokens.Count ? _tokens[_index] : null;

            private bool IsPunctuation(string text) => IsPunctuationAt(_index, text);

            private bool IsPunctuationAt(int index, string text) =>
                index < _tokens.Count &&
                _tokens[index].Kind == ExpressionTokenKind.Punctuation &&
                Text(_tokens[index]) == text;

            private bool IsListStart(ExpressionToken token) =>
                token != null &&
                token.Kind == ExpressionTokenKind.Punctuation &&
                ExpressionGrammar.IsListStart(_source[token.Start]);

            private string Text(ExpressionToken token) =>
                _source.Substring(token.Start, token.Length);

            private FormatException SyntaxError(ExpressionToken token) =>
                new FormatException(token == null
                    ? $"Incorrect syntax at index {_source.Length}"
                    : $"Incorrect syntax near '{Text(token)}', index {token.Start}");
        }
    }
}
