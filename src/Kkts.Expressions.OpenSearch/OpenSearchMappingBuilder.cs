using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Internal;

namespace Kkts.Expressions.OpenSearch
{
    /// <summary>Fluent builder for <see cref="OpenSearchMapping{T}"/>. A builder can be built once.</summary>
    public sealed class OpenSearchMappingBuilder<T>
    {
        private readonly MappingRegistry _registry = new MappingRegistry(typeof(T));

        internal OpenSearchMappingBuilder()
        {
        }

        /// <summary>Sets the naming policy for CLR member names; <see langword="null"/> keeps CLR names. Default: camelCase.</summary>
        public OpenSearchMappingBuilder<T> NamingPolicy(JsonNamingPolicy policy)
        {
            _registry.EnsureNotBuilt();
            _registry.NamingPolicy = policy;
            return this;
        }

        /// <summary>Sets the default string mapping. Default: text with a <c>keyword</c> subfield.</summary>
        public OpenSearchMappingBuilder<T> DefaultStringField(StringFieldKind kind)
        {
            _registry.EnsureNotBuilt();
            _registry.DefaultStringField = kind ?? throw new ArgumentNullException(nameof(kind));
            return this;
        }

        public OpenSearchMappingBuilder<T> EnumFormat(EnumFormat format)
        {
            _registry.EnsureNotBuilt();
            _registry.EnumFormat = format;
            return this;
        }

        /// <summary>Formats values of <typeparamref name="TValue"/>, such as <see cref="TimeSpan"/>, for every field of that type.</summary>
        public OpenSearchMappingBuilder<T> Converter<TValue>(Func<TValue, JsonNode> converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            var type = TypeClassifier.Underlying(typeof(TValue));
            if (type != typeof(TValue))
                throw new ArgumentException("Register converters for the non-nullable type; they also apply to nullable members.", nameof(converter));
            _registry.AddConverter(type, value => converter((TValue)value));
            return this;
        }

        /// <summary>The OpenSearch <c>index.max_result_window</c>. Default: 10,000.</summary>
        public OpenSearchMappingBuilder<T> MaxResultWindow(int maxResultWindow)
        {
            if (maxResultWindow <= 0) throw new ArgumentOutOfRangeException(nameof(maxResultWindow), "The maximum result window must be positive.");
            _registry.EnsureNotBuilt();
            _registry.MaxResultWindow = maxResultWindow;
            return this;
        }

        public OpenSearchMappingBuilder<T> Field<TField>(Expression<Func<T, TField>> selector, Action<OpenSearchFieldBuilder> configure)
        {
            BuilderOperations.Field(_registry, MemberChain.Empty, selector, configure);
            return this;
        }

        /// <summary>Marks an object or collection member as an OpenSearch <c>nested</c> path.</summary>
        public OpenSearchMappingBuilder<T> Nested<TMember>(Expression<Func<T, TMember>> selector)
        {
            BuilderOperations.Nested(_registry, MemberChain.Empty, selector);
            return this;
        }

        /// <summary>Marks a collection as <c>nested</c> and configures its element members.</summary>
        public OpenSearchMappingBuilder<T> Nested<TElement>(Expression<Func<T, IEnumerable<TElement>>> selector, Action<OpenSearchNestedBuilder<TElement>> configure)
        {
            BuilderOperations.Nested(_registry, MemberChain.Empty, selector, configure);
            return this;
        }

        /// <summary>Marks a single object as <c>nested</c> and configures its members.</summary>
        public OpenSearchMappingBuilder<T> Nested<TObject>(Expression<Func<T, TObject>> selector, Action<OpenSearchNestedBuilder<TObject>> configure)
        {
            BuilderOperations.Nested(_registry, MemberChain.Empty, selector, configure);
            return this;
        }

        /// <summary>
        /// Registers an indexed Boolean field that is <see langword="true"/> when the object is non-null and
        /// <see langword="false"/> when it is null. The application must index it on every document.
        /// </summary>
        public OpenSearchMappingBuilder<T> Presence<TObject>(Expression<Func<T, TObject>> selector, string markerPath) where TObject : class
        {
            BuilderOperations.Presence(_registry, MemberChain.Empty, selector, markerPath);
            return this;
        }

        public OpenSearchMapping<T> Build()
        {
            _registry.EnsureNotBuilt();
            return new OpenSearchMapping<T>(_registry);
        }
    }

    /// <summary>Configures members inside a nested object or collection element.</summary>
    public sealed class OpenSearchNestedBuilder<TElement>
    {
        private readonly MappingRegistry _registry;
        private readonly MemberChain _prefix;

        internal OpenSearchNestedBuilder(MappingRegistry registry, MemberChain prefix)
        {
            _registry = registry;
            _prefix = prefix;
        }

        public OpenSearchNestedBuilder<TElement> Field<TField>(Expression<Func<TElement, TField>> selector, Action<OpenSearchFieldBuilder> configure)
        {
            BuilderOperations.Field(_registry, _prefix, selector, configure);
            return this;
        }

        public OpenSearchNestedBuilder<TElement> Nested<TMember>(Expression<Func<TElement, TMember>> selector)
        {
            BuilderOperations.Nested(_registry, _prefix, selector);
            return this;
        }

        public OpenSearchNestedBuilder<TElement> Nested<TInner>(Expression<Func<TElement, IEnumerable<TInner>>> selector, Action<OpenSearchNestedBuilder<TInner>> configure)
        {
            BuilderOperations.Nested(_registry, _prefix, selector, configure);
            return this;
        }

        public OpenSearchNestedBuilder<TElement> Nested<TObject>(Expression<Func<TElement, TObject>> selector, Action<OpenSearchNestedBuilder<TObject>> configure)
        {
            BuilderOperations.Nested(_registry, _prefix, selector, configure);
            return this;
        }

        /// <summary>Registers a presence marker; <paramref name="markerPath"/> is the full path and must lie inside the enclosing nested path.</summary>
        public OpenSearchNestedBuilder<TElement> Presence<TObject>(Expression<Func<TElement, TObject>> selector, string markerPath) where TObject : class
        {
            BuilderOperations.Presence(_registry, _prefix, selector, markerPath);
            return this;
        }
    }

    internal static class BuilderOperations
    {
        public static void Field(MappingRegistry registry, MemberChain prefix, LambdaExpression selector, Action<OpenSearchFieldBuilder> configure)
        {
            registry.EnsureNotBuilt();
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var chain = prefix.Concat(SelectorParser.Parse(selector, nameof(selector)));
            var memberType = chain.MemberType;
            var valueType = TypeClassifier.Underlying(TypeClassifier.GetCollectionElementType(memberType) ?? memberType);
            var builder = new OpenSearchFieldBuilder(chain, valueType, TypeClassifier.IsScalar(valueType, registry.Converters.Keys));
            configure(builder);
            registry.AddField(chain, builder.Registration);
        }

        public static void Nested(MappingRegistry registry, MemberChain prefix, LambdaExpression selector)
        {
            registry.EnsureNotBuilt();
            registry.AddNested(prefix.Concat(SelectorParser.Parse(selector, nameof(selector))));
        }

        public static void Nested<TScope>(MappingRegistry registry, MemberChain prefix, LambdaExpression selector, Action<OpenSearchNestedBuilder<TScope>> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            registry.EnsureNotBuilt();
            var chain = prefix.Concat(SelectorParser.Parse(selector, nameof(selector)));
            registry.AddNested(chain);
            configure(new OpenSearchNestedBuilder<TScope>(registry, chain));
        }

        public static void Presence(MappingRegistry registry, MemberChain prefix, LambdaExpression selector, string markerPath)
        {
            registry.EnsureNotBuilt();
            registry.AddPresence(prefix.Concat(SelectorParser.Parse(selector, nameof(selector))), markerPath);
        }
    }
}
