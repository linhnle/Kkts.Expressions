using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Expressions
{
    internal class BuildArgument
    {
        private IDictionary<string, string> _lookup;
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
            set
            {
                if (value == null || !value.Any())
                {
                    _evaluateValidProperty = TryEvaluateValidProperty;
                }
                else
                {
                    _lookup = value?.ToDictionary(k => k, StringComparer.OrdinalIgnoreCase);
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
                if (_lookup?.Any() == true) return;
                ImportValidProperties();
                _evaluateValidProperty = TryEvaluateValidProperty;
            }
        }

        public readonly ICollection<string> InvalidProperties = new List<string>();

        public readonly ICollection<string> InvalidOperators = new List<string>();

        public readonly ICollection<string> InvalidVariables = new List<string>();

        public readonly ICollection<string> InvalidValues = new List<string>();

        public readonly ICollection<string> InvalidOrderByDirections = new List<string>();

        public IDictionary<string, string> PropertyMapping
        {
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
            var isValid = value != null && Interpreter.ComparisonOperators.Contains(value.ToLower());
            if (!isValid) InvalidOperators.Add(value ?? "null");

            return isValid;
        }

        public bool IsValidOrderByDirection(string value)
        {
            var isValid = value != null && OrderByParser.Options.Contains(value.ToLower());
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
            var isValid = _lookup.ContainsKey(value);
            if (isValid) return true;

            if (!isValid && value.Contains('.') && _evaluationType != null)
            {
                _lookup = _lookup ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var segments = value.Split('.');
                Type type = null;
                var parentProp = string.Empty;
                var hasException = false;
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
                            if (_lookup.ContainsKey(parentProp)) continue;
                            _lookup[parentProp] = parentProp;
                        }
                    }
                    catch (Exception)
                    {
                        hasException = true;
                        break;
                    }
                }

                if (!hasException)
                {
                    isValid = _lookup?.ContainsKey(value.ToLower()) == true;
                }
            }

            if (!isValid) InvalidProperties.Add(value);

            return isValid;
        }

        private void ImportValidProperties()
        {
            _lookup = _evaluationType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p is PropertyInfo pi && pi.CanRead || p is FieldInfo)
                .ToDictionary(k => k.Name, v => v.Name, StringComparer.OrdinalIgnoreCase);
        }

        private Type GetMemberType(MemberInfo memberInfo)
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

        private string EmptyMap(string prop)
        {
            return prop;
        }

        private string PartialMap(string prop)
        {
            if (_mapping.ContainsKey(prop))
            {
                return _mapping[prop];
            }

            return prop;
        }
    }
}
