using System;
using System.Linq;

namespace Kkts.Expressions.Internal
{
	internal static class ExpressionGrammar
	{
		internal const char Escape = '\\';
		internal static readonly char[] ComparisonCharacters = { '=', '!', '<', '>', '@', '*' };
		internal static readonly char[] LogicalCharacters = { '|', '&' };
		internal static readonly string[] LogicalOperators =
		{
			Interpreter.LogicalAnd, Interpreter.LogicalAnd2, Interpreter.LogicalAnd3,
			Interpreter.LogicalOr, Interpreter.LogicalOr2, Interpreter.LogicalOr3
		};

		internal static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
		internal static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || char.IsDigit(value);
		internal static bool IsQuote(char value) => value == '\'' || value == '"';
		internal static bool IsAdditive(char value) => value == '+' || value == '-';
		internal static bool IsListStart(char value) => value == '[' || value == '(' || value == '{';
		internal static char ListEnd(char value) => value == '[' ? ']' : value == '(' ? ')' : '}';
		internal static bool IsConstant(string value) =>
			string.Equals(value, Interpreter.True, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, Interpreter.False, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, Interpreter.Null, StringComparison.OrdinalIgnoreCase);
		internal static bool IsLogical(string value) => LogicalOperators.Contains(value, StringComparer.OrdinalIgnoreCase);
		internal static bool IsComparison(string value) => Interpreter.ComparisonOperators.Contains(value, StringComparer.OrdinalIgnoreCase);
		internal static bool IsFunction(string value) => Interpreter.ComparisonFunctionOperators.Contains(value, StringComparer.OrdinalIgnoreCase);

		internal static bool IsNumber(string value)
		{
			var digits = 0;
			var decimalPoint = false;
			for (var i = value.Length > 0 && value[0] == '-' ? 1 : 0; i < value.Length; ++i)
			{
				if (value[i] >= '0' && value[i] <= '9') ++digits;
				else if (value[i] == '.' && !decimalPoint) decimalPoint = true;
				else return false;
			}
			return digits > 0;
		}

		internal static bool HasBinaryOperands(int operandCount, bool hasRight) => operandCount > 0 && hasRight;
	}
}
