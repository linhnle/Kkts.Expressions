using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kkts.Expressions.OpenSearch.Internal;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>
    /// Immutable description of how a CLR type is indexed in OpenSearch. Create with
    /// <see cref="For{T}"/> or <see cref="OpenSearchMapping{T}.Create"/>.
    /// </summary>
    public abstract class OpenSearchMapping
    {
        private readonly MappingRegistry _registry;
        private readonly ConcurrentDictionary<MemberChain, ResolvedMember> _cache = new ConcurrentDictionary<MemberChain, ResolvedMember>();
        private readonly HashSet<MemberChain> _nested;

        internal OpenSearchMapping(MappingRegistry registry)
        {
            _registry = registry;
            _nested = new HashSet<MemberChain>(registry.Nested);
            registry.Validate(ResolvePath);
            registry.MarkBuilt();
        }

        public Type ElementType => _registry.RootType;

        /// <summary>The naming policy applied to CLR member names; <see langword="null"/> keeps CLR names.</summary>
        public JsonNamingPolicy NamingPolicy => _registry.NamingPolicy;

        public StringFieldKind DefaultStringField => _registry.DefaultStringField;

        public EnumFormat EnumFormat => _registry.EnumFormat;

        public int MaxResultWindow => _registry.MaxResultWindow;

        internal int CachedMemberCount => _cache.Count;

        public static OpenSearchMappingBuilder<T> For<T>() => new OpenSearchMappingBuilder<T>();

        /// <summary>Creates a mapping with default settings for <paramref name="elementType"/>.</summary>
        public static OpenSearchMapping CreateDefault(Type elementType)
        {
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            var mappingType = typeof(OpenSearchMapping<>).MakeGenericType(elementType);
            return (OpenSearchMapping)mappingType.GetProperty(nameof(OpenSearchMapping<object>.Default), BindingFlags.Public | BindingFlags.Static).GetValue(null);
        }

        internal ResolvedMember Resolve(MemberChain chain) => _cache.GetOrAdd(chain, CreateResolvedMember);

        internal bool IsNested(MemberChain chain) => _nested.Contains(chain);

        internal string ResolvePath(MemberChain chain)
        {
            for (var length = chain.Count; length > 0; length--)
            {
                var prefix = chain.Prefix(length);
                if (_registry.Fields.TryGetValue(prefix, out var registration) && registration.Name != null)
                {
                    var rest = chain.Members.Skip(length).Select(GetSegmentName);
                    return string.Join(".", new[] { registration.Name }.Concat(rest));
                }
            }

            return string.Join(".", chain.Members.Select(GetSegmentName));
        }

        private string GetSegmentName(MemberInfo member)
        {
            var attribute = member.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attribute != null) return attribute.Name;
            return NamingPolicy == null ? member.Name : NamingPolicy.ConvertName(member.Name);
        }

        private ResolvedMember CreateResolvedMember(MemberChain chain)
        {
            _registry.Fields.TryGetValue(chain, out var registration);
            var memberType = chain.MemberType;
            var elementType = TypeClassifier.GetCollectionElementType(memberType);
            var valueType = TypeClassifier.Underlying(elementType ?? memberType);
            var converterTypes = _registry.Converters.Keys;
            var isScalar = TypeClassifier.IsScalar(valueType, converterTypes);
            var isStringLike = TypeClassifier.IsStringLike(valueType);
            var path = ResolvePath(chain);
            var stringKind = isStringLike ? registration?.StringKind ?? DefaultStringField : null;

            Func<object, JsonNode> converter = registration?.Converter;
            if (converter == null) _registry.Converters.TryGetValue(valueType, out converter);

            return new ResolvedMember
            {
                Chain = chain,
                Path = path,
                ExactPath = isStringLike ? stringKind.GetExactPath(path) : path,
                MemberType = memberType,
                IsCollection = elementType != null,
                ValueType = valueType,
                IsScalarValue = isScalar,
                IsOrdinaryObject = elementType == null && !isScalar,
                IsStringLike = isStringLike,
                IsNested = _nested.Contains(chain),
                PresencePath = _registry.Presence.TryGetValue(chain, out var marker) ? marker : null,
                EnclosingNested = _registry.Nested
                    .Where(n => chain.StartsWith(n) && !chain.Equals(n))
                    .OrderBy(n => n.Count)
                    .Select(n => new NestedScope(n, ResolvePath(n)))
                    .ToArray(),
                CaseInsensitive = registration?.CaseInsensitive ?? false,
                EnumFormat = registration?.EnumFormat ?? EnumFormat,
                Converter = converter,
                StringKind = stringKind
            };
        }
    }

    /// <summary>Immutable OpenSearch mapping for <typeparamref name="T"/>.</summary>
    public sealed class OpenSearchMapping<T> : OpenSearchMapping
    {
        private static readonly Lazy<OpenSearchMapping<T>> DefaultMapping =
            new Lazy<OpenSearchMapping<T>>(() => new OpenSearchMappingBuilder<T>().Build());

        internal OpenSearchMapping(MappingRegistry registry)
            : base(registry)
        {
        }

        /// <summary>camelCase names, text + <c>keyword</c> strings, numeric enums, and a 10,000 result window.</summary>
        public static OpenSearchMapping<T> Default => DefaultMapping.Value;

        public static OpenSearchMapping<T> Create(Action<OpenSearchMappingBuilder<T>> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var builder = new OpenSearchMappingBuilder<T>();
            configure(builder);
            return builder.Build();
        }
    }
}
