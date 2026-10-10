using System;
using System.Globalization;
using System.Text;

namespace Kkts.Expressions.Internal
{
    internal static class ExpressionLiteralCodec
    {
        internal static string Quote(string value, char quote = '\'') =>
            quote + value.Replace(quote.ToString(), ExpressionGrammar.Escape + quote.ToString()) + quote;

        internal static string DecodeString(string content, char quote)
        {
            var output = new StringBuilder(content.Length);
            for (var index = 0; index < content.Length; ++index)
            {
                if (content[index] == ExpressionGrammar.Escape &&
                    index + 1 < content.Length && content[index + 1] == quote)
                    ++index;
                output.Append(content[index]);
            }
            return output.ToString();
        }

        internal static int ScanQuotedToken(string source, int start, out bool closed)
        {
            var quote = source[start];
            var index = start + 1;
            closed = false;
            while (index < source.Length)
            {
                var value = source[index++];
                if (value == ExpressionGrammar.Escape && index < source.Length && source[index] == quote)
                {
                    ++index;
                    continue;
                }
                if (value == quote)
                {
                    closed = true;
                    break;
                }
            }
            return index;
        }

        internal static string CanonicalOperator(string op)
        {
            switch (op.ToLowerInvariant())
            {
                case "==": return "=";
                case "!=":
                case "<>": return "!=";
                case "in": return "in";
                case "not in": return "not in";
                case "contain":
                case "contains": return "contains";
                case "startwith":
                case "startswith": return "startswith";
                case "endwith":
                case "endswith": return "endswith";
                default: return op;
            }
        }

        internal static bool TryEncode(
            FilterValue value, char quote, bool membership, out string text,
            out object literal, out Type literalType)
        {
            text = null;
            literal = null;
            literalType = null;
            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    text = Interpreter.Null;
                    return true;
                case FilterValueKind.Boolean:
                    literal = value.BooleanValue;
                    literalType = typeof(bool);
                    text = value.BooleanValue ? Interpreter.True : Interpreter.False;
                    return true;
                case FilterValueKind.Number:
                    if (!TryReadLosslessNumber(value.Text, out literal, out literalType)) return false;
                    text = value.Text;
                    return true;
                case FilterValueKind.String:
                    if (!ExpressionGrammar.IsQuote(quote) ||
                        membership && value.Text.StartsWith(VariableResolver.VariablePrefixString, StringComparison.Ordinal))
                        return false;
                    var encoded = Quote(value.Text, quote);
                    var parser = new StringParser();
                    var keepTrack = false;
                    var startGroup = false;
                    for (var index = 0; index < encoded.Length; ++index)
                        if (!parser.Accept(encoded[index], 0, index, ref keepTrack, ref startGroup))
                            return false;
                    if (!parser.Done || parser.EndIndex != encoded.Length - 1 ||
                        !string.Equals(parser.Result, value.Text, StringComparison.Ordinal) ||
                        !string.Equals(DecodeString(encoded.Substring(1, encoded.Length - 2), quote),
                            value.Text, StringComparison.Ordinal))
                        return false;
                    text = encoded;
                    literal = value.Text;
                    literalType = typeof(string);
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryReadLosslessNumber(string text, out object literal, out Type type)
        {
            literal = null;
            type = null;
            if (!ExpressionGrammar.IsNumber(text) || text.IndexOfAny(new[] { 'e', 'E' }) >= 0)
                return false;
            try
            {
                var parsed = NumericOperands.ParseLiteral(text);
                if (parsed.Type == typeof(double) &&
                    (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) ||
                     (decimal)(double)parsed.Value != exact))
                    return false;
                literal = parsed.Value;
                type = parsed.Type;
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
    }
}
