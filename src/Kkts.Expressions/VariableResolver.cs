using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Kkts.Expressions
{
    public class VariableResolver
    {
        private DateTime? _now;
        private DateTime? _utcNow;
        private IDictionary<string, PropertyInfo> _dictionary;
        private readonly ConcurrentDictionary<string, object> _cache = new ConcurrentDictionary<string, object>();

        public DateTime Now => _now ?? (_now = DateTime.Now).Value;
        public DateTime UtcNow => _utcNow ?? (_utcNow = DateTime.UtcNow).Value;

        public virtual bool IsVariable(string name)
        {
            if (name == null) return false;
            var segments = name.Split('.');
            var lookup = GetVariables();

            return lookup.ContainsKey(segments[0].ToLower());
        }

        public virtual bool TryResolve(string name, out object value)
        {
            if (name == null)
            {
                value = null;
                return false;
            }

            if (_cache.TryGetValue(name.ToLower(), out value))
            {
                return true;
            }

            var segments = name.Split('.');
            var lookup = GetVariables();
            var segmentName = segments[0].ToLower();
            if (lookup.ContainsKey(segmentName))
            {
                var prop = lookup[segmentName];
                var tmp = prop.GetValue(this);
                value = null;
                try
                {
                    for (var i = 1; i < segments.Length; ++i)
                    {
                        var member = Expression.PropertyOrField(Expression.Parameter(prop.PropertyType), segments[i]).Member;
                        switch (member)
                        {
                            case PropertyInfo p:
                                tmp = p.GetValue(tmp);
                                break;
                            case FieldInfo f:
                                tmp = f.GetValue(tmp);
                                break;
                        }
                    }
                }
                catch (Exception)
                {
                    return false;
                }

                value = tmp;
                _cache.TryAdd(name.ToLower(), value);

                return true;
            }

            value = null;

            return false;
        }

        public virtual Task InitializeVariablesAsync(object state)
        {
            return Task.CompletedTask;
        }

        private IDictionary<string, PropertyInfo> GetVariables()
        {
            return _dictionary is null ?
                _dictionary = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToDictionary(k => k.Name.ToLower())
                : _dictionary;
        }
    }
}
