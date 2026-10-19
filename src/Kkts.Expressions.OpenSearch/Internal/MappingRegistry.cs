using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>Collects builder registrations and validates them as a whole when the mapping is built.</summary>
    internal sealed class MappingRegistry
    {
        public MappingRegistry(Type rootType)
        {
            RootType = rootType;
        }

        public Type RootType { get; }

        public bool IsBuilt { get; private set; }

        public JsonNamingPolicy NamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

        public StringFieldKind DefaultStringField { get; set; } = StringFieldKind.Default;

        public EnumFormat EnumFormat { get; set; } = EnumFormat.Number;

        public int MaxResultWindow { get; set; } = 10000;

        public Dictionary<MemberChain, FieldRegistration> Fields { get; } = new Dictionary<MemberChain, FieldRegistration>();

        public List<MemberChain> Nested { get; } = new List<MemberChain>();

        public Dictionary<MemberChain, string> Presence { get; } = new Dictionary<MemberChain, string>();

        public Dictionary<Type, Func<object, JsonNode>> Converters { get; } = new Dictionary<Type, Func<object, JsonNode>>();

        public void EnsureNotBuilt()
        {
            if (IsBuilt) throw new InvalidOperationException("This mapping builder has already been built and can no longer be changed.");
        }

        public void AddField(MemberChain chain, FieldRegistration registration)
        {
            EnsureNotBuilt();
            if (Fields.ContainsKey(chain))
                throw new ArgumentException($"Field '{chain}' is already configured.", "selector");
            Fields.Add(chain, registration);
        }

        public void AddNested(MemberChain chain)
        {
            EnsureNotBuilt();
            var type = chain.MemberType;
            var element = TypeClassifier.GetCollectionElementType(type);
            var objectType = element ?? type;
            if (!TypeClassifier.IsOrdinaryObject(objectType, Converters.Keys))
                throw new ArgumentException($"'{chain}' cannot be nested: nested paths must be objects or collections of objects.", "selector");
            if (Nested.Contains(chain))
                throw new ArgumentException($"'{chain}' is already declared as nested.", "selector");
            Nested.Add(chain);
        }

        public void AddPresence(MemberChain chain, string markerPath)
        {
            EnsureNotBuilt();
            if (!TypeClassifier.IsOrdinaryObject(chain.MemberType, Converters.Keys))
                throw new ArgumentException($"'{chain}' cannot have a presence marker: markers apply only to object members, not scalars or collections.", "selector");
            if (!FieldPaths.IsValidPath(markerPath))
                throw new ArgumentException("A presence-marker path must be a non-empty dotted path without empty segments or whitespace.", nameof(markerPath));
            if (Presence.ContainsKey(chain))
                throw new ArgumentException($"'{chain}' already has a presence marker.", "selector");
            Presence.Add(chain, markerPath);
        }

        public void AddConverter(Type type, Func<object, JsonNode> converter)
        {
            EnsureNotBuilt();
            if (Converters.ContainsKey(type))
                throw new ArgumentException($"A converter for '{type}' is already configured.", nameof(converter));
            Converters.Add(type, converter);
        }

        public void MarkBuilt() => IsBuilt = true;

        public void Validate(Func<MemberChain, string> resolvePath)
        {
            var overridden = Fields.Where(f => f.Value.Name != null).ToList();
            var duplicatePath = overridden.GroupBy(f => f.Value.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicatePath != null)
                throw new InvalidOperationException($"Fields {string.Join(" and ", duplicatePath.Select(f => "'" + f.Key + "'"))} are both mapped to '{duplicatePath.Key}'.");

            foreach (var nested in Nested)
            {
                var nestedPath = resolvePath(nested);
                foreach (var field in overridden)
                {
                    if (field.Key.Equals(nested)) continue;
                    var inside = field.Key.StartsWith(nested);
                    var path = resolvePath(field.Key);
                    if (inside != FieldPaths.IsUnder(path, nestedPath))
                    {
                        throw new InvalidOperationException(inside
                            ? $"Field '{field.Key}' is inside nested path '{nestedPath}' but is mapped to '{path}'."
                            : $"Field '{field.Key}' is not inside nested member '{nested}' but is mapped under its path '{nestedPath}'.");
                    }
                }
            }

            foreach (var presence in Presence)
            {
                var chain = presence.Key;
                if (Nested.Contains(chain))
                    throw new InvalidOperationException($"'{chain}' is declared as nested and cannot also have a presence marker; nested objects use nested queries for null checks.");
                for (var i = 1; i < chain.Count; i++)
                {
                    var prefix = chain.Prefix(i);
                    if (TypeClassifier.IsCollection(prefix.MemberType) && !Nested.Contains(prefix))
                        throw new InvalidOperationException($"'{chain}' is inside non-nested collection '{prefix}'; presence markers require a nested scope.");
                }

                var markerPath = presence.Value;
                var enclosing = Nested.Where(n => chain.StartsWith(n) && !chain.Equals(n)).OrderBy(n => n.Count).LastOrDefault();
                if (enclosing != null && !FieldPaths.IsUnder(markerPath, resolvePath(enclosing)))
                    throw new InvalidOperationException($"Presence marker '{markerPath}' for '{chain}' must be inside its enclosing nested path '{resolvePath(enclosing)}'.");
                foreach (var nested in Nested)
                {
                    if (chain.StartsWith(nested)) continue;
                    var nestedPath = resolvePath(nested);
                    if (markerPath == nestedPath || FieldPaths.IsUnder(markerPath, nestedPath))
                        throw new InvalidOperationException($"Presence marker '{markerPath}' for '{chain}' is inside nested path '{nestedPath}', which does not enclose the object.");
                }
            }

            var duplicateMarker = Presence.GroupBy(p => p.Value, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicateMarker != null)
                throw new InvalidOperationException($"Presence marker '{duplicateMarker.Key}' is registered for more than one object.");
        }
    }
}
