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

		internal static string NormalizeOperator(string value)
		{
			switch (value?.ToLowerInvariant())
			{
				case Interpreter.LogicalAnd2:
				case Interpreter.LogicalAnd3:
					return Interpreter.LogicalAnd;
				case Interpreter.LogicalOr2:
				case Interpreter.LogicalOr3:
					return Interpreter.LogicalOr;
				case Interpreter.ComparisonEqual:
					return Interpreter.ComparisonEqual2;
				case Interpreter.ComparisonNotEqual2:
					return Interpreter.ComparisonNotEqual;
				case Interpreter.ComparisonContains2:
				case Interpreter.ComparisonContains3:
					return Interpreter.ComparisonContains;
				case Interpreter.ComparisonStartsWith2:
				case Interpreter.ComparisonStartsWith3:
					return Interpreter.ComparisonStartsWith;
				case Interpreter.ComparisonEndsWith2:
				case Interpreter.ComparisonEndsWith3:
					return Interpreter.ComparisonEndsWith;
				default:
					return value;
			}
		}

		internal static int GetBinaryPrecedence(string value)
		{
			switch (NormalizeOperator(value))
			{
				case Interpreter.LogicalOr:
					return 1;
				case Interpreter.LogicalAnd:
					return 2;
				case Interpreter.ComparisonEqual2:
				case Interpreter.ComparisonNotEqual:
				case Interpreter.ComparisonLessThan:
				case Interpreter.ComparisonLessThanOrEqual:
				case Interpreter.ComparisonGreaterThan:
				case Interpreter.ComparisonGreaterThanOrEqual:
				case Interpreter.ComparisonContains:
				case Interpreter.ComparisonStartsWith:
				case Interpreter.ComparisonEndsWith:
				case Interpreter.ComparisonIn:
				case Interpreter.ComparisonNotIn:
					return 3;
				case "+":
				case "-":
					return 4;
				default:
					return -1;
			}
		}

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
