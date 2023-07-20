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
				if (item.StartsWith(VariableResolver.VariablePrefixString))
                {
					value = arg.VariableResolver.TryResolve(item, out var v)
						? ConvertType(v, Type)
						: throw new FormatException($"Invalid variable, name {item}");

				}
                else
                {
					value = item.Cast(Type);
				}

				arr.SetValue(value, i++);
			}

			return Expression.Constant(arr);
		}

		public override async Task<Expression> BuildAsync(BuildArgument arg)
		{
			ParseValues();
			var arr = Array.CreateInstance(Type, StringValues.Count);
			var i = 0;
			foreach (var item in StringValues)
            {
				object value;
				if (item.StartsWith(VariableResolver.VariablePrefixString))
                {
					var variableInfo = await arg.VariableResolver.ResolveAsync(item, arg.CancellationToken);
					if (variableInfo.Resolved)
                    {
						value = ConvertType(variableInfo.Value, Type);
                    }
                    else
                    {
						arg.InvalidVariables.Add(item);
						throw new FormatException($"Invalid variable, name {item}");
					}
                }
                else
                {
					value = item.Cast(Type);
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
				if (value == null) value = new StringBuilder();
				if (!started)
				{
					dotCount = 0;
					started = true;
					if (c == '"' || c == '\'')
					{
						openChar = c;
						continue;
					}
					else
					{
						openChar = char.MinValue;
					}
				}
				
				if (!isSpecialChar && c == '\\')
				{
					isSpecialChar = true;
					continue;
				}

				if (isSpecialChar)
				{
					isSpecialChar = false;
					value.Append(c == '"' || c == '\'' ? c : '\\');
					continue;
				}

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
					if (openChar != char.MinValue)
					{
						value.Append(c);
					}
					else
					{
						started = false;
						StringValues.Add(value.ToString().Trim());
						value = null;
						isVariable = false;
                        IgnoreWhiteSpaceAndComma(drawValue, ref index);
					}

					continue;
				}

				if (openChar == char.MinValue)
				{
					if (isVariable)
					{
                        value.Append(c);
                        continue;
					}
					if (c == '.')
					{
						++dotCount;
						if (dotCount > 1) throw new FormatException(GetErrorMessage());
					}
                    else if (c == VariableResolver.VariablePrefix)
                    {
						isVariable = true;
                        value.Append(c);
                        continue;
                    }
                    else if (!char.IsDigit(c)) throw new FormatException(GetErrorMessage());
					
				}

				value.Append(c);
			}

			if (value != null)
			{
				StringValues.Add(value.ToString().Trim());
			}
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

		private static object ConvertType(object value, Type type)
		{
			return value is string s ? s.Cast(type) : Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type);
        }
	}
}
