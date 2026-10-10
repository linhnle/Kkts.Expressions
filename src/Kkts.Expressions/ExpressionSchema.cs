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
        private readonly IReadOnlyDictionary<string, RegisteredExpressionField> _expressionFieldsByName;

        private ExpressionSchema(
            Type entityType,
            IEnumerable<string> validProperties,
            IDictionary<string, string> propertyMapping,
            IEnumerable<ExpressionPropertyDefinition> properties,
            ExpressionConversionContext conversionContext,
            IEnumerable<RegisteredExpressionField> expressionFields = null,
            bool isPublicSchema = false)
        {
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            IsPublicSchema = isPublicSchema;
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
            if (isPublicSchema && mapping.Count != 0)
                throw new ArgumentException(
                    "Public query schemas cannot include legacy property mappings.",
                    nameof(propertyMapping));

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
            if (isPublicSchema && definitions.Count != 0)
                throw new ArgumentException(
                    "Public query schemas cannot include canonical property overrides.",
                    nameof(properties));
            _definitions = new ReadOnlyDictionary<string, ExpressionPropertyDefinition>(definitions);

            var registeredFields = (expressionFields ?? Enumerable.Empty<RegisteredExpressionField>())
                .ToArray();
            var fieldsByName = new Dictionary<string, RegisteredExpressionField>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var field in registeredFields)
            {
                if (field == null)
                    throw new ArgumentException("Registered fields cannot contain null.", nameof(expressionFields));
                if (fieldsByName.ContainsKey(field.Metadata.Name))
                    throw new ArgumentException(
                        $"Duplicate public query field '{field.Metadata.Name}'.",
                        nameof(expressionFields));
                fieldsByName.Add(field.Metadata.Name, field);
            }

            _expressionFieldsByName = new ReadOnlyDictionary<string, RegisteredExpressionField>(
                fieldsByName);
            Fields = Array.AsReadOnly(registeredFields.Select(field => field.Metadata).ToArray());
        }

        /// <summary>The entity CLR type described by this schema.</summary>
        public Type EntityType { get; }

        /// <summary>Whether this schema exposes only explicitly registered public fields.</summary>
        public bool IsPublicSchema { get; }

        /// <summary>
        /// Registered public-field metadata in registration order; empty for reflected schemas.
        /// </summary>
        public IReadOnlyList<ExpressionFieldDefinition> Fields { get; }

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

        internal static ExpressionSchema FromPublicFields(
            Type entityType,
            IEnumerable<RegisteredExpressionField> fields,
            ExpressionConversionContext conversionContext,
            IDictionary<string, string> propertyMapping = null,
            IEnumerable<ExpressionPropertyDefinition> properties = null)
        {
            if (entityType == null) throw new ArgumentNullException(nameof(entityType));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            return new ExpressionSchema(
                entityType,
                null,
                propertyMapping,
                properties,
                conversionContext,
                fields,
                isPublicSchema: true);
        }

        internal bool TryGetExpressionField(string name, out RegisteredExpressionField field)
        {
            return _expressionFieldsByName.TryGetValue(name ?? string.Empty, out field);
        }

        internal bool TryMapProperty(string sourcePath, out string clrPath)
        {
            if (IsPublicSchema)
            {
                if (_expressionFieldsByName.TryGetValue(sourcePath ?? string.Empty, out var field))
                {
                    clrPath = field.Metadata.Name;
                    return true;
                }
                clrPath = null;
                return false;
            }
            if (_propertyMapping.TryGetValue(NormalizePath(sourcePath), out clrPath)) return true;
            clrPath = NormalizePath(sourcePath);
            return clrPath.Length > 0;
        }

        internal bool IsAllowedSource(string sourcePath)
        {
            if (IsPublicSchema)
                return _expressionFieldsByName.ContainsKey(sourcePath ?? string.Empty);
            return _validProperties.Count == 0 ||
                _validProperties.Contains(NormalizePath(sourcePath), StringComparer.OrdinalIgnoreCase);
        }

        internal bool IsPropertyQueryable(string sourcePath)
        {
            if (IsPublicSchema)
                return _expressionFieldsByName.TryGetValue(
                    sourcePath ?? string.Empty,
                    out var registered) && registered.Metadata.CanFilter;
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
            if (IsPublicSchema)
            {
                if (_expressionFieldsByName.TryGetValue(
                        clrPath ?? string.Empty,
                        out var registered))
                {
                    clrType = registered.Metadata.ClrType;
                    nullability = registered.Metadata.Nullability;
                    canQuery = registered.Metadata.CanFilter;
                    return true;
                }
                clrType = null;
                nullability = ExpressionNullability.Unknown;
                canQuery = false;
                return false;
            }
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

        internal bool TryGetPolicyPathInfo(
            string sourcePath,
            out string clrPath,
            out int navigationDepth,
            out bool hasCollectionAccess)
        {
            navigationDepth = 0;
            hasCollectionAccess = false;
            if (IsPublicSchema)
            {
                if (_expressionFieldsByName.TryGetValue(
                        sourcePath ?? string.Empty,
                        out var registered))
                {
                    clrPath = registered.Metadata.Name;
                    return true;
                }
                clrPath = null;
                return false;
            }
            if (!TryMapProperty(sourcePath, out var mappedPath))
            {
                clrPath = null;
                return false;
            }

            var type = EntityType;
            var canonicalSegments = new List<string>();
            foreach (var segment in NormalizePath(mappedPath).Split('.'))
            {
                var members = PropertyMetadata.GetMemberTypes(type);
                var member = members.FirstOrDefault(pair =>
                    string.Equals(pair.Key, segment, StringComparison.OrdinalIgnoreCase));
                if (member.Key == null)
                {
                    clrPath = null;
                    return false;
                }

                type = member.Value;
                canonicalSegments.Add(member.Key);
                if (QueryPolicyFieldMetadata.IsCollection(type))
                {
                    hasCollectionAccess = true;
                    ++navigationDepth;
                }
                else if (!QueryPolicyFieldMetadata.IsScalar(type))
                {
                    ++navigationDepth;
                }
            }

            clrPath = string.Join(".", canonicalSegments);
            return canonicalSegments.Count > 0;
        }

        internal bool TryResolveQueryField(
            string sourcePath,
            out ResolvedQueryField field)
        {
            field = null;
            if (IsPublicSchema)
            {
                if (!_expressionFieldsByName.TryGetValue(
                        sourcePath ?? string.Empty,
                        out var registered))
                    return false;

                var metadata = registered.Metadata;
                field = new ResolvedQueryField(
                    metadata.Name,
                    metadata.Name,
                    metadata.ClrType,
                    metadata.Nullability,
                    metadata.CanFilter || metadata.CanSort,
                    metadata.CanFilter,
                    metadata.CanSort,
                    metadata.AllowedOperators,
                    registered.Selector,
                    navigationDepth: 0,
                    hasCollectionAccess: false);
                return true;
            }

            if (!TryMapProperty(sourcePath, out var mappedPath) ||
                !TryGetProperty(
                    mappedPath,
                    out var clrType,
                    out var nullability,
                    out var canQuery))
                return false;

            TryGetPolicyPathInfo(
                sourcePath,
                out var canonicalPath,
                out var pathNavigationDepth,
                out var hasCollectionAccess);
            field = new ResolvedQueryField(
                sourcePath,
                string.IsNullOrEmpty(canonicalPath) ? mappedPath : canonicalPath,
                clrType,
                nullability,
                canQuery,
                canQuery,
                canQuery,
                null,
                null,
                pathNavigationDepth,
                hasCollectionAccess);
            return true;
        }

        internal bool AreOperatorsAllowed(
            IEnumerable<string> sourcePaths,
            ComparisonOperator comparisonOperator)
        {
            if (sourcePaths == null) throw new ArgumentNullException(nameof(sourcePaths));
            if (!IsPublicSchema) return true;
            return sourcePaths.All(sourcePath =>
                TryResolveQueryField(sourcePath, out var field) &&
                (field.AllowedOperators == null ||
                 field.AllowedOperators.Contains(comparisonOperator)));
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
