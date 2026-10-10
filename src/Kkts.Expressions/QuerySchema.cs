using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>Builds an explicit public query schema from typed field selectors.</summary>
    /// <typeparam name="T">The entity type queried by this schema.</typeparam>
    public sealed class QuerySchema<T>
    {
        private readonly List<RegisteredExpressionField> _fields =
            new List<RegisteredExpressionField>();

        /// <summary>Registers a public field and its expression-tree selector.</summary>
        public QuerySchema<T> Field<TResult>(
            string name,
            Expression<Func<T, TResult>> selector,
            bool canFilter = true,
            bool canSort = true,
            IEnumerable<ComparisonOperator> allowedOperators = null,
            string displayName = null,
            string description = null,
            ExpressionNullability nullability = ExpressionNullability.Unknown)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A field name is required.", nameof(name));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            ValidateName(name);
            if (!Enum.IsDefined(typeof(ExpressionNullability), nullability))
                throw new ArgumentOutOfRangeException(nameof(nullability));
            if (_fields.Any(registered =>
                    string.Equals(registered.Metadata.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    $"Duplicate public query field '{name}'.",
                    nameof(name));
            if (!QueryPolicyFieldMetadata.IsScalar(typeof(TResult)))
                throw new ArgumentException(
                    $"Field '{name}' must have a supported scalar result type.",
                    nameof(selector));

            var operators = allowedOperators?.ToArray();
            if (operators != null && operators.Any(value =>
                    !Enum.IsDefined(typeof(ComparisonOperator), value)))
                throw new ArgumentException(
                    $"Field '{name}' contains an undefined comparison operator.",
                    nameof(allowedOperators));
            if (nullability == ExpressionNullability.Nullable &&
                typeof(TResult).IsValueType &&
                Nullable.GetUnderlyingType(typeof(TResult)) == null)
                throw new ArgumentException(
                    $"Non-nullable value field '{name}' cannot be declared nullable.",
                    nameof(nullability));
            if (nullability == ExpressionNullability.NonNullable &&
                Nullable.GetUnderlyingType(typeof(TResult)) != null)
                throw new ArgumentException(
                    $"Nullable value field '{name}' cannot be declared non-nullable.",
                    nameof(nullability));
            QuerySelectorValidator.Validate(typeof(T), name, selector);

            var field = new RegisteredExpressionField(
                new ExpressionFieldDefinition(
                    name,
                    typeof(TResult),
                    GetNullability(typeof(TResult), nullability),
                    canFilter,
                    canSort,
                    operators,
                    displayName,
                    description),
                selector);
            _fields.Add(field);
            return this;
        }

        /// <summary>Creates an immutable public schema snapshot of the registered fields.</summary>
        public ExpressionSchema Build(ExpressionConversionContext conversionContext = null)
        {
            return ExpressionSchema.FromPublicFields(
                typeof(T),
                _fields,
                conversionContext);
        }

        private static void ValidateName(string name)
        {
            if (!ExpressionGrammar.IsIdentifierStart(name[0]) ||
                name.Any(character => !ExpressionGrammar.IsIdentifierPart(character)) ||
                ExpressionGrammar.IsConstant(name) ||
                ExpressionGrammar.IsLogical(name) ||
                ExpressionGrammar.IsComparison(name) ||
                ExpressionGrammar.IsFunction(name))
                throw new ArgumentException(
                    $"Field name '{name}' is not a supported query identifier.",
                    nameof(name));
        }

        private static ExpressionNullability GetNullability(
            Type type,
            ExpressionNullability nullability)
        {
            if (nullability != ExpressionNullability.Unknown)
                return nullability;
            if (Nullable.GetUnderlyingType(type) != null)
                return ExpressionNullability.Nullable;
            return type.IsValueType
                ? ExpressionNullability.NonNullable
                : ExpressionNullability.Unknown;
        }
    }

    internal sealed class RegisteredExpressionField
    {
        internal RegisteredExpressionField(
            ExpressionFieldDefinition metadata,
            LambdaExpression selector)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        internal ExpressionFieldDefinition Metadata { get; }

        internal LambdaExpression Selector { get; }
    }
}
