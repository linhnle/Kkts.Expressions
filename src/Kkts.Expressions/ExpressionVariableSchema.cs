using Kkts.Expressions.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kkts.Expressions
{
    /// <summary>Declares the type and optional members of a variable without providing its value.</summary>
    public sealed class ExpressionVariableDefinition
    {
        private readonly IReadOnlyDictionary<string, ExpressionVariableDefinition> _members;

        public ExpressionVariableDefinition(
            string name,
            Type clrType,
            ExpressionNullability nullability = ExpressionNullability.Unknown,
            IEnumerable<ExpressionVariableDefinition> members = null)
            : this(name, clrType, nullability, members, false)
        {
        }

        private ExpressionVariableDefinition(
            string name,
            Type clrType,
            ExpressionNullability nullability,
            IEnumerable<ExpressionVariableDefinition> members,
            bool reflectMembers)
        {
            Name = ValidateName(name, nameof(name), allowPath: true);
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            if (!Enum.IsDefined(typeof(ExpressionNullability), nullability))
                throw new ArgumentOutOfRangeException(nameof(nullability));
            if (nullability == ExpressionNullability.Nullable &&
                clrType.IsValueType && Nullable.GetUnderlyingType(clrType) == null)
                throw new ArgumentException("A non-nullable value type cannot be declared nullable.", nameof(nullability));
            if (nullability == ExpressionNullability.NonNullable &&
                Nullable.GetUnderlyingType(clrType) != null)
                throw new ArgumentException("A nullable value type cannot be declared non-nullable.", nameof(nullability));

            Nullability = nullability == ExpressionNullability.Unknown
                ? GetDefaultNullability(clrType)
                : nullability;
            ReflectMembers = reflectMembers;

            var memberDefinitions = new Dictionary<string, ExpressionVariableDefinition>(StringComparer.OrdinalIgnoreCase);
            if (members != null)
            {
                foreach (var member in members)
                {
                    if (member == null)
                        throw new ArgumentException("Variable members cannot contain null.", nameof(members));
                    if (member.Name.Contains("."))
                        throw new ArgumentException("Explicit child member names must be single path segments.", nameof(members));
                    if (memberDefinitions.ContainsKey(member.Name))
                        throw new ArgumentException($"Duplicate variable member '{member.Name}'.", nameof(members));
                    memberDefinitions.Add(member.Name, member);
                }
            }
            _members = new ReadOnlyDictionary<string, ExpressionVariableDefinition>(memberDefinitions);
            ElementType = GetElementType(clrType);
        }

        /// <summary>The variable name without its expression prefix.</summary>
        public string Name { get; }

        /// <summary>The declared CLR type; no runtime value is stored.</summary>
        public Type ClrType { get; }

        /// <summary>Declared nullability, or a value-type default when CLR nullability is known.</summary>
        public ExpressionNullability Nullability { get; }

        /// <summary>Collection element type when the declared type is an array or generic enumerable.</summary>
        public Type ElementType { get; }

        /// <summary>Explicit child declarations, copied into a read-only snapshot.</summary>
        public IReadOnlyDictionary<string, ExpressionVariableDefinition> Members => _members;

        internal bool ReflectMembers { get; }

        /// <summary>Declares the readable public instance members of a CLR type for metadata traversal.</summary>
        public static ExpressionVariableDefinition FromType(
            string name,
            Type clrType,
            ExpressionNullability nullability = ExpressionNullability.Unknown)
        {
            ValidateName(name, nameof(name), allowPath: false);
            return new ExpressionVariableDefinition(name, clrType, nullability, null, true);
        }

        /// <summary>Declares the readable public instance members of <typeparamref name="T"/>.</summary>
        public static ExpressionVariableDefinition FromType<T>(
            string name,
            ExpressionNullability nullability = ExpressionNullability.Unknown)
        {
            return FromType(name, typeof(T), nullability);
        }

        internal bool TryGetMember(string name, out Type clrType, out ExpressionNullability nullability, out Type elementType)
        {
            var memberName = NormalizeName(name);
            if (_members.TryGetValue(memberName, out var definition))
            {
                clrType = definition.ClrType;
                nullability = definition.Nullability;
                elementType = definition.ElementType;
                return true;
            }

            if (ReflectMembers && PropertyMetadata.GetMemberTypes(ClrType).TryGetValue(memberName, out clrType))
            {
                nullability = GetDefaultNullability(clrType);
                elementType = GetElementType(clrType);
                return true;
            }

            clrType = null;
            nullability = ExpressionNullability.Unknown;
            elementType = null;
            return false;
        }

        internal static string NormalizeName(string name)
        {
            return string.Concat((name ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
        }

        internal static string ValidateName(string name, string parameterName, bool allowPath)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A variable name is required.", parameterName);
            if (name[0] == VariableResolver.VariablePrefix)
                throw new ArgumentException("Variable declarations omit the expression prefix.", parameterName);

            var normalized = NormalizeName(name);
            var segments = normalized.Split('.');
            if (!allowPath && segments.Length != 1)
                throw new ArgumentException("A variable root name must be a single identifier.", parameterName);
            if (segments.Any(segment => segment.Length == 0 ||
                !ExpressionGrammar.IsIdentifierStart(segment[0]) ||
                segment.Skip(1).Any(character => !ExpressionGrammar.IsIdentifierPart(character))))
                throw new ArgumentException("The variable name is not a valid expression identifier path.", parameterName);
            return normalized;
        }

        internal static ExpressionNullability GetDefaultNullability(Type type)
        {
            if (Nullable.GetUnderlyingType(type) != null) return ExpressionNullability.Nullable;
            if (type.IsValueType) return ExpressionNullability.NonNullable;
            return ExpressionNullability.Unknown;
        }

        internal static Type GetElementType(Type type)
        {
            if (type == typeof(string)) return null;
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return type.GetGenericArguments()[0];

            var enumerable = type.GetInterfaces()
                .Where(interfaceType => interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(interfaceType => interfaceType.GetGenericArguments()[0])
                .Distinct()
                .ToArray();
            return enumerable.Length == 1 ? enumerable[0] : null;
        }
    }

    /// <summary>Immutable declarations for variables used by semantic expression analysis.</summary>
    public sealed class ExpressionVariableSchema
    {
        private readonly IReadOnlyDictionary<string, ExpressionVariableDefinition> _definitions;
        private readonly IReadOnlyDictionary<string, ExpressionVariableDefinition> _rootDefinitions;

        public ExpressionVariableSchema(IEnumerable<ExpressionVariableDefinition> variables)
        {
            var definitions = new Dictionary<string, ExpressionVariableDefinition>(StringComparer.Ordinal);
            var roots = new Dictionary<string, ExpressionVariableDefinition>(StringComparer.Ordinal);
            if (variables != null)
            {
                foreach (var definition in variables)
                {
                    if (definition == null)
                        throw new ArgumentException("Variable declarations cannot contain null.", nameof(variables));
                    var name = ExpressionVariableDefinition.ValidateName(definition.Name, nameof(variables), allowPath: true);
                    if (definitions.ContainsKey(name))
                        throw new ArgumentException($"Duplicate variable declaration '{name}'.", nameof(variables));
                    definitions.Add(name, definition);
                    if (!name.Contains("."))
                    {
                        if (roots.ContainsKey(name))
                            throw new ArgumentException($"Duplicate variable root '{name}'.", nameof(variables));
                        roots.Add(name, definition);
                    }
                }
            }
            _definitions = new ReadOnlyDictionary<string, ExpressionVariableDefinition>(definitions);
            _rootDefinitions = new ReadOnlyDictionary<string, ExpressionVariableDefinition>(roots);
        }

        /// <summary>Variable declarations keyed by their exact names without a prefix.</summary>
        public IReadOnlyDictionary<string, ExpressionVariableDefinition> Variables => _definitions;

        internal bool TryGetVariable(
            string sourceName,
            out Type clrType,
            out ExpressionNullability nullability,
            out Type elementType,
            out bool rootDeclared)
        {
            var name = ExpressionVariableDefinition.NormalizeName(sourceName);
            if (_definitions.TryGetValue(name, out var exact))
            {
                clrType = exact.ClrType;
                nullability = exact.Nullability;
                elementType = exact.ElementType;
                rootDeclared = !name.Contains(".");
                return true;
            }

            var segments = name.Split('.');
            if (_rootDefinitions.TryGetValue(segments[0], out var root))
            {
                rootDeclared = true;
                var current = root;
                var reflectMembers = current.ReflectMembers;
                clrType = current.ClrType;
                nullability = current.Nullability;
                elementType = current.ElementType;
                for (var index = 1; index < segments.Length; index++)
                {
                    if (current != null && current.Members.TryGetValue(segments[index], out var explicitMember))
                    {
                        current = explicitMember;
                        clrType = current.ClrType;
                        nullability = current.Nullability;
                        elementType = current.ElementType;
                        reflectMembers = current.ReflectMembers;
                    }
                    else if (reflectMembers &&
                        PropertyMetadata.GetMemberTypes(clrType).TryGetValue(segments[index], out var memberType))
                    {
                        clrType = memberType;
                        nullability = ExpressionVariableDefinition.GetDefaultNullability(clrType);
                        elementType = ExpressionVariableDefinition.GetElementType(clrType);
                        current = null;
                    }
                    else
                    {
                        return false;
                    }
                }
                return true;
            }

            clrType = null;
            nullability = ExpressionNullability.Unknown;
            elementType = null;
            rootDeclared = false;
            return false;
        }
    }
}
