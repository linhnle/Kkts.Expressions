using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Kkts.Expressions
{
    internal class BuildArgument
    {
        private IDictionary<string, string> _lookup;
        private IEnumerable<string> _validProperties;
        private Type _evaluationType;

        public VariableResolver VariableResolver { get; set; } = new VariableResolver();

        public IEnumerable<string> ValidProperties
        {
            get => _validProperties;
            set
            {
                _validProperties = value;
                _lookup = value?.ToDictionary(k => k?.ToLower());
            }
        }

        public Type EvaluationType
        {
            get => _evaluationType;
            set
            {
                _evaluationType = value;
                if (_validProperties?.Any() == true) return;
                ImportValidProperties();
            }
        }

        public ICollection<string> InvalidProperties { get; private set; } = new List<string>();
        
        public ICollection<string> InvalidOperators { get; private set; } = new List<string>();

        public ICollection<string> InvalidVariables { get; private set; } = new List<string>();

        public ICollection<string> InvalidValues { get; private set; } = new List<string>();

        public ICollection<string> InvalidOrderByDirections { get; private set; } = new List<string>();

        public bool IsValidProperty(string value)
        {
            if (value == null) return false;

            var isValid = _lookup?.ContainsKey(value.ToLower()) == true;
            if (!isValid && _validProperties?.Any() == true && value.Contains('.') && _evaluationType != null)
            {
                var segments = value.Split('.');
                Type type = null;
                var parentProp = string.Empty;
                foreach(var segment in segments)
                {
                    if (type is null)
                    {
                        type = _evaluationType.GetProperty(segment)?.PropertyType;
                        parentProp = segment;
                        if (type is null) break;
                    }
                    else
                    {
                        var hasValidProp = false;
                        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead).Select(p => p.Name);
                        foreach(var prop in props)
                        {
                            var nestedProp = $"{parentProp}.{prop}".ToLower();
                            _lookup[nestedProp] = nestedProp;
                            if (prop == segment) hasValidProp = true;
                        }

                        if (!hasValidProp) break;

                        type = type.GetProperty(segment).PropertyType;
                    }
                }

                isValid = _lookup?.ContainsKey(value.ToLower()) == true;
            }
            
            if (!isValid) InvalidProperties.Add(value);

            return isValid;
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

        private void ImportValidProperties()
        {
            ValidProperties = _evaluationType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead).Select(p => p.Name);
        }
    }
}
