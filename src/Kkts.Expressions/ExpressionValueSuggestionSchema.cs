using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>A declared scalar literal hint, not an exhaustive allowed-value constraint.</summary>
    public sealed class ExpressionValueSuggestion
    {
        public ExpressionValueSuggestion(FilterValue value, string label = null, string description = null)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            if (value.Kind == FilterValueKind.Variable || value.Kind == FilterValueKind.Collection)
                throw new ArgumentException("A value suggestion must be a scalar literal.", nameof(value));
            Label = label;
            Description = description ?? string.Empty;
        }

        public FilterValue Value { get; }
        public string Label { get; }
        public string Description { get; }
    }

    /// <summary>Immutable advisory literal metadata bound to one entity schema snapshot.</summary>
    public sealed class ExpressionValueSuggestionSchema
    {
        public ExpressionValueSuggestionSchema(
            ExpressionSchema schema,
            IEnumerable<KeyValuePair<string, IEnumerable<ExpressionValueSuggestion>>> fields)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            var snapshot = new Dictionary<string, IReadOnlyList<ExpressionValueSuggestion>>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key) ||
                    !schema.TryResolveQueryField(field.Key, out var resolved))
                    throw new ArgumentException("Value suggestions require an existing public field name.", nameof(fields));
                if (snapshot.ContainsKey(field.Key))
                    throw new ArgumentException($"Duplicate value-suggestion field '{field.Key}'.", nameof(fields));
                if (field.Value == null)
                    throw new ArgumentException("Value-suggestion groups cannot be null.", nameof(fields));
                var hints = field.Value.ToArray();
                foreach (var hint in hints)
                {
                    if (hint == null)
                        throw new ArgumentException("Value suggestions cannot contain null.", nameof(fields));
                    if (!ExpressionLiteralCodec.TryEncode(hint.Value, '\'', false, out _, out var literal, out var type))
                        throw new ArgumentException(
                            $"A value suggestion for '{field.Key}' cannot be represented losslessly by the expression grammar.", nameof(fields));
                    if (hint.Value.Kind != FilterValueKind.Null &&
                        !ExpressionConversionRules.CanConvertLiteral(literal, type, false, resolved.ClrType, schema.ConversionContext))
                        throw new ArgumentException(
                            $"A value suggestion for '{field.Key}' is incompatible with its declared result type.", nameof(fields));
                }
                snapshot.Add(field.Key, Array.AsReadOnly(hints));
            }
            Fields = new ReadOnlyDictionary<string, IReadOnlyList<ExpressionValueSuggestion>>(snapshot);
        }

        public IReadOnlyDictionary<string, IReadOnlyList<ExpressionValueSuggestion>> Fields { get; }
        internal ExpressionSchema Schema { get; }

        internal void ValidateSchema(ExpressionSchema schema)
        {
            if (!ReferenceEquals(Schema, schema))
                throw new ArgumentException(
                    "Value suggestions are bound to a different schema snapshot.", "valueSuggestions");
        }
    }
}
