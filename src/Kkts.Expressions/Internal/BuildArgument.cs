using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

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
            return _evaluateMapping(name);
        }

        public bool IsValidProperty(string value)
        {
            if (value == null) return false;
            return _evaluateValidProperty(value);
        }

        public bool IsValidOperator(string value)
        {
            var isValid = value != null && Interpreter.ComparisonOperators.Contains(value, StringComparer.OrdinalIgnoreCase);
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
