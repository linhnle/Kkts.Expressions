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
        private readonly ExpressionQueryContext _queryContext;
        private readonly QueryPolicyExecution _policyExecution;
        private readonly bool _syntaxTruncated;
        private readonly List<ExpressionDiagnostic> _diagnostics = new List<ExpressionDiagnostic>();
        private readonly HashSet<Tuple<string, int, int>> _reported = new HashSet<Tuple<string, int, int>>();
        private bool _isTruncated;

        internal ExpressionSemanticAnalyzer(
            string source,
            ExpressionSchema schema,
            ExpressionVariableSchema variables,
            ExpressionAnalysisResult syntax,
            ExpressionQueryContext queryContext = null,
            QueryPolicyExecution policyExecution = null,
            bool syntaxTruncated = false)
        {
            _source = source;
            _schema = schema;
            _variables = variables ?? new ExpressionVariableSchema(null);
            _syntax = syntax;
            _queryContext = queryContext;
            _policyExecution = policyExecution;
            _syntaxTruncated = syntaxTruncated;
        }

        internal ExpressionSemanticAnalysisResult Analyze()
        {
            if (_syntax.Tokens.Count == 0)
                return new ExpressionSemanticAnalysisResult(
                    _syntax,
                    AllDiagnostics(),
                    false,
                    isTruncated: IsTruncated(),
                    capDiagnostics: _queryContext != null,
                    atomicConditionCount: _policyExecution?.ConditionCount.Value ?? 0);

            try
            {
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
                    AllDiagnostics(),
                    _syntax.IsComplete && semanticRoot && _diagnostics.Count == 0 &&
                    (_policyExecution == null || _policyExecution.Diagnostics.ToReadOnlyList().Count == 0),
                    isTruncated: IsTruncated(),
                    capDiagnostics: _queryContext != null,
                    atomicConditionCount: _policyExecution?.ConditionCount.Value ?? 0);
            }
            catch (PolicyAnalysisStoppedException)
            {
                return new ExpressionSemanticAnalysisResult(
                    _syntax,
                    AllDiagnostics(),
                    false,
                    isTruncated: true,
                    capDiagnostics: _queryContext != null,
                    atomicConditionCount: _policyExecution?.ConditionCount.Value ?? 0);
            }
        }

        private bool IsTruncated()
        {
            return _isTruncated || _syntaxTruncated ||
                (_policyExecution != null && _policyExecution.Diagnostics.IsTruncated);
        }

        private IEnumerable<ExpressionDiagnostic> AllDiagnostics()
        {
            return _policyExecution == null
                ? _diagnostics
                : _diagnostics.Concat(_policyExecution.Diagnostics.ToReadOnlyList());
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
            if (node.Type == typeof(bool))
            {
                if (node.IsBareBooleanPredicate) CheckBareBooleanPredicate(node);
                return true;
            }
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
                if (!(_queryContext?.IsPropertyQueryable(text) ?? _schema.IsPropertyQueryable(text)))
                {
                    Report(
                        "property-not-queryable",
                        $"Property '{text}' is not permitted for querying.",
                        token.Start,
                        token.Length);
                    return SemanticNode.Error(token.Start, token.Start + token.Length);
                }
                if (_queryContext != null)
                {
                    if (!_queryContext.IsNavigationAllowed(text, out var navigationDepth))
                    {
                        _policyExecution.Diagnostics.Add(
                            "query-policy-navigation-depth-exceeded",
                            "The field exceeds the configured entity navigation depth.",
                            token.Start,
                            token.Length,
                            _queryContext.Policy.MaxNavigationDepth,
                            navigationDepth);
                    }
                    if (!_queryContext.IsCollectionAccessAllowed(text))
                    {
                        _policyExecution.Diagnostics.Add(
                            "query-policy-collection-access-denied",
                            "Traversal through entity collection fields is not permitted.",
                            token.Start,
                            token.Length);
                    }
                }
                return SemanticNode.Value(
                    type,
                    nullability,
                    token.Start,
                    token.Start + token.Length,
                    entityPaths: new[] { text },
                    isBareBooleanPredicate: type == typeof(bool),
                    predicateStart: token.Start,
                    predicateLength: token.Length);
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
            op = ExpressionGrammar.NormalizeOperator(op);
            if (_queryContext != null &&
                (IsComparison(op) || IsStringFunction(op) || IsMembership(op)))
                CheckAllowedOperator(op, left.EntityPaths.Concat(right.EntityPaths), start, end - start);

            if (left.Invalid || right.Invalid)
                return SemanticNode.Error(nodeStart, nodeEnd);

            if (IsLogical(op))
            {
                if (left.IsBareBooleanPredicate) CheckBareBooleanPredicate(left);
                if (right.IsBareBooleanPredicate) CheckBareBooleanPredicate(right);
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
                    return SemanticNode.Value(
                        typeof(string),
                        ExpressionNullability.Unknown,
                        nodeStart,
                        nodeEnd,
                        entityPaths: MergeEntityPaths(left, right));
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
                        return SemanticNode.Value(
                            type,
                            Nullable.GetUnderlyingType(type) == null
                                ? ExpressionNullability.NonNullable : ExpressionNullability.Nullable,
                            nodeStart,
                            nodeEnd,
                            entityPaths: MergeEntityPaths(left, right));
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

        private void CheckAllowedOperator(
            string op,
            IEnumerable<string> sourcePaths,
            int start,
            int length)
        {
            var comparisonOperator = QueryPolicyFieldMetadata.NormalizeComparisonOperator(op);
            if (_queryContext.AreOperatorsAllowed(sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase), comparisonOperator))
                return;

            _policyExecution.Diagnostics.Add(
                "query-policy-operator-denied",
                "The comparison operator is not permitted for one or more fields in this predicate.",
                start,
                length);
        }

        private void CheckBareBooleanPredicate(SemanticNode node)
        {
            if (_policyExecution == null) return;

            if (_queryContext != null &&
                node.EntityPaths.Count > 0 &&
                !_queryContext.AreOperatorsAllowed(node.EntityPaths, ComparisonOperator.Equal))
            {
                _policyExecution.Diagnostics.Add(
                    "query-policy-operator-denied",
                    "The comparison operator is not permitted for one or more fields in this predicate.",
                    node.PredicateStart,
                    node.PredicateLength);
            }

            if (!_policyExecution.TryCountCondition(node.PredicateStart, node.PredicateLength))
                throw new PolicyAnalysisStoppedException();
        }

        private sealed class PolicyAnalysisStoppedException : Exception { }

        private static IReadOnlyList<string> MergeEntityPaths(SemanticNode left, SemanticNode right)
        {
            return left.EntityPaths.Concat(right.EntityPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private bool CanConvertLiteral(SemanticNode value, Type targetType)
        {
            return !value.Invalid && ExpressionConversionRules.CanConvertLiteral(
                value.LiteralValue,
                value.Type,
                value.IsNull,
                targetType,
                _schema.ConversionContext);
        }

        private static bool CanConvertNumericLiteral(SemanticNode value, Type targetType) =>
            value.IsLiteral && ExpressionConversionRules.CanConvertNumericLiteral(
                value.LiteralValue,
                value.Type,
                targetType);

        private static bool IsContextDependent(SemanticNode value, Type targetType)
        {
            return ExpressionConversionRules.IsContextDependent(value.LiteralValue, targetType);
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
            if (_queryContext != null && _isTruncated) return;
            if (_reported.Add(Tuple.Create(code, start, length)))
            {
                if (_queryContext != null && _diagnostics.Count >= 32)
                {
                    _diagnostics.RemoveAt(31);
                    _diagnostics.Add(new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        "query-policy-diagnostics-truncated",
                        "Additional diagnostics were omitted; fix the reported issues before retrying.",
                        start,
                        0));
                    _isTruncated = true;
                    return;
                }

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
        }

        private ExpressionCorrectionSuggestion FindPropertySuggestion(string sourceName, ExpressionToken token)
        {
            if (_queryContext != null) return null;

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
                IReadOnlyList<SemanticNode> elements,
                IReadOnlyList<string> entityPaths,
                bool isBareBooleanPredicate,
                int predicateStart,
                int predicateLength)
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
                EntityPaths = entityPaths ?? Array.Empty<string>();
                IsBareBooleanPredicate = isBareBooleanPredicate;
                PredicateStart = predicateStart < 0 ? start : predicateStart;
                PredicateLength = predicateLength < 0 ? Math.Max(0, end - start) : predicateLength;
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
            internal IReadOnlyList<string> EntityPaths { get; }
            internal bool IsBareBooleanPredicate { get; }
            internal int PredicateStart { get; }
            internal int PredicateLength { get; }

            internal static SemanticNode Value(
                Type type,
                ExpressionNullability nullability,
                int start,
                int end,
                Type elementType = null,
                IReadOnlyList<string> entityPaths = null,
                bool isBareBooleanPredicate = false,
                int predicateStart = -1,
                int predicateLength = -1) =>
                new SemanticNode(
                    type,
                    nullability,
                    start,
                    end,
                    false,
                    false,
                    false,
                    type == null,
                    null,
                    elementType,
                    null,
                    entityPaths,
                    isBareBooleanPredicate,
                    predicateStart,
                    predicateLength);
            internal static SemanticNode Literal(Type type, int start, int end, object value) =>
                new SemanticNode(
                    type,
                    ExpressionNullability.NonNullable,
                    start,
                    end,
                    false,
                    true,
                    false,
                    false,
                    value,
                    null,
                    null,
                    null,
                    type == typeof(bool),
                    start,
                    end - start);
            internal static SemanticNode NullValue(int start, int end) =>
                new SemanticNode(null, ExpressionNullability.Nullable, start, end, true, true, false, false, null, null,
                    null, null, false, start, end - start);
            internal static SemanticNode List(int start, int end, IReadOnlyList<SemanticNode> elements, Type elementType) =>
                new SemanticNode(typeof(Array), ExpressionNullability.NonNullable, start, end, false, false, true, false, null,
                    elementType, elements, null, false, start, end - start);
            internal static SemanticNode Error(int start, int end) =>
                new SemanticNode(null, ExpressionNullability.Unknown, start, end, false, false, false, true, null, null,
                    null, null, false, start, end - start);
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
                if (_owner._queryContext != null && IsUnaryOperator(token, text))
                    return ParsePolicyUnaryChain();

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
                        ? SemanticNode.Value(
                            typeof(bool),
                            ExpressionNullability.NonNullable,
                            token.Start,
                            Math.Max(end, operand.End),
                            entityPaths: operand.EntityPaths,
                            isBareBooleanPredicate: operand.IsBareBooleanPredicate,
                            predicateStart: operand.PredicateStart,
                            predicateLength: operand.PredicateLength)
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

            private SemanticNode ParsePolicyUnaryChain()
            {
                var operators = new List<ExpressionToken>();
                while (_index < _end)
                {
                    var token = _tokens[_index];
                    var text = _owner.Text(token);
                    if (!IsUnaryOperator(token, text)) break;
                    operators.Add(token);
                    ++_index;
                }

                var groupEnd = -1;
                SemanticNode operand;
                if (IsPunctuation("("))
                {
                    ++_index;
                    operand = ParseExpression(0);
                    if (IsPunctuation(")"))
                    {
                        groupEnd = _tokens[_index].Start + _tokens[_index].Length;
                        ++_index;
                    }
                }
                else
                {
                    operand = ParsePrefix();
                }

                if (operand == null)
                {
                    var last = operators[operators.Count - 1];
                    return SemanticNode.Error(operators[0].Start, last.Start + last.Length);
                }

                for (var index = operators.Count - 1; index >= 0; --index)
                {
                    var token = operators[index];
                    var text = _owner.Text(token);
                    var end = groupEnd >= 0 ? Math.Max(groupEnd, operand.End) : operand.End;
                    _owner.CheckUnary(text, operand, token.Start, token.Start + token.Length);
                    operand = operand.Type == typeof(bool)
                        ? SemanticNode.Value(
                            typeof(bool),
                            ExpressionNullability.NonNullable,
                            token.Start,
                            end,
                            entityPaths: operand.EntityPaths,
                            isBareBooleanPredicate: operand.IsBareBooleanPredicate,
                            predicateStart: operand.PredicateStart,
                            predicateLength: operand.PredicateLength)
                        : SemanticNode.Error(token.Start, end);
                }

                return operand;
            }

            private static bool IsUnaryOperator(ExpressionToken token, string text) =>
                token.Kind == ExpressionTokenKind.Operator &&
                (text == "!" || text.Equals("not", StringComparison.OrdinalIgnoreCase));

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
