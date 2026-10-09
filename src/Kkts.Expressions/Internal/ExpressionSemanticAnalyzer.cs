using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kkts.Expressions.Internal
{
    internal sealed class ExpressionSemanticAnalyzer
    {
        private readonly string _source;
        private readonly ExpressionSchema _schema;
        private readonly ExpressionVariableSchema _variables;
        private readonly ExpressionAnalysisResult _syntax;
        private readonly List<ExpressionDiagnostic> _diagnostics = new List<ExpressionDiagnostic>();
        private readonly HashSet<Tuple<string, int, int>> _reported = new HashSet<Tuple<string, int, int>>();

        internal ExpressionSemanticAnalyzer(
            string source,
            ExpressionSchema schema,
            ExpressionVariableSchema variables,
            ExpressionAnalysisResult syntax)
        {
            _source = source;
            _schema = schema;
            _variables = variables ?? new ExpressionVariableSchema(null);
            _syntax = syntax;
        }

        internal ExpressionSemanticAnalysisResult Analyze()
        {
            if (_syntax.Tokens.Count == 0)
                return new ExpressionSemanticAnalysisResult(_syntax, _diagnostics, false);

            var tokens = _syntax.Tokens;
            var semanticRoot = false;
            if (_syntax.IsComplete)
            {
                var parser = new Parser(this, tokens, 0, tokens.Count);
                var expression = parser.Parse();
                if (expression != null && parser.AtEnd)
                    semanticRoot = CheckRoot(expression);
            }
            else
            {
                foreach (var range in GetRecoverableRanges(tokens))
                {
                    if (HasSyntaxErrorInRange(tokens, range.Start, range.End)) continue;
                    var parser = new Parser(this, tokens, range.Start, range.End);
                    var expression = parser.Parse();
                    if (expression == null || !parser.AtEnd) continue;
                    CheckRoot(expression, reportRoot: false);
                }
            }

            return new ExpressionSemanticAnalysisResult(
                _syntax,
                _diagnostics,
                _syntax.IsComplete && semanticRoot && _diagnostics.Count == 0);
        }

        private bool HasSyntaxErrorInRange(IReadOnlyList<ExpressionToken> tokens, int start, int end)
        {
            var sourceStart = tokens[start].Start;
            var sourceEnd = tokens[end - 1].Start + tokens[end - 1].Length;
            return _syntax.Diagnostics.Any(diagnostic =>
            {
                if (diagnostic.Code == "unmatched-delimiter" && diagnostic.Start == _source.Length)
                    return false;
                return diagnostic.Length == 0
                    ? diagnostic.Start >= sourceStart && diagnostic.Start <= sourceEnd
                    : diagnostic.Start < sourceEnd && diagnostic.Start + diagnostic.Length > sourceStart;
            });
        }

        private bool CheckRoot(SemanticNode node, bool reportRoot = true)
        {
            if (node.Invalid) return false;
            if (node.Type == typeof(bool)) return true;
            if (reportRoot)
            {
                Report(
                    "predicate-result-not-boolean",
                    "A predicate must have a Boolean result.",
                    node.Start,
                    Math.Max(0, node.End - node.Start),
                    new[] { ExpressionTypeInfo.ForClr(typeof(bool), ExpressionNullability.NonNullable) },
                    new[] { TypeInfo(node) });
            }
            return false;
        }

        private IEnumerable<(int Start, int End)> GetRecoverableRanges(IReadOnlyList<ExpressionToken> tokens)
        {
            var depth = 0;
            var start = 0;
            var recoverInsideGroups = _syntax.Diagnostics.Any(diagnostic =>
                diagnostic.Code == "unmatched-delimiter");
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                var text = Text(token);
                if (token.Kind == ExpressionTokenKind.Punctuation)
                {
                    if (text == "(" || text == "[" || text == "{") ++depth;
                    else if (text == ")" || text == "]" || text == "}") depth = Math.Max(0, depth - 1);
                }

                if ((depth == 0 || recoverInsideGroups) &&
                    token.Kind == ExpressionTokenKind.Operator &&
                    IsLogical(text))
                {
                    if (TryTrimUnmatchedParentheses(tokens, start, index, out var range))
                        yield return range;
                    start = index + 1;
                }
            }
            if (TryTrimUnmatchedParentheses(tokens, start, tokens.Count, out var finalRange))
                yield return finalRange;
        }

        private bool TryTrimUnmatchedParentheses(
            IReadOnlyList<ExpressionToken> tokens,
            int start,
            int end,
            out (int Start, int End) range)
        {
            var unmatchedOpen = new HashSet<int>();
            var unmatchedClose = new HashSet<int>();
            var groups = new Stack<int>();
            for (var index = start; index < end; index++)
            {
                if (tokens[index].Kind != ExpressionTokenKind.Punctuation) continue;
                var text = Text(tokens[index]);
                if (text == "(") groups.Push(index);
                else if (text == ")")
                {
                    if (groups.Count == 0) unmatchedClose.Add(index);
                    else groups.Pop();
                }
            }
            while (groups.Count > 0) unmatchedOpen.Add(groups.Pop());

            while (start < end && unmatchedOpen.Contains(start)) start++;
            while (end > start && unmatchedClose.Contains(end - 1)) end--;
            range = (start, end);
            return start < end;
        }

        private SemanticNode Bind(ExpressionToken token)
        {
            var text = Text(token);
            if (token.Kind == ExpressionTokenKind.Constant)
                return ReadLiteral(token, text);

            if (token.Kind == ExpressionTokenKind.Variable)
            {
                var name = text.Substring(1);
                if (_variables.TryGetVariable(name, out var variableType, out var nullable, out var elementType, out var rootDeclared))
                    return SemanticNode.Value(variableType, nullable, token.Start, token.Start + token.Length, elementType);
                Report(
                    rootDeclared ? "unknown-variable-member" : "undeclared-variable",
                    rootDeclared
                        ? $"Variable member '{text}' is not declared."
                        : $"Variable '{text}' is not declared.",
                    token.Start,
                    token.Length);
                return SemanticNode.Error(token.Start, token.Start + token.Length);
            }

            if (token.Kind != ExpressionTokenKind.Property)
                return SemanticNode.Error(token.Start, token.Start + token.Length);

            if (_schema.TryMapProperty(text, out var clrPath) &&
                _schema.TryGetProperty(clrPath, out var type, out var nullability, out _))
            {
                if (!_schema.IsPropertyQueryable(text))
                {
                    Report(
                        "property-not-queryable",
                        $"Property '{text}' is not permitted for querying.",
                        token.Start,
                        token.Length);
                    return SemanticNode.Error(token.Start, token.Start + token.Length);
                }
                return SemanticNode.Value(type, nullability, token.Start, token.Start + token.Length);
            }

            if (_variables.TryGetVariable(text, out var fallbackType, out var fallbackNullability, out var fallbackElement, out _))
                return SemanticNode.Value(fallbackType, fallbackNullability, token.Start, token.Start + token.Length, fallbackElement);

            var suggestion = FindPropertySuggestion(text, token);
            Report(
                "unknown-property",
                $"Property '{text}' does not exist.",
                token.Start,
                token.Length,
                suggestions: suggestion == null ? null : new[] { suggestion });
            return SemanticNode.Error(token.Start, token.Start + token.Length);
        }

        private SemanticNode ReadLiteral(ExpressionToken token, string text)
        {
            var start = token.Start;
            var end = start + token.Length;
            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                return SemanticNode.NullValue(start, end);
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                return SemanticNode.Literal(typeof(bool), start, end, bool.Parse(text));
            if (text.Length > 1 && ExpressionGrammar.IsQuote(text[0]))
                return SemanticNode.Literal(typeof(string), start, end, Unquote(text));

            try
            {
                var expression = NumericOperands.ParseLiteral(text);
                return SemanticNode.Literal(expression.Type, start, end, expression.Value);
            }
            catch (FormatException)
            {
                return SemanticNode.Error(start, end);
            }
        }

        private void CheckUnary(string op, SemanticNode operand, int start, int end)
        {
            if (operand.Invalid) return;
            if (op == "!" || string.Equals(op, "not", StringComparison.OrdinalIgnoreCase))
            {
                if (operand.Type != typeof(bool))
                    ReportOperator(start, end - start, typeof(bool), operand.Type);
                return;
            }
            if (operand.Type != typeof(bool))
                ReportOperator(start, end - start, typeof(bool), operand.Type);
        }

        private SemanticNode CheckBinary(string op, SemanticNode left, SemanticNode right, int start, int end)
        {
            var nodeStart = Math.Min(left.Start, start);
            var nodeEnd = Math.Max(right.End, end);
            if (left.Invalid || right.Invalid)
                return SemanticNode.Error(nodeStart, nodeEnd);

            op = ExpressionGrammar.NormalizeOperator(op);
            if (IsLogical(op))
            {
                if (left.Type != typeof(bool))
                    ReportOperator(start, end - start, typeof(bool), left.Type);
                if (right.Type != typeof(bool))
                    ReportOperator(start, end - start, typeof(bool), right.Type);
                return left.Type == typeof(bool) && right.Type == typeof(bool)
                    ? SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd)
                    : SemanticNode.Error(nodeStart, nodeEnd);
            }

            if (op == "+" || op == "-")
            {
                if (op == "+" && (left.Type == typeof(string) || right.Type == typeof(string)))
                    return SemanticNode.Value(typeof(string), ExpressionNullability.Unknown, nodeStart, nodeEnd);
                if (NumericOperands.IsNumeric(left.Type) && NumericOperands.IsNumeric(right.Type))
                {
                    try
                    {
                        var leftType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
                        var rightType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;
                        if (CanConvertNumericLiteral(left, rightType)) leftType = rightType;
                        if (CanConvertNumericLiteral(right, leftType)) rightType = leftType;
                        var type = NumericOperands.Promote(
                            leftType,
                            rightType);
                        type = NumericOperands.LiftNullable(type, left.Type, right.Type);
                        return SemanticNode.Value(type, Nullable.GetUnderlyingType(type) == null
                            ? ExpressionNullability.NonNullable : ExpressionNullability.Nullable, nodeStart, nodeEnd);
                    }
                    catch (InvalidOperationException)
                    {
                        ReportOperator(start, end - start, left.Type, right.Type);
                        return SemanticNode.Error(nodeStart, nodeEnd);
                    }
                }
                ReportOperator(start, end - start, left.Type, right.Type);
                return SemanticNode.Error(nodeStart, nodeEnd);
            }

            if (IsStringFunction(op))
            {
                if (left.Type != typeof(string))
                {
                    ReportOperator(start, end - start, typeof(string), left.Type);
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                if (right.Type != typeof(string))
                {
                    ReportOperand(right, typeof(string));
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                return SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd);
            }

            if (IsMembership(op))
            {
                var targetType = left.Type;
                if (right.IsList && right.ElementNodes != null)
                {
                    foreach (var item in right.ElementNodes)
                        if (!item.Invalid && !CanConvertLiteral(item, targetType))
                            ReportOperand(item, targetType);
                    return right.ElementNodes.Any(item => item.Invalid || !CanConvertLiteral(item, targetType))
                        ? SemanticNode.Error(nodeStart, nodeEnd)
                        : SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd);
                }
                if (right.ElementType == null)
                {
                    ReportOperator(start, end - start, typeof(IEnumerable<>).MakeGenericType(targetType), right.Type);
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                if (right.ElementType != left.Type)
                {
                    ReportOperator(start, end - start, typeof(IEnumerable<>).MakeGenericType(left.Type), right.Type);
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                return SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd);
            }

            if (IsComparison(op))
            {
                if (left.IsNull || right.IsNull)
                {
                    var valueType = left.IsNull ? right.Type : left.Type;
                    var comparisonType = valueType.IsValueType && Nullable.GetUnderlyingType(valueType) == null
                        ? typeof(Nullable<>).MakeGenericType(valueType)
                        : valueType;
                    return ComparisonResult(op, comparisonType, comparisonType, nodeStart, nodeEnd, start, end);
                }
                if (right.IsLiteral && CanConvertLiteral(right, left.Type))
                    return ComparisonResult(op, left.Type, left.Type, nodeStart, nodeEnd, start, end);
                if (left.IsLiteral && CanConvertLiteral(left, right.Type))
                    return ComparisonResult(op, right.Type, right.Type, nodeStart, nodeEnd, start, end);
                if (right.IsLiteral)
                {
                    if (IsContextDependent(right, left.Type))
                    {
                        Report(
                            "context-dependent-conversion",
                            "Use an explicit date and time-zone offset so the conversion does not depend on machine defaults.",
                            right.Start,
                            right.End - right.Start,
                            new[] { ExpressionTypeInfo.ForClr(left.Type) },
                            new[] { ExpressionTypeInfo.ForClr(typeof(string), ExpressionNullability.NonNullable) });
                        return SemanticNode.Error(nodeStart, nodeEnd);
                    }
                    ReportOperand(right, left.Type);
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                if (left.IsLiteral)
                {
                    if (IsContextDependent(left, right.Type))
                    {
                        Report(
                            "context-dependent-conversion",
                            "Use an explicit date and time-zone offset so the conversion does not depend on machine defaults.",
                            left.Start,
                            left.End - left.Start,
                            new[] { ExpressionTypeInfo.ForClr(right.Type) },
                            new[] { ExpressionTypeInfo.ForClr(typeof(string), ExpressionNullability.NonNullable) });
                        return SemanticNode.Error(nodeStart, nodeEnd);
                    }
                    ReportOperand(left, right.Type);
                    return SemanticNode.Error(nodeStart, nodeEnd);
                }
                if (NumericOperands.IsNumeric(left.Type) && NumericOperands.IsNumeric(right.Type))
                {
                    try
                    {
                        var promotedType = NumericOperands.Promote(
                            Nullable.GetUnderlyingType(left.Type) ?? left.Type,
                            Nullable.GetUnderlyingType(right.Type) ?? right.Type);
                        promotedType = NumericOperands.LiftNullable(
                            promotedType,
                            left.Type,
                            right.Type);
                        return ComparisonResult(op, promotedType, promotedType, nodeStart, nodeEnd, start, end);
                    }
                    catch (InvalidOperationException)
                    {
                        ReportOperator(start, end - start, left.Type, right.Type);
                        return SemanticNode.Error(nodeStart, nodeEnd);
                    }
                }
                if (ExpressionOperatorRules.CanApplyComparison(op, left.Type, right.Type))
                    return SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd);

                ReportOperator(start, end - start, left.Type, right.Type);
                return SemanticNode.Error(nodeStart, nodeEnd);
            }

            return SemanticNode.Error(nodeStart, nodeEnd);
        }

        private SemanticNode ComparisonResult(
            string op,
            Type leftType,
            Type rightType,
            int nodeStart,
            int nodeEnd,
            int operatorStart,
            int operatorEnd)
        {
            if (ExpressionOperatorRules.CanApplyComparison(op, leftType, rightType))
                return SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, nodeStart, nodeEnd);

            ReportOperator(operatorStart, operatorEnd - operatorStart, leftType, rightType);
            return SemanticNode.Error(nodeStart, nodeEnd);
        }

        private bool CanConvertLiteral(SemanticNode value, Type targetType)
        {
            if (value.Invalid) return false;
            if (value.IsNull) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
            var nullableTarget = Nullable.GetUnderlyingType(targetType) != null;
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value.Type == targetType || targetType == typeof(string)) return true;
            if (CanConvertNumericLiteral(value, targetType)) return true;
            if (value.LiteralValue is string text)
            {
                if (targetType.IsEnum)
                {
                    try
                    {
                        Enum.Parse(targetType, text, true);
                        return true;
                    }
                    catch (ArgumentException) { return false; }
                    catch (OverflowException) { return false; }
                }
                if (nullableTarget && string.IsNullOrWhiteSpace(text)) return true;
                if (targetType == typeof(Guid)) return Guid.TryParse(text, out _);
                if (targetType == typeof(DateTime))
                {
                    if (RequiresDateDefaults(text)) return false;
                    return DateTime.TryParse(text, _schema.ConversionContext.Culture, DateTimeStyles.None, out _) ||
                        DateTime.TryParseExact(text, _schema.ConversionContext.DateTimeFormats.ToArray(),
                            _schema.ConversionContext.Culture, DateTimeStyles.None, out _);
                }
                if (targetType == typeof(DateTimeOffset))
                {
                    if (!HasExplicitOffset(text)) return false;
                    return DateTimeOffset.TryParse(text, _schema.ConversionContext.Culture, DateTimeStyles.None, out _) ||
                        DateTimeOffset.TryParseExact(text, _schema.ConversionContext.DateTimeFormats.ToArray(),
                            _schema.ConversionContext.Culture, DateTimeStyles.None, out _);
                }
                if (targetType == typeof(TimeSpan)) return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out _);
                if (targetType == typeof(bool)) return bool.TryParse(text, out _);
                if (NumericOperands.IsNumeric(targetType))
                {
                    try
                    {
                        Convert.ChangeType(text, targetType, _schema.ConversionContext.Culture);
                        return true;
                    }
                    catch (FormatException) { return false; }
                    catch (OverflowException) { return false; }
                    catch (InvalidCastException) { return false; }
                }
            }
            if (NumericOperands.IsNumeric(value.Type) && NumericOperands.IsNumeric(targetType))
            {
                try
                {
                    Convert.ChangeType(value.LiteralValue, targetType, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (FormatException) { return false; }
                catch (OverflowException) { return false; }
                catch (InvalidCastException) { return false; }
            }
            return false;
        }

        private static bool CanConvertNumericLiteral(SemanticNode value, Type targetType)
        {
            if (!value.IsLiteral || value.LiteralValue == null ||
                (value.Type != typeof(int) && value.Type != typeof(long)))
                return false;

            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (targetType != typeof(uint) && targetType != typeof(ulong)) return false;
            var numericValue = Convert.ToDecimal(value.LiteralValue, CultureInfo.InvariantCulture);
            return NumericOperands.CanConvertIntegralConstant(value.Type, numericValue, targetType);
        }

        private static bool IsContextDependent(SemanticNode value, Type targetType)
        {
            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (!(value.LiteralValue is string text)) return false;
            if (targetType == typeof(DateTime)) return RequiresDateDefaults(text);
            if (targetType == typeof(DateTimeOffset)) return !HasExplicitOffset(text);
            return false;
        }

        private void ReportOperand(SemanticNode value, Type expectedType)
        {
            var actual = value.IsNull
                ? ExpressionTypeInfo.Null()
                : ExpressionTypeInfo.ForClr(value.Type, value.Nullability);
            Report(
                "incompatible-operand",
                $"The operand is not compatible with '{FriendlyName(expectedType)}'.",
                value.Start,
                value.End - value.Start,
                new[] { ExpressionTypeInfo.ForClr(expectedType) },
                new[] { actual });
        }

        private void ReportOperator(int start, int length, Type expectedType, Type actualType)
        {
            ReportOperator(start, length, new[] { expectedType }, new[] { actualType });
        }

        private void ReportOperator(int start, int length, Type[] expectedTypes, Type[] actualTypes)
        {
            Report(
                "operator-not-applicable",
                $"The operator is not applicable to the supplied operand types.",
                start,
                length,
                expectedTypes.Select(type => ExpressionTypeInfo.ForClr(type)),
                actualTypes.Select(type => ExpressionTypeInfo.ForClr(type)));
        }

        private void Report(
            string code,
            string message,
            int start,
            int length,
            IEnumerable<ExpressionTypeInfo> expectedTypes = null,
            IEnumerable<ExpressionTypeInfo> actualTypes = null,
            IEnumerable<ExpressionCorrectionSuggestion> suggestions = null)
        {
            if (_reported.Add(Tuple.Create(code, start, length)))
                _diagnostics.Add(new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    code,
                    message,
                    start,
                    length,
                    expectedTypes,
                    actualTypes,
                    suggestions));
        }

        private ExpressionCorrectionSuggestion FindPropertySuggestion(string sourceName, ExpressionToken token)
        {
            var normalized = ExpressionSchema.NormalizePath(sourceName);
            var separator = normalized.LastIndexOf('.');
            var sourceParent = separator < 0 ? string.Empty : normalized.Substring(0, separator);
            var terminal = separator < 0 ? normalized : normalized.Substring(separator + 1);
            _schema.TryMapProperty(sourceParent, out var clrParent);
            var parentType = _schema.EntityType;
            foreach (var segment in clrParent.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!PropertyMetadata.GetMemberTypes(parentType).TryGetValue(segment, out parentType))
                    return null;
            }

            var allowedCandidates = _schema.ValidProperties.Count == 0
                ? PropertyMetadata.GetMemberTypes(parentType).Keys
                    .Select(candidate => string.IsNullOrEmpty(sourceParent) ? candidate : sourceParent + "." + candidate)
                : _schema.ValidProperties.Where(candidate =>
                    string.Equals(
                        candidate.Substring(0, Math.Max(0, candidate.LastIndexOf('.'))),
                        sourceParent,
                        StringComparison.OrdinalIgnoreCase));
            var candidates = allowedCandidates
                .Where(candidate =>
                    string.Equals(
                        candidate.Substring(0, Math.Max(0, candidate.LastIndexOf('.'))),
                        sourceParent,
                        StringComparison.OrdinalIgnoreCase) &&
                    _schema.TryMapProperty(candidate, out var candidatePath) &&
                    _schema.TryGetProperty(candidatePath, out _, out _, out _) &&
                    _schema.IsPropertyQueryable(candidate) &&
                    EditDistanceAtMostOne(
                        terminal,
                        candidate.Substring(candidate.LastIndexOf('.') + 1)))
                .ToArray();
            if (candidates.Length != 1) return null;
            return new ExpressionCorrectionSuggestion(
                $"Replace '{sourceName}' with '{candidates[0]}'.",
                candidates[0],
                token.Start,
                token.Length);
        }

        private static bool EditDistanceAtMostOne(string left, string right)
        {
            if (left.Length < 2 || right.Length < 2 || Math.Abs(left.Length - right.Length) > 1) return false;
            left = left.ToUpperInvariant();
            right = right.ToUpperInvariant();
            var leftIndex = 0;
            var rightIndex = 0;
            var edits = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (left[leftIndex] == right[rightIndex])
                {
                    leftIndex++;
                    rightIndex++;
                    continue;
                }
                if (++edits > 1) return false;
                if (left.Length > right.Length) leftIndex++;
                else if (right.Length > left.Length) rightIndex++;
                else
                {
                    leftIndex++;
                    rightIndex++;
                }
            }
            if (leftIndex < left.Length || rightIndex < right.Length) edits++;
            return edits <= 1;
        }

        private string Text(ExpressionToken token) => _source.Substring(token.Start, token.Length);

        private static string Unquote(string text)
        {
            var quote = text[0];
            var value = new StringBuilder();
            for (var index = 1; index < text.Length - 1; index++)
            {
                if (text[index] == ExpressionGrammar.Escape &&
                    index + 1 < text.Length - 1 &&
                    text[index + 1] == quote)
                    index++;
                value.Append(text[index]);
            }
            return value.ToString();
        }

        private static bool RequiresDateDefaults(string value)
        {
            if (value.IndexOfAny(new[] { '-', '/' }) < 0) return true;
            var parts = value.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && !parts.Any(part => part.Length == 4);
        }

        private static bool HasExplicitOffset(string value)
        {
            if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
            var separator = value.IndexOf('T');
            if (separator < 0) separator = value.IndexOf(' ');
            return separator >= 0 && (value.IndexOf('+', separator) >= 0 || value.IndexOf('-', separator + 1) >= 0);
        }

        private static bool IsLogical(string op) =>
            op.Equals("and", StringComparison.OrdinalIgnoreCase) ||
            op == "&&" || op == "&" ||
            op.Equals("or", StringComparison.OrdinalIgnoreCase) ||
            op == "||" || op == "|";

        private static bool IsStringFunction(string op) =>
            op.Equals("contains", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("contain", StringComparison.OrdinalIgnoreCase) ||
            op == "@" ||
            op.Equals("startswith", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("startwith", StringComparison.OrdinalIgnoreCase) ||
            op == "@*" ||
            op.Equals("endswith", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("endwith", StringComparison.OrdinalIgnoreCase) ||
            op == "*@";

        private static bool IsMembership(string op) =>
            op.Equals("in", StringComparison.OrdinalIgnoreCase) ||
            op.Equals("not in", StringComparison.OrdinalIgnoreCase);

        private static bool IsComparison(string op) =>
            op == "=" || op == "==" || op == "!=" || op == "<>" ||
            op == "<" || op == "<=" || op == ">" || op == ">=";

        private static string FriendlyName(Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null) return underlyingType.Name + "?";
            return type == typeof(bool) ? "Boolean" : type.Name;
        }

        private static ExpressionTypeInfo TypeInfo(SemanticNode node) =>
            node.IsNull ? ExpressionTypeInfo.Null() :
            ExpressionTypeInfo.ForClr(node.Type, node.Nullability, node.ElementType);

        private sealed class SemanticNode
        {
            private SemanticNode(
                Type type,
                ExpressionNullability nullability,
                int start,
                int end,
                bool isNull,
                bool isLiteral,
                bool isList,
                bool invalid,
                object literalValue,
                Type elementType,
                IReadOnlyList<SemanticNode> elements)
            {
                Type = type;
                Nullability = nullability;
                Start = start;
                End = end;
                IsNull = isNull;
                IsLiteral = isLiteral;
                IsList = isList;
                Invalid = invalid;
                LiteralValue = literalValue;
                ElementType = elementType;
                ElementNodes = elements;
            }

            internal Type Type { get; }
            internal ExpressionNullability Nullability { get; }
            internal int Start { get; }
            internal int End { get; }
            internal bool IsNull { get; }
            internal bool IsLiteral { get; }
            internal bool IsList { get; }
            internal bool Invalid { get; }
            internal object LiteralValue { get; }
            internal Type ElementType { get; }
            internal IReadOnlyList<SemanticNode> ElementNodes { get; }

            internal static SemanticNode Value(Type type, ExpressionNullability nullability, int start, int end, Type elementType = null) =>
                new SemanticNode(type, nullability, start, end, false, false, false, type == null, null, elementType, null);
            internal static SemanticNode Literal(Type type, int start, int end, object value) =>
                new SemanticNode(type, ExpressionNullability.NonNullable, start, end, false, true, false, false, value, null, null);
            internal static SemanticNode NullValue(int start, int end) =>
                new SemanticNode(null, ExpressionNullability.Nullable, start, end, true, true, false, false, null, null, null);
            internal static SemanticNode List(int start, int end, IReadOnlyList<SemanticNode> elements, Type elementType) =>
                new SemanticNode(typeof(Array), ExpressionNullability.NonNullable, start, end, false, false, true, false, null, elementType, elements);
            internal static SemanticNode Error(int start, int end) =>
                new SemanticNode(null, ExpressionNullability.Unknown, start, end, false, false, false, true, null, null, null);
        }

        private sealed class Parser
        {
            private readonly ExpressionSemanticAnalyzer _owner;
            private readonly IReadOnlyList<ExpressionToken> _tokens;
            private readonly int _end;
            private int _index;

            internal Parser(ExpressionSemanticAnalyzer owner, IReadOnlyList<ExpressionToken> tokens, int start, int end)
            {
                _owner = owner;
                _tokens = tokens;
                _index = start;
                _end = end;
            }

            internal bool AtEnd => _index == _end;

            internal SemanticNode Parse() => ParseExpression(0);

            private SemanticNode ParseExpression(int minimumPrecedence)
            {
                var left = ParsePrefix();
                if (left == null) return null;
                while (_index < _end)
                {
                    if (IsPunctuation(".") && _index + 2 < _end &&
                        _tokens[_index + 1].Kind == ExpressionTokenKind.Operator &&
                        IsPunctuationAt(_index + 2, "("))
                    {
                        var opToken = _tokens[_index + 1];
                        var op = _owner.Text(opToken);
                        var precedence = 3;
                        if (precedence < minimumPrecedence) break;
                        _index += 3;
                        var right = ParseExpression(0);
                        if (right == null) return SemanticNode.Error(left.Start, opToken.Start + opToken.Length);
                        if (IsPunctuation(")")) ++_index;
                        left = _owner.CheckBinary(op, left, right, opToken.Start, opToken.Start + opToken.Length);
                        continue;
                    }

                    if (_tokens[_index].Kind != ExpressionTokenKind.Operator) break;
                    var token = _tokens[_index];
                    var text = _owner.Text(token);
                    var binding = GetBinding(text);
                    if (binding.Precedence < minimumPrecedence) break;
                    ++_index;
                    if (IsMembership(text) && _index < _end && IsPunctuation("[") || IsMembership(text) && _index < _end && IsPunctuation("{") ||
                        IsMembership(text) && _index < _end && IsPunctuation("("))
                    {
                        var list = ParseList();
                        left = _owner.CheckBinary(text, left, list, token.Start, token.Start + token.Length);
                        continue;
                    }
                    var rightMinimum = binding.RightAssociative ? binding.Precedence : binding.Precedence + 1;
                    var rightOperand = ParseExpression(rightMinimum);
                    if (rightOperand == null) return SemanticNode.Error(left.Start, token.Start + token.Length);
                    left = _owner.CheckBinary(text, left, rightOperand, token.Start, token.Start + token.Length);
                }
                return left;
            }

            private SemanticNode ParsePrefix()
            {
                if (_index >= _end) return null;
                var token = _tokens[_index];
                var text = _owner.Text(token);
                if (token.Kind == ExpressionTokenKind.Operator &&
                    (text == "!" || text.Equals("not", StringComparison.OrdinalIgnoreCase)))
                {
                    ++_index;
                    SemanticNode operand;
                    var end = token.Start + token.Length;
                    if (IsPunctuation("("))
                    {
                        ++_index;
                        operand = ParseExpression(0);
                        if (IsPunctuation(")"))
                        {
                            end = _tokens[_index].Start + _tokens[_index].Length;
                            ++_index;
                        }
                    }
                    else
                    {
                        operand = ParseExpression(5);
                    }
                    if (operand == null) return SemanticNode.Error(token.Start, end);
                    _owner.CheckUnary(text, operand, token.Start, token.Start + token.Length);
                    return operand.Type == typeof(bool)
                        ? SemanticNode.Value(typeof(bool), ExpressionNullability.NonNullable, token.Start, Math.Max(end, operand.End))
                        : SemanticNode.Error(token.Start, Math.Max(end, operand.End));
                }

                if (IsPunctuation("("))
                {
                    ++_index;
                    var grouped = ParseExpression(0);
                    if (IsPunctuation(")")) ++_index;
                    return grouped;
                }

                if (token.Kind == ExpressionTokenKind.Punctuation || token.Kind == ExpressionTokenKind.Unknown)
                    return null;
                ++_index;
                return _owner.Bind(token);
            }

            private SemanticNode ParseList()
            {
                var open = _tokens[_index++];
                var openText = _owner.Text(open);
                var closing = openText == "[" ? "]" : openText == "{" ? "}" : ")";
                var elements = new List<SemanticNode>();
                while (_index < _end && !IsPunctuation(closing))
                {
                    if (IsPunctuation(",")) { ++_index; continue; }
                    var element = ParsePrefix();
                    if (element == null) break;
                    elements.Add(element);
                    if (IsPunctuation(",")) ++_index;
                    else if (!IsPunctuation(closing)) break;
                }
                var end = open.Start + open.Length;
                if (IsPunctuation(closing))
                {
                    end = _tokens[_index].Start + _tokens[_index].Length;
                    ++_index;
                }
                var elementType = elements.FirstOrDefault(element => !element.IsNull && !element.Invalid)?.Type;
                return SemanticNode.List(open.Start, end, elements, elementType);
            }

            private bool IsPunctuation(string value) => IsPunctuationAt(_index, value);

            private bool IsPunctuationAt(int index, string value) =>
                index < _end &&
                _tokens[index].Kind == ExpressionTokenKind.Punctuation &&
                _owner.Text(_tokens[index]) == value;

            private static (int Precedence, bool RightAssociative) GetBinding(string op) =>
                (ExpressionGrammar.GetBinaryPrecedence(op), false);
        }
    }
}
