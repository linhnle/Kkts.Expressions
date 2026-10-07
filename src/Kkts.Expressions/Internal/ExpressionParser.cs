using Kkts.Expressions.Internal.Nodes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal
{
	internal static class ExpressionParser
	{
		private static readonly List<Func<Type, Parser, bool>> BuildSteps =
			new List<Func<Type, Parser, bool>>
			{
				 (t, p) => t == typeof(ArrayParser) || t == typeof(NumberParser) || t == typeof(PropertyParser) || t == typeof(StringParser),
				 (t, p) => t == typeof(NotOperatorParser) || t == typeof(NotFunctionParser) || t == typeof(GroupParser) || t == typeof(ComparisonFunctionOperatorParser),
				 (t, p) => t == typeof(AdditiveOperatorParser),
				 (t, p) => t == typeof(ComparisonOparatorParser),
				 (t, p) => t == typeof(LogicalOperatorParser) && GetStandardOperator(p.NormalizedResult) == Interpreter.LogicalAnd,
				 (t, p) => t == typeof(LogicalOperatorParser)
			};

		public static EvaluationResult Parse(string expression, Type type, BuildArgument arg)
		{
			try
			{
				var param = type.CreateParameterExpression();
				var rootNode = Parse(new ExpressionReader(expression), param, arg);
				if (arg.InvalidProperties.Count > 0 || arg.InvalidVariables.Count > 0)
				{
					return new EvaluationResult
					{
						InvalidProperties = arg.InvalidProperties,
						InvalidVariables = arg.InvalidVariables,
						InvalidOperators = arg.InvalidOperators,
						InvalidValues = arg.InvalidValues,
					};
				}

				var body = rootNode.Build(arg);
				if (body.Type != typeof(bool))
					throw new FormatException("A predicate must have a Boolean result.");

				return new EvaluationResult
				{
					Result = Expression.Lambda(body, param),
					Succeeded = true
				};
			}
			catch (Exception ex)
			{
				return new EvaluationResult
				{
					Exception = ex,
					InvalidProperties = arg.InvalidProperties,
					InvalidVariables = arg.InvalidVariables,
					InvalidOperators = arg.InvalidOperators,
					InvalidValues = arg.InvalidValues
				};
			}
		}

		internal static async Task<EvaluationResult> ParseAsync(string expression, Type type, BuildArgument arg)
		{
			try
			{
				arg.CancellationToken.ThrowIfCancellationRequested();
				var param = type.CreateParameterExpression();
				var rootNode = Parse(new ExpressionReader(expression), param, arg);
				if (arg.InvalidProperties.Count > 0 || arg.InvalidVariables.Count > 0)
				{
					return new EvaluationResult
					{
						InvalidProperties = arg.InvalidProperties,
						InvalidVariables = arg.InvalidVariables,
						InvalidOperators = arg.InvalidOperators,
						InvalidValues = arg.InvalidValues
					};
				}

				arg.CancellationToken.ThrowIfCancellationRequested();
				var body = await rootNode.BuildAsync(arg);
				if (body.Type != typeof(bool))
					throw new FormatException("A predicate must have a Boolean result.");

				return new EvaluationResult
				{
					Result = Expression.Lambda(body, param),
					Succeeded = true
				};
			}
			catch (Exception ex)
			{
				return new EvaluationResult
				{
					Exception = ex,
					InvalidProperties = arg.InvalidProperties,
					InvalidVariables = arg.InvalidVariables,
					InvalidOperators = arg.InvalidOperators,
					InvalidValues = arg.InvalidValues
				};
			}
		}

		private static Node Parse(ExpressionReader reader, ParameterExpression parameter, BuildArgument arg)
		{
			var state = new ParsingState();
			reader.IgnoreWhiteSpace();
			while (!reader.IsEnd)
			{
				state.Read(reader);
			}

			return BuildNode(parameter, state.Complete(reader), arg);
		}

		private sealed class ParsingState
		{
			private List<Parser> _accepted = GetBeginningParsers();
			private List<Parser> _next = new List<Parser>(6);
			private readonly Stack<Parser> _groups = new Stack<Parser>();
			private bool _keepTrack;
			private bool _isStartGroup;
			private Parser _lastAccepted;

			public void Read(ExpressionReader reader)
			{
				var whitespace = _keepTrack ? 0 : reader.IgnoreWhiteSpace();
				var value = reader.Read();
				var parsers = _accepted;
				_accepted = _next;
				_accepted.Clear();
				_next = parsers;
				_isStartGroup = false;
				Parser group = null;

				foreach (var parser in parsers)
				{
					AcceptParser(parser, value, whitespace, reader.CurrentIndex, ref group);
					if (group == null) continue;
					_groups.Push(group);
					_accepted.Clear();
					_accepted.AddRange(group.GetNextParsers(value));
				}

				if (_accepted.Count == 0) CloseGroup(reader, value, whitespace);
			}

			private void AcceptParser(Parser parser, char value, int whitespace, int index, ref Parser group)
			{
				if (parser.Accept(value, whitespace, index, ref _keepTrack, ref _isStartGroup))
				{
					RecordAccepted(parser, parser, ref group);
				}
				else if (parser.Done && parser.Validate())
				{
					if (_groups.Count > 0) _groups.Peek().LastSuccess = parser;
					AcceptNextParsers(parser, value, whitespace, index, ref group);
				}
			}

			private void AcceptNextParsers(Parser parser, char value, int whitespace, int index, ref Parser group)
			{
				foreach (var next in parser.GetNextParsers(value))
				{
					if (next.Accept(value, whitespace, index, ref _keepTrack, ref _isStartGroup))
						RecordAccepted(next, parser, ref group);
				}
			}

			private void RecordAccepted(Parser accepted, Parser last, ref Parser group)
			{
				if (_isStartGroup) group = accepted;
				else
				{
					_accepted.Add(accepted);
					_lastAccepted = last;
				}
			}

			private void CloseGroup(ExpressionReader reader, char value, int whitespace)
			{
				var group = _groups.Count > 0 ? _groups.Pop() : null;
				if (group == null || !group.Accept(value, whitespace, reader.CurrentIndex, ref _keepTrack, ref _isStartGroup))
					throw SyntaxError(_lastAccepted?.Result ?? reader.Current.ToString(), _lastAccepted?.StartIndex ?? reader.CurrentIndex);

				if (reader.HasNext) _accepted.AddRange(group.GetNextParsers(value));
				else _accepted.Add(group);
				group.Body = BuildChain(group, group.LastSuccess);
				if (_groups.Count > 0) _groups.Peek().LastSuccess = group;
			}

			public List<Parser> Complete(ExpressionReader reader)
			{
				Parser last = null;
				var count = 0;
				foreach (var parser in _accepted)
				{
					parser.EndExpression();
					if (!parser.Validate()) continue;
					++count;
					last = parser;
				}

				if (count != 1 || _groups.Count > 0)
					throw SyntaxError(reader.LastChar.ToString(), reader.Length - 1);
				return BuildChain(null, last);
			}

			private static FormatException SyntaxError(string value, int index)
			{
				return new FormatException($"Incorrect syntax near '{value}', index {index}");
			}
			private static List<Parser> BuildChain(Parser root, Parser last)
			{
				var chain = new List<Parser>();
				do
				{
					chain.Add(last);
					var tmp = last;
					last = last.Previous;
					tmp.Previous = null;
				} while (last != root);

				chain.Reverse();
				return chain;
			}

			private static List<Parser> GetBeginningParsers()
			{
				return new List<Parser>
				{
					new PropertyParser(),
					new NumberParser(),
					new StringParser(),
					new NotOperatorParser(),
					new NotFunctionParser(),
					new GroupParser()
				};
			}
		}

		private static Node BuildNode(ParameterExpression param, List<Parser> parsers, BuildArgument arg)
		{
			foreach (var step in BuildSteps)
			{
				// The written prefix holds reduced operands; the unread suffix stays intact.
				var writeIndex = 0;
				for (var index = 0; index < parsers.Count; ++index)
				{
					var parser = parsers[index];
					if (step(parser.GetType(), parser))
					{
						BuildNode(param, parser, parsers, ref index, ref writeIndex, arg);
					}
					parsers[writeIndex++] = parser;
				}

				parsers.RemoveRange(writeIndex, parsers.Count - writeIndex);
			}

			return parsers[0].BuiltNode;
		}

		private static Node BuildNode(ParameterExpression param, Parser parser, List<Parser> list, ref int currentIndex, ref int writeIndex, BuildArgument arg)
		{
			switch (parser)
			{
				case StringParser sp:
					return BuildNode(sp);
				case PropertyParser pp:
					return BuildNode(param, pp, arg);
				case NumberParser np:
					return BuildNode(np);
				case NotOperatorParser nop:
					return BuildNode(param, nop, list, ref currentIndex, ref writeIndex, arg);
				case NotFunctionParser nfp:
					return BuildNode(param, nfp, arg);
				case LogicalOperatorParser lop:
					return BuildNode(lop, list, ref currentIndex, ref writeIndex);
				case GroupParser gp:
					return BuildNode(param, gp, arg);
				case ComparisonOparatorParser cop:
					return BuildNode(cop, list, ref currentIndex, ref writeIndex);
				case ComparisonFunctionOperatorParser cfop:
					return BuildNode(param, cfop, list, ref writeIndex, arg);
				case ArrayParser ap:
					return BuildNode(ap);
				case AdditiveOperatorParser additive:
					return BuildNode(additive, list, ref currentIndex, ref writeIndex);
				default:
					throw new FormatException($"Incorrect syntax near '{parser.Result}', index {parser.StartIndex}");
			}

		}

		private static Node BuildNode(AdditiveOperatorParser parser, List<Parser> list, ref int currentIndex, ref int writeIndex)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			ReadBinaryOperands(parser, list, ref currentIndex, ref writeIndex, out var left, out var right);
			Arithmetic arithmetic = parser.StartChar == '+' ? (Arithmetic)new Addition() : new Subtraction();
			arithmetic.Left = left;
			arithmetic.Right = right;
			arithmetic.StartIndex = parser.StartIndex;
			arithmetic.StartChar = parser.StartChar;
			parser.BuiltNode = arithmetic;
			return parser.BuiltNode;
		}

		private static Node BuildNode(ParameterExpression param, NotOperatorParser parser, List<Parser> list, ref int currentIndex, ref int writeIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var nextParser = list[++currentIndex];
			var result = new Not { Node = BuildNode(param, nextParser, list, ref currentIndex, ref writeIndex, arg), StartIndex = parser.StartIndex, StartChar = '!' };
			parser.BuiltNode = result;

			return result;
		}

		private static Node BuildNode(ParameterExpression param, NotFunctionParser parser, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Not { Node = BuildNode(param, parser.Body, arg), StartIndex = parser.StartIndex, StartChar = parser.StartChar };
			parser.BuiltNode = result;

			return result;
		}

		private static Node BuildNode(ComparisonOparatorParser parser, List<Parser> list, ref int currentIndex, ref int writeIndex)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			ReadBinaryOperands(parser, list, ref currentIndex, ref writeIndex, out var left, out var right);
			var result = new Comparison
			{
				Left = left,
				Right = right,
				Operator = GetStandardOperator(parser.NormalizedResult),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			return result;
		}

		private static Node BuildNode(ParameterExpression param, ComparisonFunctionOperatorParser parser, List<Parser> list, ref int writeIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Comparison
			{
				Left = list[writeIndex - 1].BuiltNode,
				Right = BuildNode(param, parser.Body, arg),
				Operator = GetStandardOperator(parser.NormalizedResult),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			--writeIndex;

			return result;
		}

		private static Node BuildNode(ParameterExpression param, PropertyParser parser, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;

			var result = parser.Result;

			Node builtNode;

			if (parser.IsNull)
			{
				builtNode = new Constant { Value = null, StartIndex = parser.StartIndex, StartChar = parser.StartChar };
			}
			else if (parser.IsBoolean)
			{
				builtNode = new Constant { Value = parser.NormalizedResult, Type = typeof(bool), StartIndex = parser.StartIndex, StartChar = parser.StartChar };
			}
			else if (parser.IsVariable)
			{
                builtNode = new Constant { Value = result, StartIndex = parser.StartIndex, StartChar = parser.StartChar, IsVariable = true };
            }
			else
			{
				if (arg.IsValidProperty(result))
                {
					builtNode = new Property { Name = result, Param = param, StartIndex = parser.StartIndex, StartChar = parser.StartChar };
				}
                else
                {
					builtNode = new Constant { Value = result, StartIndex = parser.StartIndex, StartChar = parser.StartChar, IsVariable = true };
				}
			}

			parser.BuiltNode = builtNode;

			return builtNode;
		}

		private static Node BuildNode(StringParser parser)
		{
			return parser.BuiltNode ?? (parser.BuiltNode = new Constant { Value = parser.Result, StartIndex = parser.StartIndex, StartChar = parser.StartChar, Type = typeof(string) });
		}

		private static Node BuildNode(NumberParser parser)
		{
			return parser.BuiltNode ?? (parser.BuiltNode = new Constant { Value = parser.Result, StartIndex = parser.StartIndex, StartChar = parser.StartChar });
		}

		private static Node BuildNode(LogicalOperatorParser parser, List<Parser> list, ref int currentIndex, ref int writeIndex)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			ReadBinaryOperands(parser, list, ref currentIndex, ref writeIndex, out var left, out var right);
			var result = new Logicality
			{
				Left = left,
				Right = right,
				Operator = GetStandardOperator(parser.NormalizedResult),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			return result;
		}

		private static Node BuildNode(ParameterExpression param, GroupParser parser, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Group { Node = BuildNode(param, parser.Body, arg), StartIndex = parser.StartIndex, StartChar = parser.StartChar };
			parser.BuiltNode = result;

			return result;
		}

		private static Node BuildNode(ArrayParser parser)
		{
			return parser.BuiltNode ?? (parser.BuiltNode = new ArrayList { DrawValue = parser.Result, StartIndex = parser.StartIndex, StartChar = parser.StartChar });
		}

		private static void ReadBinaryOperands(Parser parser, List<Parser> list, ref int currentIndex, ref int writeIndex, out Node left, out Node right)
		{
			if (writeIndex == 0 || currentIndex + 1 >= list.Count)
				throw new FormatException($"Incorrect syntax near '{parser.Result}', index {parser.StartIndex}");

			left = list[writeIndex - 1].BuiltNode;
			right = list[currentIndex + 1].BuiltNode;
			if (left == null || right == null)
				throw new FormatException($"Incorrect syntax near '{parser.Result}', index {parser.StartIndex}");

			--writeIndex;
			++currentIndex;
		}

		private static string GetStandardOperator(string op)
		{
			switch (op)
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
					return op;
			}
		}
	}
}
