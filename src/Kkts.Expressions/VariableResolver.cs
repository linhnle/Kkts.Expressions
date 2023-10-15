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
        private static string _variablePrefix = "$";
        public DateTime Now => _now ?? (_now = DateTime.Now).Value;
        public DateTime UtcNow => _utcNow ?? (_utcNow = DateTime.UtcNow).Value;

        internal protected static string VariablePrefixString => _variablePrefix;

        public static char VariablePrefix
        {
            get => _variablePrefix[0];
            set
            {
                if (value == '\0')
                {
                    throw new InvalidOperationException("Variable Prefix must have a value");
                }

                _variablePrefix = value.ToString();
            }
        }

        /// <summary>
        /// If true, the variable must have prefix $ (example: '$now' instead of 'now'), otherwise it is a property
        /// </summary>
        internal protected bool UseVariableQuery { get; set; } = true;

        private static readonly TaskFactory _syncTaskFactory = new TaskFactory(CancellationToken.None,
                  TaskCreationOptions.None,
                  TaskContinuationOptions.None,
                  TaskScheduler.Default);


        [Obsolete]
        public virtual bool IsVariable(string name)
        {
            if (name == null) return false;
            if (name.StartsWith(_variablePrefix))
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

        public bool TryResolve(string name, out object value)
        {
            var result = RunSync(() => TryResolve(name, CancellationToken.None));
            value = result.Resolved ? result.Value : null;

            return result.Resolved;
        }

        public Task<VariableInfo> TryResolveAsync(string name, CancellationToken cancellationToken = default)
        {
            return TryResolve(name, cancellationToken);
        }

        public virtual bool TryAdd(string variableName, object value)
        {
            if (variableName is null) throw new ArgumentNullException(nameof(variableName));

            return _cache.TryAdd(variableName, value);
        }

        public virtual bool TryRemove(string variableName)
        {
            if (variableName is null) throw new ArgumentNullException(nameof(variableName));

            return _cache.TryRemove(variableName, out var _);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public virtual Task InitializeVariablesAsync(object state)
        {
            return Task.CompletedTask;
        }

        protected virtual Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
        {
            return Task.FromResult(new VariableInfo { Name = name });
        }

        private async Task<VariableInfo> TryResolve(string name, CancellationToken cancellationToken)
        {
            if (name == null)
            {
                return new VariableInfo { Name = name };
            }

            if (name.StartsWith(_variablePrefix))
            {
                name = name.Substring(1);
            }

            if (_cache.TryGetValue(name, out var value))
            {
                return new VariableInfo { Name = name, Resolved = true, Value = value };
            }

            var segments = name.Split('.');
            var lookup = GetVariables();
            var segmentName = segments[0];
            if (lookup.ContainsKey(segmentName))
            {
                var prop = lookup[segmentName];
                var tmp = prop.GetValue(this);

                try
                {
                    for (var i = 1; i < segments.Length; ++i)
                    {
                        if (tmp is null)
                        {
                            return new VariableInfo { Name = name };
                        }

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
                    return new VariableInfo { Name = name };
                }

                value = tmp;
                _cache.TryAdd(name, value);

                return new VariableInfo { Name = name, Resolved = true, Value = value };
            }

            var varInfo = await TryResolveCore(name, cancellationToken);
            if (varInfo?.Resolved == true)
            {
                _cache.TryAdd(name, varInfo.Value);
            }

            return varInfo;
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

        /// <summary>
        /// Refer to: https://github.com/aspnet/AspNetIdentity/blob/master/src/Microsoft.AspNet.Identity.Core/AsyncHelper.cs
        /// </summary>
        private static TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            return _syncTaskFactory
              .StartNew(func)
              .Unwrap()
              .GetAwaiter()
              .GetResult();
        }

    }
}
