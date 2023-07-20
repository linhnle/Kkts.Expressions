using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions
{
    public class VariableResolver
    {
        private DateTime? _now;
        private DateTime? _utcNow;
        private IDictionary<string, PropertyInfo> _dictionary;
        private readonly ConcurrentDictionary<string, object> _cache = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public DateTime Now => _now ?? (_now = DateTime.Now).Value;
        public DateTime UtcNow => _utcNow ?? (_utcNow = DateTime.UtcNow).Value;

        /// <summary>
        /// If true, the variable must have prefix $ (example: '$now' instead of 'now'), otherwise it is a property
        /// </summary>
        internal protected bool UseVariableQuery { get; set; } = false;

        public virtual bool IsVariable(string name)
        {
            if (name == null) return false;
            if (name.StartsWith("$"))
            {
                return true;
            }

            if (UseVariableQuery)
            {
                return false;
            }

            var segments = name.Split('.');
            var lookup = GetVariables();

            return lookup.ContainsKey(segments[0]);
        }

        public virtual Task<bool> IsVariableAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsVariable(name));
        }

        public virtual bool TryResolve(string name, out object value)
        {
            if (name == null)
            {
                value = null;
                return false;
            }

            if (name.StartsWith("$"))
            {
                name = name.Substring(1);
            }

            if (_cache.TryGetValue(name, out value))
            {
                return true;
            }

            var segments = name.Split('.');
            var lookup = GetVariables();
            var segmentName = segments[0];
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
                _cache.TryAdd(name, value);

                return true;
            }

            value = null;

            return false;
        }

        public virtual Task<VariableInfo> ResolveAsync(string name, CancellationToken cancellationToken = default)
        {
            var status = TryResolve(name, out var value);

            return Task.FromResult(new VariableInfo
            {
                Name = name,
                Value = value,
                Resolved = status
            });
        }

        public virtual Task InitializeVariablesAsync(object state)
        {
            return Task.CompletedTask;
        }

        public virtual bool TryAddVariable(string name, object value)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            return _cache.TryAdd(name, value);
        }

        public virtual bool TryRemoveVariable(string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            return _cache.TryRemove(name, out var _);
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        private IDictionary<string, PropertyInfo> GetVariables()
        {
            return _dictionary is null ?
                _dictionary = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase)
                : _dictionary;
        }
    }
}
