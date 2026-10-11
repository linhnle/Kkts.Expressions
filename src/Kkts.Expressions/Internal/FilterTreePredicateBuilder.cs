using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal
{
    internal static class FilterTreePredicateBuilder
    {
        internal static LambdaExpression Build(
            FilterNode tree,
            ExpressionSchema schema,
            VariableResolver variableResolver = null,
            BuildArgument policyArgument = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (schema == null) throw new ArgumentNullException(nameof(schema));

            var resolver = policyArgument?.VariableResolver ?? variableResolver ?? new VariableResolver();
            var parameter = Expression.Parameter(schema.EntityType, "entity");
            var expressions = new Stack<Expression>();
            var frames = new Stack<Frame>();
            frames.Push(new Frame(tree, string.Empty, false));

            while (frames.Count > 0)
            {
                var frame = frames.Pop();
                if (!frame.IsExit)
                {
                    if (frame.Node.Kind == FilterNodeKind.Condition)
                    {
                        expressions.Push(BuildCondition(
                            frame.Node,
                            parameter,
                            schema,
                            resolver,
                            frame.Path,
                            policyArgument));
                        continue;
                    }

                    frames.Push(new Frame(frame.Node, frame.Path, true));
                    if (frame.Node.Kind == FilterNodeKind.Not)
                    {
                        frames.Push(new Frame(
                            frame.Node.Child,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "not"),
                            false));
                    }
                    else
                    {
                        var member = frame.Node.Kind == FilterNodeKind.And ? "and" : "or";
                        var childrenPath = FilterTreeDiagnosticProjection.AppendPointer(frame.Path, member);
                        for (var index = frame.Node.Children.Count - 1; index >= 0; index--)
                            frames.Push(new Frame(
                                frame.Node.Children[index],
                                FilterTreeDiagnosticProjection.AppendPointer(childrenPath, index.ToString()),
                                false));
                    }
                    continue;
                }

                Assemble(frame.Node, expressions);
            }

            return Expression.Lambda(expressions.Pop(), parameter);
        }

        internal static async Task<LambdaExpression> BuildAsync(
            FilterNode tree,
            ExpressionSchema schema,
            VariableResolver variableResolver = null,
            CancellationToken cancellationToken = default,
            BuildArgument policyArgument = null)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            cancellationToken.ThrowIfCancellationRequested();

            var resolver = policyArgument?.VariableResolver ?? variableResolver ?? new VariableResolver();
            if (policyArgument != null) policyArgument.CancellationToken = cancellationToken;
            var parameter = Expression.Parameter(schema.EntityType, "entity");
            var expressions = new Stack<Expression>();
            var frames = new Stack<Frame>();
            frames.Push(new Frame(tree, string.Empty, false));

            while (frames.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = frames.Pop();
                if (!frame.IsExit)
                {
                    if (frame.Node.Kind == FilterNodeKind.Condition)
                    {
                        expressions.Push(await BuildConditionAsync(
                            frame.Node,
                            parameter,
                            schema,
                            resolver,
                            frame.Path,
                            cancellationToken,
                            policyArgument).ConfigureAwait(false));
                        continue;
                    }

                    frames.Push(new Frame(frame.Node, frame.Path, true));
                    if (frame.Node.Kind == FilterNodeKind.Not)
                    {
                        frames.Push(new Frame(
                            frame.Node.Child,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "not"),
                            false));
                    }
                    else
                    {
                        var member = frame.Node.Kind == FilterNodeKind.And ? "and" : "or";
                        var childrenPath = FilterTreeDiagnosticProjection.AppendPointer(frame.Path, member);
                        for (var index = frame.Node.Children.Count - 1; index >= 0; index--)
                            frames.Push(new Frame(
                                frame.Node.Children[index],
                                FilterTreeDiagnosticProjection.AppendPointer(childrenPath, index.ToString()),
                                false));
                    }
                    continue;
                }

                Assemble(frame.Node, expressions);
            }

            return Expression.Lambda(expressions.Pop(), parameter);
        }

        private static Expression BuildCondition(
            FilterNode condition,
            ParameterExpression parameter,
            ExpressionSchema schema,
            VariableResolver variableResolver,
            string nodePath,
            BuildArgument policyArgument)
        {
            if (!schema.TryMapProperty(condition.Field, out var mappedPath) ||
                !schema.TryGetProperty(mappedPath, out var propertyType, out _, out _))
                throw new InvalidOperationException("The condition field is not present in the entity schema.");

            var member = policyArgument == null
                ? BuildMemberAccess(parameter, mappedPath)
                : policyArgument.BuildPropertyExpression(parameter, condition.Field);
            var operation = QueryPolicyFieldMetadata.NormalizeComparisonOperator(condition.Operator);
            var valuePath = FilterTreeDiagnosticProjection.AppendPointer(nodePath, "value");
            try
            {
                if (operation == ComparisonOperator.In || operation == ComparisonOperator.NotIn)
                {
                    var collection = BuildMembershipCollection(
                        condition.Value,
                        propertyType,
                        schema.ConversionContext,
                        variableResolver,
                        valuePath,
                        policyArgument);
                    return TypedFilterExpressionBuilder.BuildMembership(
                        member,
                        collection,
                        propertyType,
                        operation == ComparisonOperator.NotIn);
                }

                var literal = condition.Value.Kind == FilterValueKind.Variable
                    ? ResolveVariable(condition.Value.Text, variableResolver, valuePath, propertyType)
                    : ConvertLiteral(condition.Value, propertyType, schema.ConversionContext);
                var right = CreateTypedConstant(literal, propertyType);
                return TypedFilterExpressionBuilder.ApplyComparison(member, operation, right);
            }
            catch (FilterTreeBuildException)
            {
                throw;
            }
            catch (QueryPolicyException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FilterTreeBuildException(
                    "filter-tree-runtime-conversion",
                    "The filter value could not be converted for the entity field.",
                    valuePath,
                    exception);
            }
        }

        private static Expression BuildMembershipCollection(
            FilterValue value,
            Type propertyType,
            ExpressionConversionContext context,
            VariableResolver variableResolver,
            string valuePath,
            BuildArgument policyArgument)
        {
            if (value.Kind == FilterValueKind.Variable)
            {
                var resolved = ResolveVariable(value.Text, variableResolver, valuePath, null);
                if (policyArgument != null)
                {
                    policyArgument.MembershipInputPath = valuePath;
                    resolved = policyArgument.SnapshotDirectMembershipCollection(
                        resolved,
                        elementType: null,
                        inputPath: valuePath);
                }
                var source = resolved as IEnumerable;
                if (source == null || source is string)
                    throw new InvalidCastException("A collection reference must resolve to a non-string enumerable.");
                var expressions = new List<Expression>();
                var index = 0;
                foreach (var item in source)
                {
                    var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                    try
                    {
                        expressions.Add(CreateTypedConstant(
                            ConvertVariableValue(item, propertyType),
                            propertyType));
                    }
                    catch (Exception exception)
                    {
                        throw new FilterTreeBuildException(
                            "filter-tree-runtime-conversion",
                            "A membership value could not be converted for the entity field.",
                            itemPath,
                            exception);
                    }
                    index++;
                }
                return Expression.NewArrayInit(propertyType, expressions);
            }

            if (value.Kind != FilterValueKind.Collection)
                throw new InvalidCastException("Membership conditions require a collection or collection reference.");

            var values = new Expression[value.Items.Count];
            for (var index = 0; index < value.Items.Count; index++)
            {
                var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                try
                {
                    var item = value.Items[index];
                    var converted = item.Kind == FilterValueKind.Variable
                        ? ResolveVariable(item.Text, variableResolver, itemPath, propertyType)
                        : ConvertLiteral(item, propertyType, context);
                    values[index] = CreateTypedConstant(converted, propertyType);
                }
                catch (FilterTreeBuildException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new FilterTreeBuildException(
                        "filter-tree-runtime-conversion",
                        "A membership value could not be converted for the entity field.",
                        itemPath,
                        exception);
                }
            }
            return Expression.NewArrayInit(propertyType, values);
        }

        private static async Task<Expression> BuildConditionAsync(
            FilterNode condition,
            ParameterExpression parameter,
            ExpressionSchema schema,
            VariableResolver variableResolver,
            string nodePath,
            CancellationToken cancellationToken,
            BuildArgument policyArgument)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schema.TryMapProperty(condition.Field, out var mappedPath) ||
                !schema.TryGetProperty(mappedPath, out var propertyType, out _, out _))
                throw new InvalidOperationException("The condition field is not present in the entity schema.");

            var member = policyArgument == null
                ? BuildMemberAccess(parameter, mappedPath)
                : policyArgument.BuildPropertyExpression(parameter, condition.Field);
            var operation = QueryPolicyFieldMetadata.NormalizeComparisonOperator(condition.Operator);
            var valuePath = FilterTreeDiagnosticProjection.AppendPointer(nodePath, "value");
            try
            {
                if (operation == ComparisonOperator.In || operation == ComparisonOperator.NotIn)
                {
                    var collection = await BuildMembershipCollectionAsync(
                        condition.Value,
                        propertyType,
                        schema.ConversionContext,
                        variableResolver,
                        valuePath,
                        cancellationToken,
                        policyArgument).ConfigureAwait(false);
                    return TypedFilterExpressionBuilder.BuildMembership(
                        member,
                        collection,
                        propertyType,
                        operation == ComparisonOperator.NotIn);
                }

                var literal = condition.Value.Kind == FilterValueKind.Variable
                    ? await ResolveVariableAsync(
                        condition.Value.Text,
                        variableResolver,
                        valuePath,
                        propertyType,
                        cancellationToken).ConfigureAwait(false)
                    : ConvertLiteral(condition.Value, propertyType, schema.ConversionContext);
                var right = CreateTypedConstant(literal, propertyType);
                return TypedFilterExpressionBuilder.ApplyComparison(member, operation, right);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FilterTreeBuildException)
            {
                throw;
            }
            catch (QueryPolicyException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FilterTreeBuildException(
                    "filter-tree-runtime-conversion",
                    "The filter value could not be converted for the entity field.",
                    valuePath,
                    exception);
            }
        }

        private static async Task<Expression> BuildMembershipCollectionAsync(
            FilterValue value,
            Type propertyType,
            ExpressionConversionContext context,
            VariableResolver variableResolver,
            string valuePath,
            CancellationToken cancellationToken,
            BuildArgument policyArgument)
        {
            if (value.Kind == FilterValueKind.Variable)
            {
                var resolved = await ResolveVariableAsync(
                    value.Text,
                    variableResolver,
                    valuePath,
                    null,
                    cancellationToken).ConfigureAwait(false);
                if (policyArgument != null)
                {
                    policyArgument.MembershipInputPath = valuePath;
                    resolved = policyArgument.SnapshotDirectMembershipCollection(
                        resolved,
                        elementType: null,
                        inputPath: valuePath,
                        cancellationToken: cancellationToken);
                }
                if (!(resolved is IEnumerable source) || resolved is string)
                    throw new InvalidCastException("A collection reference must resolve to a non-string enumerable.");
                var expressions = new List<Expression>();
                var index = 0;
                foreach (var item in source)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                    try
                    {
                        expressions.Add(CreateTypedConstant(ConvertVariableValue(item, propertyType), propertyType));
                    }
                    catch (Exception exception)
                    {
                        throw new FilterTreeBuildException(
                            "filter-tree-runtime-conversion",
                            "A membership value could not be converted for the entity field.",
                            itemPath,
                            exception);
                    }
                    index++;
                }
                return Expression.NewArrayInit(propertyType, expressions);
            }

            if (value.Kind != FilterValueKind.Collection)
                throw new InvalidCastException("Membership conditions require a collection or collection reference.");

            var values = new Expression[value.Items.Count];
            for (var index = 0; index < value.Items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                try
                {
                    var item = value.Items[index];
                    var converted = item.Kind == FilterValueKind.Variable
                        ? await ResolveVariableAsync(
                            item.Text,
                            variableResolver,
                            itemPath,
                            propertyType,
                            cancellationToken).ConfigureAwait(false)
                        : ConvertLiteral(item, propertyType, context);
                    values[index] = CreateTypedConstant(converted, propertyType);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FilterTreeBuildException)
                {
                    throw;
                }
                catch (QueryPolicyException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new FilterTreeBuildException(
                        "filter-tree-runtime-conversion",
                        "A membership value could not be converted for the entity field.",
                        itemPath,
                        exception);
                }
            }
            return Expression.NewArrayInit(propertyType, values);
        }

        private static Expression BuildMemberAccess(ParameterExpression parameter, string path)
        {
            Expression current = parameter;
            var currentType = parameter.Type;
            foreach (var segment in ExpressionSchema.NormalizePath(path).Split('.'))
            {
                var member = (MemberInfo)currentType.GetProperty(
                    segment,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (member == null)
                    member = currentType.GetField(
                        segment,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (member == null)
                    throw new InvalidOperationException("A mapped entity field could not be bound.");

                current = Expression.MakeMemberAccess(current, member);
                currentType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
            }
            return current;
        }

        private static Expression CreateTypedConstant(object value, Type targetType)
        {
            if (value == null) return Expression.Constant(null, targetType);
            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
                return Expression.Convert(Expression.Constant(value, nullableType), targetType);
            return Expression.Constant(value, targetType);
        }

        private static object ConvertLiteral(
            FilterValue value,
            Type targetType,
            ExpressionConversionContext context)
        {
            if (value.Kind == FilterValueKind.Variable)
                throw new NotSupportedException("Variable references are constructed by the runtime tree builder.");
            if (value.Kind == FilterValueKind.Null) return null;

            var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value.Kind == FilterValueKind.Boolean)
            {
                if (effectiveType != typeof(bool))
                    throw new InvalidCastException("A Boolean filter value requires a Boolean entity field.");
                return value.BooleanValue;
            }

            if (value.Kind == FilterValueKind.Number)
            {
                var numericType = effectiveType.IsEnum ? Enum.GetUnderlyingType(effectiveType) : effectiveType;
                if (!ExpressionConversionRules.TryConvertJsonNumber(value.Text, numericType, out var numericValue))
                    throw new InvalidCastException("The numeric filter value is outside the entity field's supported range.");
                return effectiveType.IsEnum
                    ? Enum.ToObject(effectiveType, numericValue)
                    : numericValue;
            }

            if (value.Kind != FilterValueKind.String)
                throw new InvalidCastException("The filter value cannot be converted to a scalar entity field.");

            var text = value.Text;
            if (effectiveType == typeof(string)) return text;
            if (effectiveType == typeof(char))
            {
                if (text.Length != 1) throw new InvalidCastException("A character filter value must contain exactly one character.");
                return text[0];
            }
            if (effectiveType.IsEnum) return Enum.Parse(effectiveType, text, ignoreCase: true);
            if (effectiveType == typeof(Guid)) return Guid.Parse(text);
            if (effectiveType == typeof(DateTime))
            {
                if (DateTime.TryParse(text, context.Culture, DateTimeStyles.RoundtripKind, out var dateTime))
                    return dateTime;
                if (DateTime.TryParseExact(text, ToArray(context.DateTimeFormats), context.Culture, DateTimeStyles.RoundtripKind, out dateTime))
                    return dateTime;
                throw new FormatException("The date-time filter value is invalid for the configured conversion context.");
            }
            if (effectiveType == typeof(DateTimeOffset))
            {
                if (!DateTimeOffset.TryParse(text, context.Culture, DateTimeStyles.None, out var dateTimeOffset) &&
                    !DateTimeOffset.TryParseExact(text, ToArray(context.DateTimeFormats), context.Culture, DateTimeStyles.None, out dateTimeOffset))
                    throw new FormatException("The date-time-offset filter value is invalid for the configured conversion context.");
                return dateTimeOffset;
            }
            if (effectiveType == typeof(TimeSpan))
                return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            if (effectiveType == typeof(bool))
                return bool.Parse(text);
            if (NumericOperands.IsNumeric(effectiveType))
                return Convert.ChangeType(text, effectiveType, context.Culture);

            throw new InvalidCastException("The string filter value is not supported for this entity field type.");
        }

        private static object ResolveVariable(
            string name,
            VariableResolver resolver,
            string path,
            Type targetType)
        {
            if (!resolver.TryResolve(name, out var value))
                throw new FilterTreeBuildException(
                    "filter-tree-variable-unavailable",
                    "A referenced filter value could not be resolved.",
                    path,
                    new InvalidOperationException("The variable reference is unavailable."));
            return targetType == null ? value : ConvertVariableValue(value, targetType);
        }

        private static async Task<object> ResolveVariableAsync(
            string name,
            VariableResolver resolver,
            string path,
            Type targetType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await resolver.TryResolveAsync(name, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved?.Resolved != true)
                throw new FilterTreeBuildException(
                    "filter-tree-variable-unavailable",
                    "A referenced filter value could not be resolved.",
                    path,
                    new InvalidOperationException("The variable reference is unavailable."));
            return targetType == null
                ? resolved.Value
                : ConvertVariableValue(resolved.Value, targetType);
        }

        private static object ConvertVariableValue(object value, Type targetType)
        {
            if (value == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return null;
                throw new InvalidCastException("A null value is incompatible with the entity field.");
            }

            var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (effectiveType.IsInstanceOfType(value)) return value;
            return value.Cast(targetType);
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (var index = 0; index < values.Count; index++) result[index] = values[index];
            return result;
        }

        private static void Assemble(FilterNode node, Stack<Expression> expressions)
        {
            if (node.Kind == FilterNodeKind.Not)
            {
                expressions.Push(Expression.Not(expressions.Pop()));
                return;
            }
            if (node.Kind != FilterNodeKind.And && node.Kind != FilterNodeKind.Or)
                throw new InvalidOperationException("A condition node cannot be an assembly frame.");

            var children = new Expression[node.Children.Count];
            for (var index = children.Length - 1; index >= 0; index--)
                children[index] = expressions.Pop();
            var result = children[0];
            for (var index = 1; index < children.Length; index++)
                result = node.Kind == FilterNodeKind.And
                    ? Expression.AndAlso(result, children[index])
                    : Expression.OrElse(result, children[index]);
            expressions.Push(result);
        }

        private readonly struct Frame
        {
            internal Frame(FilterNode node, string path, bool isExit)
            {
                Node = node;
                Path = path;
                IsExit = isExit;
            }

            internal FilterNode Node { get; }
            internal string Path { get; }
            internal bool IsExit { get; }
        }
    }

    internal sealed class FilterTreeBuildException : Exception
    {
        internal FilterTreeBuildException(string code, string message, string path, Exception innerException)
            : base(message, innerException)
        {
            Diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                code,
                message,
                0,
                0,
                inputPath: path);
        }

        internal ExpressionDiagnostic Diagnostic { get; }
    }
}
