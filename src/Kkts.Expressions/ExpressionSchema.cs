using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Describes whether an entity member can contain null values.</summary>
    public enum ExpressionNullability
    {
        Unknown,
        NonNullable,
        Nullable
    }

    /// <summary>Optional metadata that overrides nullability and query permission for an entity path.</summary>
    public sealed class ExpressionPropertyDefinition
    {
        public ExpressionPropertyDefinition(
            string path,
            Type clrType,
            ExpressionNullability nullability = ExpressionNullability.Unknown,
            bool canQuery = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A property path is required.", nameof(path));
            if (clrType == null) throw new ArgumentNullException(nameof(clrType));
            if (!Enum.IsDefined(typeof(ExpressionNullability), nullability))
                throw new ArgumentOutOfRangeException(nameof(nullability));

            Path = path;
            ClrType = clrType;
            Nullability = nullability;
            CanQuery = canQuery;
        }

        /// <summary>The canonical CLR member path.</summary>
        public string Path { get; }

        /// <summary>The CLR type of the described member.</summary>
        public Type ClrType { get; }

        /// <summary>The declared nullability, or Unknown when it is not known.</summary>
        public ExpressionNullability Nullability { get; }

        /// <summary>Whether callers may use this member when constructing a query.</summary>
        public bool CanQuery { get; }
    }

    /// <summary>
    /// Immutable entity metadata for expression analysis. Public instance properties and fields
    /// are discovered from the entity type; callers only need to supply selective overrides.
    /// </summary>
    public sealed class ExpressionSchema
    {
        private readonly IReadOnlyList<string> _validProperties;
        private readonly IReadOnlyDictionary<string, string> _propertyMapping;
        private readonly IReadOnlyDictionary<string, ExpressionPropertyDefinition> _definitions;

        private ExpressionSchema(
            Type entityType,
            IEnumerable<string> validProperties,
            IDictionary<string, string> propertyMapping,
            IEnumerable<ExpressionPropertyDefinition> properties,
            ExpressionConversionContext conversionContext)
        {
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            ConversionContext = conversionContext ?? ExpressionConversionContext.Default;
            _validProperties = Array.AsReadOnly((validProperties ?? Enumerable.Empty<string>())
                .Select(value => value == null
                    ? throw new ArgumentException("Allowed property names cannot be null.", nameof(validProperties))
                    : NormalizePath(value))
                .ToArray());

            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (propertyMapping != null)
            {
                foreach (var pair in propertyMapping)
                {
                    var source = NormalizePath(pair.Key);
                    if (string.IsNullOrWhiteSpace(source))
                        throw new ArgumentException("Mapping source names are required.", nameof(propertyMapping));
                    if (string.IsNullOrWhiteSpace(pair.Value))
                        throw new ArgumentException($"Mapping target for '{pair.Key}' is required.", nameof(propertyMapping));
                    if (mapping.ContainsKey(source))
                        throw new ArgumentException($"Duplicate property mapping for '{pair.Key}'.", nameof(propertyMapping));
                    mapping.Add(source, NormalizePath(pair.Value));
                    if (!TryGetClrPath(pair.Value, out _))
                        throw new ArgumentException($"Mapping target '{pair.Value}' is not an entity property path.", nameof(propertyMapping));
                }
            }
            _propertyMapping = new ReadOnlyDictionary<string, string>(mapping);

            var definitions = new Dictionary<string, ExpressionPropertyDefinition>(StringComparer.OrdinalIgnoreCase);
            if (properties != null)
            {
                foreach (var definition in properties)
                {
                    if (definition == null)
                        throw new ArgumentException("Property definitions cannot contain null.", nameof(properties));
                    if (!TryGetClrPath(definition.Path, out var actualType))
                        throw new ArgumentException($"Property path '{definition.Path}' is not present on '{entityType}'.", nameof(properties));
                    if (actualType != definition.ClrType)
                        throw new ArgumentException($"Property path '{definition.Path}' has CLR type '{actualType}', not '{definition.ClrType}'.", nameof(properties));
                    if (definitions.ContainsKey(definition.Path))
                        throw new ArgumentException($"Duplicate property definition for '{definition.Path}'.", nameof(properties));
                    definitions.Add(definition.Path, definition);
                    if (definition.Nullability == ExpressionNullability.Nullable &&
                        actualType.IsValueType && Nullable.GetUnderlyingType(actualType) == null)
                        throw new ArgumentException($"Non-nullable value property '{definition.Path}' cannot be declared nullable.", nameof(properties));
                    if (definition.Nullability == ExpressionNullability.NonNullable &&
                        Nullable.GetUnderlyingType(actualType) != null)
                        throw new ArgumentException($"Nullable value property '{definition.Path}' cannot be declared non-nullable.", nameof(properties));
                }
            }
            _definitions = new ReadOnlyDictionary<string, ExpressionPropertyDefinition>(definitions);
        }

        /// <summary>The entity CLR type described by this schema.</summary>
        public Type EntityType { get; }

        /// <summary>Explicit property metadata overrides, copied into a read-only snapshot.</summary>
        public IReadOnlyDictionary<string, ExpressionPropertyDefinition> Properties => _definitions;

        /// <summary>Allowed source property names, if a legacy-style allowlist was supplied.</summary>
        public IReadOnlyList<string> ValidProperties => _validProperties;

        /// <summary>Exact external-name mappings, copied into a read-only snapshot.</summary>
        public IReadOnlyDictionary<string, string> PropertyMapping => _propertyMapping;

        /// <summary>Culture and date formats used for deterministic literal analysis.</summary>
        public ExpressionConversionContext ConversionContext { get; }

        /// <summary>Creates metadata by reflecting public instance members of <typeparamref name="T"/>.</summary>
        public static ExpressionSchema FromType<T>(
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            IEnumerable<ExpressionPropertyDefinition> properties = null,
            ExpressionConversionContext conversionContext = null)
        {
            return FromType(typeof(T), validProperties, propertyMapping, properties, conversionContext);
        }

        /// <summary>Creates metadata by reflecting public instance members of the supplied type.</summary>
        public static ExpressionSchema FromType(
            Type entityType,
            IEnumerable<string> validProperties = null,
            IDictionary<string, string> propertyMapping = null,
            IEnumerable<ExpressionPropertyDefinition> properties = null,
            ExpressionConversionContext conversionContext = null)
        {
            if (entityType == null) throw new ArgumentNullException(nameof(entityType));
            return new ExpressionSchema(entityType, validProperties, propertyMapping, properties, conversionContext);
        }

        internal bool TryMapProperty(string sourcePath, out string clrPath)
        {
            if (_propertyMapping.TryGetValue(NormalizePath(sourcePath), out clrPath)) return true;
            clrPath = NormalizePath(sourcePath);
            return clrPath.Length > 0;
        }

        internal bool IsAllowedSource(string sourcePath)
        {
            return _validProperties.Count == 0 ||
                _validProperties.Contains(NormalizePath(sourcePath), StringComparer.OrdinalIgnoreCase);
        }

        internal bool IsPropertyQueryable(string sourcePath)
        {
            if (!IsAllowedSource(sourcePath)) return false;
            if (!TryMapProperty(sourcePath, out var clrPath)) return false;

            var segments = NormalizePath(clrPath).Split('.');
            for (var index = 0; index < segments.Length; index++)
            {
                var path = string.Join(".", segments.Take(index + 1));
                if (_definitions.TryGetValue(path, out var definition) && !definition.CanQuery)
                    return false;
            }
            return true;
        }

        internal bool TryGetProperty(string clrPath, out Type clrType, out ExpressionNullability nullability, out bool canQuery)
        {
            if (!TryGetClrPath(clrPath, out clrType))
            {
                nullability = ExpressionNullability.Unknown;
                canQuery = false;
                return false;
            }

            if (_definitions.TryGetValue(NormalizePath(clrPath), out var definition))
            {
                nullability = definition.Nullability == ExpressionNullability.Unknown
                    ? GetDefaultNullability(clrType)
                    : definition.Nullability;
                canQuery = definition.CanQuery;
                return true;
            }

            nullability = GetDefaultNullability(clrType);
            canQuery = true;
            return true;
        }

        internal static string NormalizePath(string path)
        {
            return string.Concat((path ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        }

        private bool TryGetClrPath(string path, out Type memberType)
        {
            memberType = EntityType;
            foreach (var segment in NormalizePath(path).Split('.'))
            {
                if (segment.Length == 0) return false;
                if (!PropertyMetadata.GetMemberTypes(memberType).TryGetValue(segment, out memberType))
                {
                    return false;
                }
            }
            return memberType != null;
        }

        private static ExpressionNullability GetDefaultNullability(Type type)
        {
            if (Nullable.GetUnderlyingType(type) != null) return ExpressionNullability.Nullable;
            if (type.IsValueType) return ExpressionNullability.NonNullable;
            return ExpressionNullability.Unknown;
        }
    }
}
