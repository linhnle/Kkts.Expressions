using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>Converts between expressions and the representable nested filter-tree subset.</summary>
    public static class FilterExpression
    {
        /// <summary>Parses a supported expression into a schema-validated filter tree.</summary>
        public static FilterOperationResult<FilterNode> Parse(
            string expression,
            ExpressionSchema schema,
            ExpressionVariableSchema variables = null)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var syntax = ExpressionSyntaxTree.Parse(expression);
            if (!syntax.Analysis.IsComplete)
                return FilterOperationResult<FilterNode>.Failure(
                    syntax.Analysis.Diagnostics.Select(ExpressionDiagnostic.FromSyntax));

            try
            {
                var tree = ConvertNode(syntax.Root, expression, schema);
                var validation = FilterTreeSemanticValidator.Validate(tree, schema, variables);
                return validation.Succeeded
                    ? FilterOperationResult<FilterNode>.Success(tree)
                    : validation;
            }
            catch (FilterConversionException exception)
            {
                return FilterOperationResult<FilterNode>.Failure(new[]
                {
                    Unsupported(exception.Node.Start, exception.Node.Length, exception.Message)
                });
            }
            catch (FormatException exception)
            {
                return FilterOperationResult<FilterNode>.Failure(new[]
                {
                    Unsupported(0, expression.Length, exception.Message)
                });
            }
        }

        /// <summary>Formats a schema-valid tree as deterministic canonical expression text.</summary>
        public static FilterOperationResult<string> Format(
            FilterNode tree,
            ExpressionSchema schema,
            ExpressionVariableSchema variables = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var validation = FilterTreeSemanticValidator.Validate(tree, schema, variables);
            if (!validation.Succeeded)
                return FilterOperationResult<string>.Failure(validation.Diagnostics);

            try
            {
                return FilterOperationResult<string>.Success(FormatTree(tree));
            }
            catch (FilterConversionException exception)
            {
                return FilterOperationResult<string>.Failure(new[]
                {
                    new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        "filter-conversion-unsupported",
                        exception.Message,
                        0,
                        0,
                        inputPath: exception.Path)
                });
            }
        }

        internal static ExpressionDiagnostic Unsupported(int start, int length, string message) =>
            new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                "filter-conversion-unsupported",
                message,
                start,
                length);

        private static FilterNode ConvertNode(ExpressionSyntaxNode root, string source, ExpressionSchema schema)
        {
            var result = new Dictionary<ExpressionSyntaxNode, FilterNode>();
            var pending = new Stack<Tuple<ExpressionSyntaxNode, bool>>();
            pending.Push(Tuple.Create(root, false));
            while (pending.Count > 0)
            {
                var (node, visited) = pending.Pop();
                var isLogicalBinary = node.Kind == ExpressionSyntaxNodeKind.Binary &&
                    (node.NormalizedText.Equals(Interpreter.LogicalAnd, StringComparison.OrdinalIgnoreCase) ||
                     node.NormalizedText.Equals(Interpreter.LogicalOr, StringComparison.OrdinalIgnoreCase));
                if (!visited &&
                    (node.Kind == ExpressionSyntaxNodeKind.Group ||
                     node.Kind == ExpressionSyntaxNodeKind.Unary ||
                     isLogicalBinary))
                {
                    pending.Push(Tuple.Create(node, true));
                    for (var index = node.Children.Count - 1; index >= 0; --index)
                        pending.Push(Tuple.Create(node.Children[index], false));
                    continue;
                }

                if (node.Kind == ExpressionSyntaxNodeKind.Group)
                    result[node] = result[node.Children[0]];
                else if (node.Kind == ExpressionSyntaxNodeKind.Unary &&
                         (node.NormalizedText.Equals("not", StringComparison.OrdinalIgnoreCase) ||
                          node.NormalizedText == Interpreter.LogicalNot))
                    result[node] = FilterNode.Not(result[node.Children[0]]);
                else if (node.Kind == ExpressionSyntaxNodeKind.Binary &&
                         node.NormalizedText.Equals(Interpreter.LogicalAnd, StringComparison.OrdinalIgnoreCase))
                    result[node] = Combine(FilterNodeKind.And, node.Children, result);
                else if (node.Kind == ExpressionSyntaxNodeKind.Binary &&
                         node.NormalizedText.Equals(Interpreter.LogicalOr, StringComparison.OrdinalIgnoreCase))
                    result[node] = Combine(FilterNodeKind.Or, node.Children, result);
                else if (node.Kind == ExpressionSyntaxNodeKind.Binary &&
                         Interpreter.ComparisonOperators.Contains(
                             Interpreter.NormalizeComparisonOperator(node.NormalizedText),
                             StringComparer.OrdinalIgnoreCase))
                    result[node] = ConvertComparison(node, source);
                else if (node.Kind == ExpressionSyntaxNodeKind.Function &&
                         Interpreter.ComparisonFunctionOperators.Contains(
                             node.NormalizedText,
                             StringComparer.OrdinalIgnoreCase))
                    result[node] = ConvertFunction(node, source);
                else if (node.Kind == ExpressionSyntaxNodeKind.Property)
                    result[node] = ConvertBooleanField(node, schema);
                else
                    throw new FilterConversionException(
                        node,
                        "This expression construct is outside the supported filter-tree subset.");
            }
            return result[root];
        }

        private static FilterNode Combine(
            FilterNodeKind kind,
            IReadOnlyList<ExpressionSyntaxNode> operands,
            IReadOnlyDictionary<ExpressionSyntaxNode, FilterNode> converted)
        {
            var children = new List<FilterNode>();
            foreach (var operand in operands)
            {
                var child = converted[operand];
                if (child.Kind == kind) children.AddRange(child.Children);
                else children.Add(child);
            }
            return kind == FilterNodeKind.And
                ? FilterNode.And(children)
                : FilterNode.Or(children);
        }

        private static FilterNode ConvertComparison(ExpressionSyntaxNode node, string source)
        {
            if (node.Children.Count != 2 || node.Children[0].Kind != ExpressionSyntaxNodeKind.Property)
                throw new FilterConversionException(
                    node,
                    "Comparisons must have one entity field on the left and a literal or explicit variable on the right.");
            var valueNode = node.Children[1];
            var value = ReadValue(valueNode, source, Interpreter.IsMembership(node.NormalizedText));
            return FilterNode.Condition(
                node.Children[0].Text,
                Interpreter.NormalizeComparisonOperator(node.NormalizedText),
                value);
        }

        private static FilterNode ConvertFunction(ExpressionSyntaxNode node, string source)
        {
            if (node.Children.Count != 2 || node.Children[0].Kind != ExpressionSyntaxNodeKind.Property)
                throw new FilterConversionException(
                    node,
                    "String functions require one entity field receiver and a literal or explicit variable argument.");
            return FilterNode.Condition(
                node.Children[0].Text,
                CanonicalOperator(Interpreter.NormalizeComparisonOperator(node.NormalizedText)),
                ReadValue(node.Children[1], source, false));
        }

        private static FilterNode ConvertBooleanField(ExpressionSyntaxNode node, ExpressionSchema schema)
        {
            if (schema.IsPropertyQueryable(node.Text) &&
                schema.TryMapProperty(node.Text, out var mappedPath) &&
                schema.TryGetProperty(mappedPath, out var fieldType, out _, out _) &&
                (Nullable.GetUnderlyingType(fieldType) ?? fieldType) != typeof(bool))
                throw new FilterConversionException(
                    node,
                    "Only a Boolean entity field can be used as a standalone predicate.");
            return FilterNode.Condition(node.Text, "=", FilterValue.Boolean(true));
        }

        private static FilterValue ReadValue(
            ExpressionSyntaxNode node,
            string source,
            bool membership)
        {
            if (node.Kind == ExpressionSyntaxNodeKind.List && membership)
                return FilterValue.Collection(node.Children.Select(child => ReadScalar(child, source)));
            return ReadScalar(node, source);
        }

        private static FilterValue ReadScalar(ExpressionSyntaxNode node, string source)
        {
            if (node.Kind == ExpressionSyntaxNodeKind.Variable)
            {
                var name = node.Text.Length > 0 &&
                           node.Text[0] == VariableResolver.VariablePrefix
                    ? node.Text.Substring(1)
                    : node.Text;
                return FilterValue.Variable(name);
            }
            if (node.Kind != ExpressionSyntaxNodeKind.Literal)
                throw new FilterConversionException(node, "A condition value must be a scalar literal or variable reference.");

            var text = node.Text;
            if (text.Length >= 2 && ExpressionGrammar.IsQuote(text[0]) && text[text.Length - 1] == text[0])
                return FilterValue.String(UnescapeString(text.Substring(1, text.Length - 2), text[0]));
            if (text.Equals("null", StringComparison.OrdinalIgnoreCase)) return FilterValue.Null;
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return FilterValue.Boolean(true);
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return FilterValue.Boolean(false);
            try
            {
                return FilterValue.Number(text);
            }
            catch (ArgumentException)
            {
            }
            throw new FilterConversionException(node, "The expression literal has no lossless filter-value representation.");
        }

        private static string UnescapeString(string text, char quote)
        {
            return ExpressionLiteralCodec.DecodeString(text, quote);
        }

        private static string FormatTree(FilterNode tree)
        {
            var output = new StringBuilder();
            var pending = new Stack<FormatAction>();
            pending.Push(FormatAction.Node(tree, 0));
            while (pending.Count > 0)
            {
                var action = pending.Pop();
                if (action.Text != null)
                {
                    output.Append(action.Text);
                    continue;
                }
                var node = action.Filter;
                var precedence = GetPrecedence(node);
                var wrap = precedence < action.ParentPrecedence;
                if (wrap) output.Append('(');
                if (node.Kind == FilterNodeKind.Condition)
                {
                    AppendCondition(output, node, action.Path);
                    if (wrap) output.Append(')');
                    continue;
                }
                if (node.Kind == FilterNodeKind.Not)
                {
                    output.Append("not (");
                    pending.Push(FormatAction.TextAction(")"));
                    pending.Push(FormatAction.Node(node.Child, 0, AppendPath(action.Path, "not")));
                    continue;
                }

                var isAnd = node.Kind == FilterNodeKind.And;
                pending.Push(FormatAction.TextAction(wrap ? ")" : string.Empty));
                for (var index = node.Children.Count - 1; index >= 0; --index)
                {
                    pending.Push(FormatAction.Node(
                        node.Children[index],
                        precedence,
                        AppendPath(AppendPath(action.Path, isAnd ? "and" : "or"), index.ToString(CultureInfo.InvariantCulture))));
                    if (index > 0)
                        pending.Push(FormatAction.TextAction(isAnd ? " and " : " or "));
                }
            }
            return output.ToString();
        }

        private static void AppendCondition(StringBuilder output, FilterNode node, string path)
        {
            var op = Interpreter.NormalizeComparisonOperator(node.Operator);
            output.Append(node.Field);
            var canonical = CanonicalOperator(op);
            if (canonical == Interpreter.ComparisonContains ||
                canonical == Interpreter.ComparisonStartsWith ||
                canonical == Interpreter.ComparisonEndsWith)
            {
                output.Append('.').Append(canonical).Append('(');
                AppendValue(output, node.Value, path + "/value", inMembershipList: false);
                output.Append(')');
                return;
            }

            output.Append(' ').Append(CanonicalOperator(op)).Append(' ');
            AppendValue(output, node.Value, path + "/value", Interpreter.IsMembership(op));
        }

        private static void AppendValue(StringBuilder output, FilterValue value, string path, bool inMembershipList)
        {
            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    output.Append("null");
                    break;
                case FilterValueKind.Boolean:
                    output.Append(value.BooleanValue ? "true" : "false");
                    break;
                case FilterValueKind.Number:
                    if (value.Text.IndexOfAny(new[] { 'e', 'E' }) >= 0)
                        throw new FilterConversionException(path, "Exponent-form numbers cannot be preserved by the expression grammar.");
                    try
                    {
                        var parsed = NumericOperands.ParseLiteral(value.Text);
                        if (parsed.Type == typeof(double))
                        {
                            if (!decimal.TryParse(
                                    value.Text,
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out var exact) ||
                                (decimal)(double)parsed.Value != exact)
                                throw new FilterConversionException(path, "This number cannot be preserved exactly by the expression grammar.");
                        }
                    }
                    catch (Exception exception) when (exception is FormatException || exception is OverflowException)
                    {
                        throw new FilterConversionException(path, "This number cannot be represented by the expression grammar.");
                    }
                    output.Append(value.Text);
                    break;
                case FilterValueKind.String:
                    if (inMembershipList && value.Text.StartsWith(
                            VariableResolver.VariablePrefixString,
                            StringComparison.Ordinal))
                        throw new FilterConversionException(path, "A membership literal beginning with the variable prefix is ambiguous in expression text.");
                    output.Append(Quote(value.Text));
                    break;
                case FilterValueKind.Variable:
                    output.Append(VariableResolver.VariablePrefix).Append(value.Text);
                    break;
                case FilterValueKind.Collection:
                    output.Append('[');
                    for (var index = 0; index < value.Items.Count; ++index)
                    {
                        if (index > 0) output.Append(", ");
                        AppendValue(
                            output,
                            value.Items[index],
                            AppendPath(path, index.ToString(CultureInfo.InvariantCulture)),
                            inMembershipList: true);
                    }
                    output.Append(']');
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown filter value kind.");
            }
        }

        private static string Quote(string value)
        {
            return ExpressionLiteralCodec.Quote(value);
        }

        private static string CanonicalOperator(string op)
        {
            return ExpressionLiteralCodec.CanonicalOperator(op);
        }

        private static int GetPrecedence(FilterNode node)
        {
            if (node.Kind == FilterNodeKind.Or) return 1;
            if (node.Kind == FilterNodeKind.And) return 2;
            if (node.Kind == FilterNodeKind.Not) return 3;
            return 4;
        }

        private static string AppendPath(string path, string segment) =>
            FilterTreeDiagnosticProjection.AppendPointer(path, segment);

        private sealed class FilterConversionException : Exception
        {
            internal FilterConversionException(ExpressionSyntaxNode node, string message)
                : base(message) => Node = node;

            internal FilterConversionException(string path, string message)
                : base(message) => Path = path;

            internal ExpressionSyntaxNode Node { get; }
            internal string Path { get; }
        }

        private sealed class FormatAction
        {
            private FormatAction(FilterNode filter, int parentPrecedence, string path, string text)
            {
                Filter = filter;
                ParentPrecedence = parentPrecedence;
                Path = path;
                Text = text;
            }

            internal FilterNode Filter { get; }
            internal int ParentPrecedence { get; }
            internal string Path { get; }
            internal string Text { get; }

            internal static FormatAction Node(FilterNode node, int parentPrecedence, string path = "") =>
                new FormatAction(node, parentPrecedence, path, null);
            internal static FormatAction TextAction(string text) =>
                new FormatAction(null, 0, null, text);
        }
    }
}
