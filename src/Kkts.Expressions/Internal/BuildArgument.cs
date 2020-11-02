using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Kkts.Expressions
{
    internal class BuildArgument
    {
        private ILookup<string, string> _lookup;
        private IEnumerable<string> _validProperties;
        private Type _evaluationType;

        public VariableResolver VariableResolver { get; set; } = new VariableResolver();

        public IEnumerable<string> ValidProperties
        {
            get => _validProperties;
            set
            {
                _validProperties = value;
                _lookup = value?.ToLookup(k => k?.ToLower());
            }
        }

        public Type EvaluationType
        {
            get => _evaluationType;
            set
            {
                _evaluationType = value;
                if (_validProperties?.Count() > 0) return;
                ImportValidProperties();
            }
        }

        public ICollection<string> InvalidProperties { get; private set; } = new List<string>();
        
        public ICollection<string> InvalidOperators { get; private set; } = new List<string>();

        public ICollection<string> InvalidVariables { get; private set; } = new List<string>();

        public ICollection<string> InvalidOrderByDirections { get; private set; } = new List<string>();

        public bool IsValidProperty(string value)
        {
            var isValid = _lookup?.Contains(value?.ToLower()) == true;
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
