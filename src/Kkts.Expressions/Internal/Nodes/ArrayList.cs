using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class ArrayList : Node
	{
		public string DrawValue { get; set; }

		public List<string> StringValues { get; private set; }

		public Type Type { get; set; }

		public override Expression Build(BuildArgument arg)
		{
			ParseValues();
			var arr = Array.CreateInstance(Type, StringValues.Count);
			var i = 0;
			foreach (var item in StringValues)
            {
				object value;
				if (item != null && item.StartsWith(VariableResolver.VariablePrefixString, StringComparison.Ordinal))
                {
					if (!arg.VariableResolver.TryResolve(item, out var resolved))
					{
						arg.InvalidVariables.Add(item);
						throw new FormatException($"Invalid variable, name {item}");
					}
					value = resolved.Cast(Type);
				}
                else
                {
					value = ((object)item).Cast(Type);
				}

				arr.SetValue(value, i++);
			}

			return Expression.Constant(arr);
		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			arg.CancellationToken.ThrowIfCancellationRequested();
			ParseValues();
			var arr = Array.CreateInstance(Type, StringValues.Count);
			var i = 0;
			foreach (var item in StringValues)
            {
				arg.CancellationToken.ThrowIfCancellationRequested();
				object value;
				if (item != null && item.StartsWith(VariableResolver.VariablePrefixString, StringComparison.Ordinal))
                {
					var variableInfo = await arg.VariableResolver.TryResolveAsync(item, arg.CancellationToken);
					if (variableInfo.Resolved)
                    {
						value = variableInfo.Value.Cast(Type);
                    }
                    else
                    {
						arg.InvalidVariables.Add(item);
						throw new FormatException($"Invalid variable, name {item}");
					}
                }
                else
                {
					value = ((object)item).Cast(Type);
                }
				
				arr.SetValue(value, i++);
			}

			return Expression.Constant(arr);
		}

		public void ParseValues()
		{
			StringValues = new List<string>();
			if (string.IsNullOrWhiteSpace(DrawValue)) return;

			StringBuilder value = null;
			var isSpecialChar = false;
			var started = false;
			var isVariable = false;
			char openChar = char.MinValue;
			var whiteSpaceCount = 0;
			while (DrawValue[whiteSpaceCount].IsWhiteSpace()) ++whiteSpaceCount;
			var drawValue = DrawValue.Trim();
			StartIndex += whiteSpaceCount;
			var dotCount = 0;

			for (var index = 0; index < drawValue.Length; ++index, ++StartIndex)
			{
				var c = drawValue[index];
				if (!started && index + 4 <= drawValue.Length &&
					string.Compare(drawValue, index, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0 &&
					(index + 4 == drawValue.Length || drawValue[index + 4] == ',' || drawValue[index + 4].IsWhiteSpace()))
				{
					StringValues.Add(null);
					index += 3;
					StartIndex += 3;
					IgnoreWhiteSpaceAndComma(drawValue, ref index);
					continue;
				}
				value = value ?? new StringBuilder();
				if (StartValue(c, ref started, ref openChar, ref dotCount)) continue;
				if (AppendEscapedCharacter(c, value, ref isSpecialChar)) continue;

				if (c == openChar)
				{
					openChar = char.MinValue;
					started = false;
					StringValues.Add(value.ToString());
					value = null;
					IgnoreWhiteSpaceAndComma(drawValue, ref index);
					continue;
				}

				if (c == ',')
				{
					FinishComma(drawValue, openChar, ref index, ref value, ref started, ref isVariable);
					continue;
				}

				if (openChar == char.MinValue)
				{
					ValidateUnquotedCharacter(c, ref isVariable, ref dotCount);
				}

				value.Append(c);
			}

			if (value != null)
			{
				StringValues.Add(value.ToString().Trim());
			}
		}

		private static bool StartValue(char c, ref bool started, ref char openChar, ref int dotCount)
		{
			if (started) return false;
			dotCount = 0;
			started = true;
			var quoted = c == '"' || c == '\'';
			openChar = quoted ? c : char.MinValue;
			return quoted;
		}

		private static bool AppendEscapedCharacter(char c, StringBuilder value, ref bool isSpecialChar)
		{
			if (!isSpecialChar && c == '\\')
			{
				isSpecialChar = true;
				return true;
			}
			if (!isSpecialChar) return false;
			isSpecialChar = false;
			value.Append(c == '"' || c == '\'' ? c : '\\');
			return true;
		}

		private void FinishComma(string drawValue, char openChar, ref int index, ref StringBuilder value, ref bool started, ref bool isVariable)
		{
			if (openChar != char.MinValue)
			{
				value.Append(',');
				return;
			}
			started = false;
			StringValues.Add(value.ToString().Trim());
			value = null;
			isVariable = false;
			IgnoreWhiteSpaceAndComma(drawValue, ref index);
		}

		private void ValidateUnquotedCharacter(char c, ref bool isVariable, ref int dotCount)
		{
			if (isVariable) return;
			if (c == '.')
			{
				++dotCount;
				if (dotCount > 1) throw new FormatException(GetErrorMessage());
			}
			else if (c == VariableResolver.VariablePrefix) isVariable = true;
			else if (!char.IsDigit(c)) throw new FormatException(GetErrorMessage());
		}

		private void IgnoreWhiteSpaceAndComma(string str, ref int index)
		{
			if (index == str.Length - 1) return;
			++index;
			++StartIndex;
			var c = str[index];
			while (index < (str.Length - 1) && (c.IsWhiteSpace() || c == ','))
			{
				++StartIndex;
				++index;
				c = str[index];
			}

			--StartIndex;
			--index;
		}
	}
}
