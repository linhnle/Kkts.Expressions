using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
    internal static class FilterTreeSemanticValidator
    {
        private const int MaximumDiagnostics = 32;

        internal static FilterOperationResult<FilterNode> Validate(
            FilterNode tree,
            ExpressionSchema schema,
            ExpressionVariableSchema variables = null,
            bool allowUnresolvedVariables = false)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var validator = new Validator(
                schema,
                variables ?? new ExpressionVariableSchema(null),
                allowUnresolvedVariables);
            return validator.Validate(tree);
        }

        private sealed class Validator
        {
            private readonly ExpressionSchema _schema;
            private readonly ExpressionVariableSchema _variables;
            private readonly bool _allowUnresolvedVariables;
            private readonly List<ExpressionDiagnostic> _diagnostics = new List<ExpressionDiagnostic>();
            private bool _truncated;

            internal Validator(
                ExpressionSchema schema,
                ExpressionVariableSchema variables,
                bool allowUnresolvedVariables)
            {
                _schema = schema;
                _variables = variables;
                _allowUnresolvedVariables = allowUnresolvedVariables;
            }

            internal FilterOperationResult<FilterNode> Validate(FilterNode tree)
            {
                var activeNodes = new HashSet<FilterNode>();
                var pending = new Stack<ValidationFrame>();
                pending.Push(ValidationFrame.Enter(tree, string.Empty));
                while (pending.Count > 0 && !_truncated)
                {
                    var frame = pending.Pop();
                    if (frame.IsExit)
                    {
                        activeNodes.Remove(frame.Node);
                        continue;
                    }
                    if (!activeNodes.Add(frame.Node))
                    {
                        Add(
                            "filter-tree-invalid-shape",
                            "A filter tree cannot contain a cycle.",
                            frame.Path);
                        continue;
                    }
                    pending.Push(ValidationFrame.Exit(frame.Node));
                    if (frame.Node.Kind == FilterNodeKind.Condition)
                    {
                        ValidateCondition(frame.Node, frame.Path);
                        continue;
                    }
                    if (frame.Node.Kind == FilterNodeKind.Not)
                    {
                        pending.Push(ValidationFrame.Enter(
                            frame.Node.Child,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "not")));
                        continue;
                    }

                    var member = frame.Node.Kind == FilterNodeKind.And ? "and" : "or";
                    var childrenPath = FilterTreeDiagnosticProjection.AppendPointer(frame.Path, member);
                    for (var index = frame.Node.Children.Count - 1; index >= 0; index--)
                        pending.Push(ValidationFrame.Enter(
                            frame.Node.Children[index],
                            FilterTreeDiagnosticProjection.AppendPointer(childrenPath, index.ToString())));
                }

                if (_diagnostics.Count == 0)
                    return FilterOperationResult<FilterNode>.Success(tree);
                return FilterOperationResult<FilterNode>.Failure(
                    _diagnostics,
                    isTruncated: _truncated);
            }

            private void ValidateCondition(FilterNode condition, string path)
            {
                if (!_schema.TryMapProperty(condition.Field, out var mappedPath) ||
                    !_schema.TryGetProperty(mappedPath, out var fieldType, out var nullability, out var canQuery))
                {
                    Add(
                        "unknown-property",
                        "The field does not exist in the entity schema.",
                        FilterTreeDiagnosticProjection.AppendPointer(path, "field"));
                    return;
                }
                if (!canQuery || !_schema.IsPropertyQueryable(condition.Field))
                {
                    Add(
                        "property-not-queryable",
                        "The field is not permitted for querying.",
                        FilterTreeDiagnosticProjection.AppendPointer(path, "field"));
                    return;
                }

                var valuePath = FilterTreeDiagnosticProjection.AppendPointer(path, "value");
                if (!QueryPolicyFieldMetadata.IsScalar(fieldType))
                {
                    Add(
                        "operator-not-applicable",
                        "The selected operator requires a scalar entity field.",
                        FilterTreeDiagnosticProjection.AppendPointer(path, "op"),
                        new[] { ExpressionTypeInfo.ForClr(typeof(bool)) },
                        new[] { ExpressionTypeInfo.ForClr(fieldType, nullability) });
                    return;
                }

                ComparisonOperator operation;
                try
                {
                    operation = QueryPolicyFieldMetadata.NormalizeComparisonOperator(condition.Operator);
                }
                catch (NotSupportedException)
                {
                    Add(
                        "filter-tree-unknown-operator",
                        "The condition operator is not supported.",
                        FilterTreeDiagnosticProjection.AppendPointer(path, "op"));
                    return;
                }

                if (operation == ComparisonOperator.In || operation == ComparisonOperator.NotIn)
                {
                    ValidateMembership(condition.Value, fieldType, nullability, valuePath);
                    return;
                }

                if (operation == ComparisonOperator.Contains ||
                    operation == ComparisonOperator.StartsWith ||
                    operation == ComparisonOperator.EndsWith)
                {
                    ValidateStringOperation(condition.Value, fieldType, valuePath, path);
                    return;
                }

                if (condition.Value.Kind == FilterValueKind.Variable)
                {
                    if (!TryGetVariable(condition.Value.Text, valuePath, out var variableType, out var elementType))
                        return;
                    if (variableType != null &&
                        (elementType != null ||
                         !CanCompareVariable(variableType, fieldType, condition.Operator)))
                    {
                        ReportIncompatible(valuePath, fieldType, variableType);
                        return;
                    }
                }
                else if (!ValidateLiteral(condition.Value, fieldType, nullability, valuePath))
                {
                    return;
                }

                if (!ExpressionOperatorRules.CanApplyComparison(condition.Operator, fieldType, fieldType))
                {
                    Add(
                        "operator-not-applicable",
                        "The comparison operator is not applicable to this field type.",
                        FilterTreeDiagnosticProjection.AppendPointer(path, "op"),
                        new[] { ExpressionTypeInfo.ForClr(fieldType, nullability) },
                        new[] { ExpressionTypeInfo.ForClr(fieldType, nullability) });
                }
            }

            private void ValidateMembership(
                FilterValue value,
                Type fieldType,
                ExpressionNullability nullability,
                string valuePath)
            {
                if (value.Kind == FilterValueKind.Collection)
                {
                    for (var index = 0; index < value.Items.Count && !_truncated; index++)
                    {
                        var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                        var item = value.Items[index];
                        if (item.Kind == FilterValueKind.Variable)
                        {
                            if (TryGetVariable(item.Text, itemPath, out var itemType, out var elementType))
                            {
                                if (itemType != null &&
                                    (elementType != null || !CanCompareVariable(itemType, fieldType, "=")))
                                    ReportIncompatible(itemPath, fieldType, itemType);
                            }
                        }
                        else
                        {
                            ValidateLiteral(item, fieldType, nullability, itemPath);
                        }
                    }
                    return;
                }

                if (value.Kind != FilterValueKind.Variable)
                {
                    Add(
                        "filter-tree-invalid-value",
                        "Membership conditions require an array or a collection reference.",
                        valuePath);
                    return;
                }
                if (!TryGetVariable(value.Text, valuePath, out var collectionType, out var collectionElementType))
                    return;
                if (collectionType != null &&
                    (collectionElementType == null || collectionElementType != fieldType))
                    ReportIncompatible(valuePath, typeof(IEnumerable<>).MakeGenericType(fieldType), collectionElementType);
            }

            private void ValidateStringOperation(
                FilterValue value,
                Type fieldType,
                string valuePath,
                string conditionPath)
            {
                if ((Nullable.GetUnderlyingType(fieldType) ?? fieldType) != typeof(string))
                {
                    Add(
                        "operator-not-applicable",
                        "String operators require a string field.",
                        FilterTreeDiagnosticProjection.AppendPointer(conditionPath, "op"),
                        new[] { ExpressionTypeInfo.ForClr(typeof(string)) },
                        new[] { ExpressionTypeInfo.ForClr(fieldType) });
                    return;
                }

                if (value.Kind == FilterValueKind.Variable)
                {
                    if (!TryGetVariable(value.Text, valuePath, out var variableType, out var elementType))
                        return;
                    if (variableType != null &&
                        (elementType != null || variableType != typeof(string)))
                        ReportIncompatible(valuePath, typeof(string), variableType);
                    return;
                }
                if (value.Kind != FilterValueKind.String)
                {
                    Add(
                        "incompatible-operand",
                        "String operators require a string literal or string variable.",
                        valuePath,
                        new[] { ExpressionTypeInfo.ForClr(typeof(string)) });
                    return;
                }
            }

            private bool ValidateLiteral(
                FilterValue value,
                Type fieldType,
                ExpressionNullability nullability,
                string path)
            {
                var targetType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
                if (value.Kind == FilterValueKind.Null)
                {
                    var nullable = !fieldType.IsValueType ||
                        Nullable.GetUnderlyingType(fieldType) != null;
                    if (nullability == ExpressionNullability.NonNullable) nullable = false;
                    if (nullable && ExpressionConversionRules.CanConvertLiteral(
                            null,
                            null,
                            true,
                            fieldType,
                            _schema.ConversionContext))
                        return true;
                    ReportIncompatible(path, fieldType, null);
                    return false;
                }

                if (value.Kind == FilterValueKind.Number)
                {
                    if (targetType.IsEnum)
                    {
                        if (ExpressionConversionRules.TryConvertJsonNumber(
                                value.Text,
                                Enum.GetUnderlyingType(targetType),
                                out _))
                            return true;
                    }
                    else if (targetType != typeof(char) &&
                        NumericOperands.IsNumeric(targetType) &&
                        ExpressionConversionRules.TryConvertJsonNumber(value.Text, targetType, out _))
                    {
                        return true;
                    }
                    ReportIncompatible(path, fieldType, null);
                    return false;
                }

                if (value.Kind == FilterValueKind.Boolean)
                {
                    if (targetType == typeof(bool) &&
                        ExpressionConversionRules.CanConvertLiteral(
                            value.BooleanValue,
                            typeof(bool),
                            false,
                            fieldType,
                            _schema.ConversionContext))
                        return true;
                    ReportIncompatible(path, fieldType, typeof(bool));
                    return false;
                }

                if (value.Kind == FilterValueKind.String)
                {
                    var text = value.Text;
                    if (ExpressionConversionRules.IsContextDependent(text, fieldType))
                    {
                        Add(
                            "context-dependent-conversion",
                            "Use an explicit date and time-zone offset so conversion does not depend on machine defaults.",
                            path,
                            new[] { ExpressionTypeInfo.ForClr(fieldType, nullability) },
                            new[] { ExpressionTypeInfo.ForClr(typeof(string), ExpressionNullability.NonNullable) });
                        return false;
                    }
                    if (targetType == typeof(string) ||
                        ExpressionConversionRules.CanConvertLiteral(
                            text,
                            typeof(string),
                            false,
                            fieldType,
                            _schema.ConversionContext))
                        return true;
                    ReportIncompatible(path, fieldType, typeof(string));
                    return false;
                }

                Add(
                    "filter-tree-invalid-value",
                    "The condition value has an unsupported shape.",
                    path);
                return false;
            }

            private bool TryGetVariable(string name, string path, out Type type, out Type elementType)
            {
                if (_variables.TryGetVariable(name, out type, out _, out elementType, out var rootDeclared))
                    return true;
                if (_allowUnresolvedVariables)
                {
                    type = null;
                    elementType = null;
                    return true;
                }
                Add(
                    rootDeclared ? "unknown-variable-member" : "undeclared-variable",
                    rootDeclared
                        ? "A variable member is not declared."
                        : "The variable is not declared.",
                    path);
                type = null;
                elementType = null;
                return false;
            }

            private bool CanCompareVariable(Type variableType, Type fieldType, string op)
            {
                if (variableType == null) return false;
                var source = Nullable.GetUnderlyingType(variableType) ?? variableType;
                var target = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
                if (source == target) return true;
                if (NumericOperands.IsNumeric(source) && NumericOperands.IsNumeric(target))
                    return true;
                if (source == typeof(string) &&
                    (target == typeof(bool) || NumericOperands.IsNumeric(target) ||
                     target == typeof(Guid) || target.IsEnum ||
                     target == typeof(DateTime) || target == typeof(DateTimeOffset) ||
                     target == typeof(TimeSpan)))
                    return true;
                return ExpressionOperatorRules.CanApplyComparison(op, fieldType, variableType);
            }

            private void ReportIncompatible(string path, Type expected, Type actual)
            {
                Add(
                    "incompatible-operand",
                    "The value is not compatible with the field type.",
                    path,
                    expected == null ? null : new[] { ExpressionTypeInfo.ForClr(expected) },
                    actual == null ? null : new[] { ExpressionTypeInfo.ForClr(actual) });
            }

            private void Add(
                string code,
                string message,
                string path,
                IEnumerable<ExpressionTypeInfo> expected = null,
                IEnumerable<ExpressionTypeInfo> actual = null)
            {
                if (_diagnostics.Count < MaximumDiagnostics)
                {
                    _diagnostics.Add(new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        code,
                        message,
                        0,
                        0,
                        expected,
                        actual,
                        inputPath: path));
                    return;
                }

                _diagnostics.RemoveAt(MaximumDiagnostics - 1);
                _diagnostics.Add(new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    "query-policy-diagnostics-truncated",
                    "Additional diagnostics were omitted; fix the reported issues before retrying.",
                    0,
                    0,
                    inputPath: path));
                _truncated = true;
            }
        }

        private sealed class ValidationFrame
        {
            private ValidationFrame(FilterNode node, string path, bool isExit)
            {
                Node = node;
                Path = path;
                IsExit = isExit;
            }

            internal FilterNode Node { get; }
            internal string Path { get; }
            internal bool IsExit { get; }

            internal static ValidationFrame Enter(FilterNode node, string path) =>
                new ValidationFrame(node, path, false);

            internal static ValidationFrame Exit(FilterNode node) =>
                new ValidationFrame(node, string.Empty, true);
        }
    }
}
