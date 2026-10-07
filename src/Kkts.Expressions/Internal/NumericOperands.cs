using System;
using System.Globalization;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal
{
	internal static class NumericOperands
	{
		public static ConstantExpression ParseLiteral(string value)
		{
			if (value.Contains(".")) return Expression.Constant(value.Cast(typeof(double)));
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return Expression.Constant(i);
			if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ui)) return Expression.Constant(ui);
			if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return Expression.Constant(l);
			if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul)) return Expression.Constant(ul);
			throw new FormatException($"Invalid numeric literal '{value}'.");
		}

		public static bool IsNumeric(Type type)
		{
			type = Nullable.GetUnderlyingType(type) ?? type;
			return type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) ||
				type == typeof(ushort) || type == typeof(char) || type == typeof(int) ||
				type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
				type == typeof(float) || type == typeof(double) || type == typeof(decimal);
		}

		public static bool IsNull(Expression expression) => expression is ConstantExpression c && c.Value == null;

		public static void Normalize(ref Expression left, ref Expression right, bool leftConstant = false, bool rightConstant = false)
		{
			var leftType = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
			var rightType = Nullable.GetUnderlyingType(right.Type) ?? right.Type;
			if (IsNull(left) && IsNumeric(right.Type)) leftType = rightType;
			if (IsNull(right) && IsNumeric(left.Type)) rightType = leftType;
			if (!IsNumeric(leftType) || !IsNumeric(rightType))
				throw new InvalidOperationException($"Numeric operands required, received {left.Type} and {right.Type}.");

			if (leftConstant && CanConvertConstant(left, rightType)) leftType = rightType;
			if (rightConstant && CanConvertConstant(right, leftType)) rightType = leftType;
			var type = Promote(leftType, rightType);
			if (Nullable.GetUnderlyingType(left.Type) != null || Nullable.GetUnderlyingType(right.Type) != null ||
				IsNull(left) || IsNull(right))
				type = typeof(Nullable<>).MakeGenericType(type);
			left = ConvertOperand(left, type);
			right = ConvertOperand(right, type);
		}

		private static bool CanConvertConstant(Expression expression, Type target)
		{
			if (target != typeof(uint) && target != typeof(ulong)) return false;
			if (expression.Type != typeof(int) && !(target == typeof(ulong) && expression.Type == typeof(long))) return false;
			if (!TryGetIntegralConstant(expression, out var value)) return false;
			return value >= 0 && (target == typeof(ulong) || value <= uint.MaxValue);
		}

		private static bool TryGetIntegralConstant(Expression expression, out decimal value)
		{
			if (expression is ConstantExpression c && c.Value != null && IsNumeric(c.Type) &&
				c.Type != typeof(float) && c.Type != typeof(double) && c.Type != typeof(decimal))
			{
				value = c.Value is char character ? character : Convert.ToDecimal(c.Value, CultureInfo.InvariantCulture);
				return true;
			}
			if (expression is UnaryExpression conversion && conversion.NodeType == ExpressionType.Convert)
				return TryGetIntegralConstant(conversion.Operand, out value);
			if (expression is BinaryExpression sum && sum.NodeType == ExpressionType.Add &&
				TryGetIntegralConstant(sum.Left, out var left) && TryGetIntegralConstant(sum.Right, out var right))
			{
				if (sum.Type == typeof(int)) value = unchecked((int)left + (int)right);
				else if (sum.Type == typeof(uint)) value = unchecked((uint)left + (uint)right);
				else if (sum.Type == typeof(long)) value = unchecked((long)left + (long)right);
				else if (sum.Type == typeof(ulong)) value = unchecked((ulong)left + (ulong)right);
				else
				{
					value = 0;
					return false;
				}
				return true;
			}
			value = 0;
			return false;
		}

		private static Expression ConvertOperand(Expression expression, Type type)
		{
			if (IsNull(expression)) return Expression.Constant(null, type);
			return expression.Type == type ? expression : Expression.Convert(expression, type);
		}

		private static Type Promote(Type left, Type right)
		{
			if (left == typeof(decimal) || right == typeof(decimal))
			{
				return PromoteDecimal(left, right);
			}
			if (left == typeof(double) || right == typeof(double)) return typeof(double);
			if (left == typeof(float) || right == typeof(float)) return typeof(float);
			return PromoteIntegral(left, right);
		}

		private static Type PromoteIntegral(Type left, Type right)
		{
			if (left == typeof(ulong) || right == typeof(ulong))
			{
				return PromoteUnsignedLong(left == typeof(ulong) ? right : left);
			}
			if (left == typeof(long) || right == typeof(long)) return typeof(long);
			if (left == typeof(uint) || right == typeof(uint))
			{
				var other = left == typeof(uint) ? right : left;
				return IsSignedSmallIntegral(other) ? typeof(long) : typeof(uint);
			}
			return typeof(int);
		}

		private static bool IsSignedSmallIntegral(Type type)
		{
			return type == typeof(sbyte) || type == typeof(short) || type == typeof(int);
		}

		private static Type PromoteDecimal(Type left, Type right)
		{
			if (left == typeof(float) || right == typeof(float) || left == typeof(double) || right == typeof(double))
				throw new InvalidOperationException("Decimal cannot be added to float or double.");
			return typeof(decimal);
		}

		private static Type PromoteUnsignedLong(Type other)
		{
			if (IsSignedSmallIntegral(other) || other == typeof(long))
				throw new InvalidOperationException("UInt64 cannot be added to a signed integral operand.");
			return typeof(ulong);
		}
	}
}
