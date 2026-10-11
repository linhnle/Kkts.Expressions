using System;
using System.Collections.Generic;

namespace Kkts.Expressions.Internal
{
    internal static class FilterTreePolicyScanner
    {
        internal static bool TryScan(
            FilterNode tree,
            QueryPolicyExecution execution,
            string rootPath = null)
        {
            return TryScan(tree, null, execution, rootPath);
        }

        internal static bool TryScan(
            FilterNode tree,
            ExpressionQueryContext context,
            QueryPolicyExecution execution,
            string rootPath = null,
            bool countMembershipItems = true)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (execution == null) throw new ArgumentNullException(nameof(execution));

            var frames = new Stack<Frame>();
            frames.Push(new Frame(tree, 0, rootPath ?? string.Empty));
            while (frames.Count > 0)
            {
                var frame = frames.Pop();
                var node = frame.Node;
                if (node.Kind == FilterNodeKind.Condition)
                {
                    if (context != null &&
                        !TryValidateFieldPolicy(node, frame.Path, context, execution))
                        return false;
                    if (!execution.TryCountCondition(
                            0,
                            0,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "field")))
                        return false;
                    if (!execution.TryAdmitTreeText(
                            node.Field.Length,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "field")) ||
                        !execution.TryAdmitTreeText(
                            node.Operator.Length,
                            FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "op")))
                        return false;

                    var valuePath = FilterTreeDiagnosticProjection.AppendPointer(frame.Path, "value");
                    if (!TryAdmitValueText(node.Value, execution, valuePath))
                        return false;

                    var operation = QueryPolicyFieldMetadata.NormalizeComparisonOperator(node.Operator);
                    if (operation == ComparisonOperator.In || operation == ComparisonOperator.NotIn)
                    {
                        if (countMembershipItems && node.Value.Kind == FilterValueKind.Collection)
                        {
                            var counter = execution.CreateMembershipCounter();
                            for (var index = 0; index < node.Value.Items.Count; index++)
                            {
                                var itemPath = FilterTreeDiagnosticProjection.AppendPointer(valuePath, index.ToString());
                                if (!execution.TryCountMembershipItem(counter, 0, 0, itemPath))
                                    return false;
                            }
                        }
                    }
                    continue;
                }

                var nextDepth = frame.Depth + 1;
                var member = node.Kind == FilterNodeKind.And
                    ? "and"
                    : node.Kind == FilterNodeKind.Or ? "or" : "not";
                var nodePath = FilterTreeDiagnosticProjection.AppendPointer(frame.Path, member);
                if (execution.Policy.MaxFilterTreeDepth.HasValue &&
                    nextDepth > execution.Policy.MaxFilterTreeDepth.Value)
                {
                    execution.Diagnostics.Add(
                        "query-policy-filter-tree-depth-exceeded",
                        "The filter tree exceeds the configured logical depth limit.",
                        0,
                        0,
                        execution.Policy.MaxFilterTreeDepth.Value,
                        nextDepth,
                        inputPath: nodePath);
                    return false;
                }

                if (node.Kind == FilterNodeKind.Not)
                {
                    frames.Push(new Frame(node.Child, nextDepth, nodePath));
                    continue;
                }

                for (var index = node.Children.Count - 1; index >= 0; index--)
                    frames.Push(new Frame(
                        node.Children[index],
                        nextDepth,
                        FilterTreeDiagnosticProjection.AppendPointer(nodePath, index.ToString())));
            }
            return true;
        }

        private static bool TryValidateFieldPolicy(
            FilterNode condition,
            string path,
            ExpressionQueryContext context,
            QueryPolicyExecution execution)
        {
            if (!context.Schema.TryGetPolicyPathInfo(
                    condition.Field,
                    out _,
                    out var navigationDepth,
                    out var collectionAccess))
                return true;

            var fieldPath = FilterTreeDiagnosticProjection.AppendPointer(path, "field");
            if (!context.IsPropertyQueryable(condition.Field))
            {
                execution.Diagnostics.Add(
                    "property-not-queryable",
                    "The field is not permitted for querying.",
                    0,
                    0,
                    inputPath: fieldPath);
                return false;
            }
            if (!context.IsNavigationAllowed(condition.Field, out navigationDepth))
            {
                execution.Diagnostics.Add(
                    "query-policy-navigation-depth-exceeded",
                    "The field exceeds the configured entity navigation depth.",
                    0,
                    0,
                    context.Policy.MaxNavigationDepth,
                    navigationDepth,
                    inputPath: fieldPath);
                return false;
            }
            if (!context.Policy.AllowCollectionAccess && collectionAccess)
            {
                execution.Diagnostics.Add(
                    "query-policy-collection-access-denied",
                    "Traversal through entity collection fields is not permitted.",
                    0,
                    0,
                    inputPath: fieldPath);
                return false;
            }

            var operation = QueryPolicyFieldMetadata.NormalizeComparisonOperator(condition.Operator);
            if (!context.IsOperatorAllowed(condition.Field, operation))
            {
                execution.Diagnostics.Add(
                    "query-policy-operator-denied",
                    "The comparison operator is not permitted for this field.",
                    0,
                    0,
                    inputPath: FilterTreeDiagnosticProjection.AppendPointer(path, "op"));
                return false;
            }
            return true;
        }

        private static bool TryAdmitValueText(
            FilterValue value,
            QueryPolicyExecution execution,
            string path)
        {
            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    return execution.TryAdmitTreeText(4, path);
                case FilterValueKind.Boolean:
                    return execution.TryAdmitTreeText(value.BooleanValue ? 4 : 5, path);
                case FilterValueKind.Number:
                case FilterValueKind.String:
                case FilterValueKind.Variable:
                    return execution.TryAdmitTreeText(value.Text.Length, path);
                case FilterValueKind.Collection:
                    for (var index = 0; index < value.Items.Count; index++)
                    {
                        if (!TryAdmitValueText(
                                value.Items[index],
                                execution,
                                FilterTreeDiagnosticProjection.AppendPointer(path, index.ToString())))
                            return false;
                    }
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown filter value kind.");
            }
        }

        private readonly struct Frame
        {
            internal Frame(FilterNode node, int depth, string path)
            {
                Node = node;
                Depth = depth;
                Path = path;
            }

            internal FilterNode Node { get; }
            internal int Depth { get; }
            internal string Path { get; }
        }
    }
}
