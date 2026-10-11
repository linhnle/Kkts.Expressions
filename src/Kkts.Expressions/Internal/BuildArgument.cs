using Kkts.Expressions.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Kkts.Expressions.Internal.Nodes;

namespace Kkts.Expressions
{
    internal class BuildArgument
    {
        private IReadOnlyDictionary<string, string> _lookup;
        private HashSet<string> _nestedProperties;
        private Type _evaluationType;
        private IDictionary<string, string> _mapping;
        private Func<string, string> _evaluateMapping;
        private Func<string, bool> _evaluateValidProperty;
        private VariableResolver _variableResolver = new VariableResolver();

        public BuildArgument()
        {
            _evaluateMapping = EmptyMap;
        }

        public VariableResolver VariableResolver
        {
            get => _variableResolver;
            set
            {
                _variableResolver = value ?? _variableResolver;
            }
        }

        public IEnumerable<string> ValidProperties
        {
            get => _lookup?.Keys;
            set
            {
                var lookup = value?.ToDictionary(k => k, StringComparer.OrdinalIgnoreCase);
                if (lookup == null || lookup.Count == 0)
                {
                    _evaluateValidProperty = TryEvaluateValidProperty;
                }
                else
                {
                    _lookup = lookup;
                    _evaluateValidProperty = IsExactValidProperty;
                }
            }
        }

        public Type EvaluationType
        {
            get => _evaluationType;
            set
            {
                _evaluationType = value;
                if (_lookup?.Count > 0) return;
                ImportValidProperties();
                _evaluateValidProperty = TryEvaluateValidProperty;
            }
        }

        public readonly ICollection<string> InvalidProperties = new List<string>();

        public readonly ICollection<string> InvalidOperators = new List<string>();

        public readonly ICollection<string> InvalidVariables = new List<string>();

        public readonly ICollection<string> InvalidValues = new List<string>();

        public readonly ICollection<string> InvalidOrderByDirections = new List<string>();

        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        public ExpressionQueryContext QueryContext { get; set; }

        public ExpressionSchema Schema { get; set; }

        public QueryPolicyExecution PolicyExecution { get; set; }

        public int SourceOffset { get; set; }

        public string SourceExpression { get; set; }

        public string MembershipInputPath { get; set; }

        public IDictionary<string, string> PropertyMapping
        {
            get => _mapping;
            set
            {
                if (value is null || value.Count == 0)
                {
                    _mapping = null;
                    _evaluateMapping = EmptyMap;
                    return;
                }

                _mapping = new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
                _evaluateMapping = PartialMap;
            }
        }

        public string MapProperty(string name)
        {
            if (Schema?.IsPublicSchema == true)
                return name;
            return _evaluateMapping(name);
        }

        internal Expression BuildPropertyExpression(ParameterExpression parameter, string sourcePath)
        {
            if (Schema != null && Schema.TryResolveQueryField(sourcePath, out var field))
                return field.Compose(parameter);
            return parameter.CreatePropertyExpression(MapProperty(sourcePath));
        }

        internal bool IsValidOrderByProperty(string value)
        {
            if (Schema?.IsPublicSchema == true)
            {
                if (Schema.TryResolveQueryField(value, out var field) &&
                    field.CanSort &&
                    (QueryContext == null || QueryContext.IsPropertySortable(value)))
                    return true;
                InvalidProperties.Add(value ?? "null");
                return false;
            }
            return IsValidProperty(value);
        }

        internal bool TryResolveQueryField(string sourcePath, out ResolvedQueryField field)
        {
            if (Schema != null)
                return Schema.TryResolveQueryField(sourcePath, out field);
            field = null;
            return false;
        }

        internal void ValidateQueryProperty(string sourcePath, int start, int length)
        {
            if (QueryContext == null) return;

            var policyDiagnostics = new List<ExpressionDiagnostic>();
            if (!QueryContext.Schema.TryGetPolicyPathInfo(sourcePath, out _, out var navigationDepth, out var collectionAccess) ||
                !QueryContext.IsPropertyQueryable(sourcePath))
            {
                policyDiagnostics.Add(new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    "property-not-queryable",
                    "The field is not permitted for querying.",
                    start + SourceOffset,
                    length));
            }
            else
            {
                if (!QueryContext.IsNavigationAllowed(sourcePath, out navigationDepth))
                {
                    policyDiagnostics.Add(new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        "query-policy-navigation-depth-exceeded",
                        "The field exceeds the configured entity navigation depth.",
                        start + SourceOffset,
                        length,
                        configuredLimit: QueryContext.Policy.MaxNavigationDepth,
                        observedValue: navigationDepth));
                }
                if (!QueryContext.Policy.AllowCollectionAccess && collectionAccess)
                {
                    policyDiagnostics.Add(new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        "query-policy-collection-access-denied",
                        "Traversal through entity collection fields is not permitted.",
                        start + SourceOffset,
                        length));
                }
            }

            if (policyDiagnostics.Count > 0)
                throw new QueryPolicyException(policyDiagnostics);
        }

        internal void ValidateComparison(string operatorName, Node left, Node right, int start)
        {
            if (QueryContext == null) return;
            var normalized = Interpreter.NormalizeComparisonOperator(operatorName);
            if (!Interpreter.ComparisonOperators.Contains(
                    normalized,
                    StringComparer.OrdinalIgnoreCase))
                return;

            var comparisonOperator = normalized.GetComparisonOperator();
            var denied = GetEntityPaths(left, right)
                .Any(path => !QueryContext.IsOperatorAllowed(path, comparisonOperator));
            if (!denied) return;

            throw new QueryPolicyException(new[]
            {
                new ExpressionDiagnostic(
                    ExpressionDiagnosticKind.Semantic,
                    "query-policy-operator-denied",
                    "The comparison operator is not permitted for one or more fields in this predicate.",
                    start + SourceOffset,
                    normalized.Length)
            });
        }

        internal void SnapshotMembershipVariable(Constant variable)
        {
            if (QueryContext == null ||
                !QueryContext.Policy.MaxInItems.HasValue ||
                !variable.IsVariable ||
                variable.HasResolvedObjectValue)
                return;

            if (!VariableResolver.TryResolve(variable.Value, out var value))
            {
                InvalidVariables.Add(variable.Value);
                InvalidProperties.Add(variable.Value);
                throw new InvalidCastException($"Invalid variable or property, name {variable.Value}");
            }

            variable.HasResolvedObjectValue = true;
            variable.ResolvedObjectValue = SnapshotMembershipValue(value, variable);
        }

        internal async Task SnapshotMembershipVariableAsync(Constant variable)
        {
            if (QueryContext == null ||
                !QueryContext.Policy.MaxInItems.HasValue ||
                !variable.IsVariable ||
                variable.HasResolvedObjectValue)
                return;

            CancellationToken.ThrowIfCancellationRequested();
            var variableInfo = await VariableResolver.TryResolveAsync(variable.Value, CancellationToken)
                .ConfigureAwait(false);
            if (variableInfo?.Resolved != true)
            {
                InvalidVariables.Add(variable.Value);
                InvalidProperties.Add(variable.Value);
                throw new InvalidCastException($"Invalid variable or property, name {variable.Value}");
            }

            variable.HasResolvedObjectValue = true;
            variable.ResolvedObjectValue = SnapshotMembershipValue(variableInfo.Value, variable);
        }

        private object SnapshotMembershipValue(object value, Constant variable)
        {
            if (value == null || value is string || !(value is IEnumerable enumerable))
                return value;

            int start;
            int length;
            if (MembershipInputPath == null)
                GetOriginalSourceSpan(variable, out start, out length);
            else
            {
                start = 0;
                length = 0;
            }
            return SnapshotMembershipCollection(value, enumerable, start, length, MembershipInputPath);
        }

        internal object SnapshotDirectMembershipCollection(
            object value,
            Type elementType,
            string inputPath = "Value",
            CancellationToken cancellationToken = default)
        {
            if (QueryContext == null ||
                !QueryContext.Policy.MaxInItems.HasValue ||
                value == null ||
                value is string ||
                !(value is IEnumerable enumerable))
                return value;

            return SnapshotMembershipCollection(
                value,
                enumerable,
                0,
                0,
                inputPath,
                elementType,
                cancellationToken);
        }

        private object SnapshotMembershipCollection(
            object value,
            IEnumerable enumerable,
            int start,
            int length,
            string inputPath,
            Type targetElementType = null,
            CancellationToken cancellationToken = default)
        {
            var maximum = QueryContext.Policy.MaxInItems.Value;
            if (value is IQueryable)
            {
                throw new QueryPolicyException(new[]
                {
                    new ExpressionDiagnostic(
                        ExpressionDiagnosticKind.Semantic,
                        "query-policy-in-items-unverifiable",
                        "The membership collection cannot be safely checked under the configured item limit.",
                        start,
                        length,
                        configuredLimit: maximum,
                        inputPath: inputPath)
                });
            }

            var counter = PolicyExecution.CreateMembershipCounter();
            var items = new List<object>();
            var enumerator = enumerable.GetEnumerator();
            using (enumerator as IDisposable)
            {
                while (enumerator.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!PolicyExecution.TryCountMembershipItem(counter, start, length, inputPath))
                        throw new QueryPolicyException(PolicyExecution.Diagnostics.ToReadOnlyList());
                    items.Add(enumerator.Current);
                }
            }

            var elementType = targetElementType ?? GetEnumerableElementType(value.GetType());
            var snapshot = Array.CreateInstance(elementType, items.Count);
            for (var index = 0; index < items.Count; index++)
                snapshot.SetValue(items[index], index);
            return snapshot;
        }

        private void GetOriginalSourceSpan(Constant variable, out int start, out int length)
        {
            var sourceToken = variable.Value ?? string.Empty;
            if (sourceToken.Length == 0 || sourceToken[0] != VariableResolver.VariablePrefix)
                sourceToken = VariableResolver.VariablePrefix + sourceToken;

            start = variable.StartIndex + SourceOffset;
            length = sourceToken.Length;
            if (string.IsNullOrEmpty(SourceExpression)) return;

            var searchStart = Math.Max(0, start - sourceToken.Length);
            var sourceStart = SourceExpression.IndexOf(sourceToken, searchStart, StringComparison.Ordinal);
            if (sourceStart >= 0) start = sourceStart;
        }

        private static Type GetEnumerableElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            foreach (var candidate in type.GetInterfaces())
            {
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return candidate.GetGenericArguments()[0];
            }
            return typeof(object);
        }

        private static IEnumerable<string> GetEntityPaths(Node left, Node right)
        {
            var pending = new Stack<Node>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (left != null) pending.Push(left);
            if (right != null) pending.Push(right);

            while (pending.Count > 0)
            {
                var node = pending.Pop();
                switch (node)
                {
                    case Property property:
                        paths.Add(property.Name);
                        break;
                    case Comparison comparison:
                        if (comparison.Left != null) pending.Push(comparison.Left);
                        if (comparison.Right != null) pending.Push(comparison.Right);
                        break;
                    case Arithmetic arithmetic:
                        if (arithmetic.Left != null) pending.Push(arithmetic.Left);
                        if (arithmetic.Right != null) pending.Push(arithmetic.Right);
                        break;
                    case Group group:
                        if (group.Node != null) pending.Push(group.Node);
                        break;
                    case Not not:
                        if (not.Node != null) pending.Push(not.Node);
                        break;
                }
            }

            return paths;
        }

        public bool IsValidProperty(string value)
        {
            if (value == null) return false;
            if (Schema?.IsPublicSchema == true)
            {
                if (Schema.TryResolveQueryField(value, out var field) &&
                    field.CanFilter &&
                    (QueryContext == null || QueryContext.IsPropertyQueryable(value)))
                    return true;
                InvalidProperties.Add(value);
                return false;
            }
            return _evaluateValidProperty(value);
        }

        public bool IsValidOperator(string value)
        {
            var isValid = value != null && Interpreter.ComparisonOperators.Contains(Interpreter.NormalizeComparisonOperator(value), StringComparer.OrdinalIgnoreCase);
            if (!isValid) InvalidOperators.Add(value ?? "null");

            return isValid;
        }

        public bool IsValidOrderByDirection(string value)
        {
            var isValid = value != null && OrderByParser.Options.Contains(value, StringComparer.OrdinalIgnoreCase);
            if (!isValid) InvalidOrderByDirections.Add(value ?? "null");

            return isValid;
        }

        private bool IsExactValidProperty(string prop)
        {
            var valid = _lookup.ContainsKey(prop);
            if (!valid) InvalidProperties.Add(prop);

            return valid;
        }

        private bool TryEvaluateValidProperty(string value)
        {
            if (value == null) return false;
            value = MapProperty(value);
            var isValid = _lookup.ContainsKey(value) || _nestedProperties?.Contains(value) == true;
            if (isValid) return true;

            if (value.Contains('.') && _evaluationType != null)
            {
                return TryEvaluateNestedProperty(value);
            }

            return false;
        }

        private bool TryEvaluateNestedProperty(string value)
        {
            _nestedProperties = _nestedProperties ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var segments = value.Split('.');
            Type type = null;
            var parentProp = string.Empty;
            var param = Expression.Parameter(_evaluationType);
            MemberExpression propertyExpression = null;
            foreach (var segment in segments)
            {
                try
                {
                    if (type is null)
                    {
                        propertyExpression = Expression.PropertyOrField(param, segment);
                        type = GetMemberType(propertyExpression.Member);
                        parentProp = segment;
                    }
                    else
                    {
                        propertyExpression = Expression.PropertyOrField(propertyExpression, segment);
                        type = GetMemberType(propertyExpression.Member);
                        parentProp = $"{parentProp}.{segment}";
                        _nestedProperties.Add(parentProp);
                    }
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            return _nestedProperties.Contains(value);
        }

        private void ImportValidProperties()
        {
            _lookup = PropertyMetadata.GetPropertyNames(_evaluationType);
        }

        private static Type GetMemberType(MemberInfo memberInfo)
        {
            switch (memberInfo)
            {
                case FieldInfo fi:
                    return fi.FieldType;
                case PropertyInfo pi:
                    return pi.PropertyType;
                default:
                    return null;
            }
        }

        private static string EmptyMap(string prop)
        {
            return prop;
        }

        private string PartialMap(string prop)
        {
            if (_mapping.TryGetValue(prop, out var mapped))
            {
                return mapped;
            }

            return prop;
        }
    }
}
