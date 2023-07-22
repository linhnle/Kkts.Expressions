using Kkts.Expressions.Internal.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
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
				 (t, p) => t == typeof(ComparisonOparatorParser),
				 (t, p) => t == typeof(LogicalOperatorParser) && GetStandardOperator(p.Result) == Interpreter.LogicalAnd,
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

		public static async Task<EvaluationResult> ParseAsync(string expression, Type type, BuildArgument arg)
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
						InvalidValues = arg.InvalidValues
					};
				}

				var body = await rootNode.BuildAsync(arg);

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
			var acceptedParsers = GetBeginningParsers();
			var keepTrack = false;
			var groups = new Stack<Parser>();
			reader.IgnoreWhiteSpace();
			while (!reader.IsEnd)
			{
				var noOfWhiteSpaceIgnored = 0;
				if (!keepTrack) noOfWhiteSpaceIgnored = reader.IgnoreWhiteSpace();
				var c = reader.Read();
				var parsers = acceptedParsers;
				acceptedParsers = new List<Parser>();
				var isStartGroup = false;
				Parser group = null;
				foreach (var parser in parsers)
				{
					if (parser.Accept(c, noOfWhiteSpaceIgnored, reader.CurrentIndex, ref keepTrack, ref isStartGroup))
					{
						if (isStartGroup)
						{
							group = parser;
						}
						else
						{
							acceptedParsers.Add(parser);
						}
					}
					else
					{
						if (parser.Done && parser.Validate())
						{
							if (groups.Count > 0) groups.Peek().LastSuccess = parser;
							var nextParsers = parser.GetNextParsers(c);
							foreach (var nextParser in nextParsers)
							{
								if (nextParser.Accept(c, 0, reader.CurrentIndex, ref keepTrack, ref isStartGroup))
								{
									if (isStartGroup)
									{
										group = nextParser;
									}
									else
									{
										acceptedParsers.Add(nextParser);
									}
								}
							}
						}
					}

					if (group != null)
					{
						groups.Push(group);
						acceptedParsers = group.GetNextParsers(c);
					}
				}

				if (acceptedParsers.Count == 0)
				{
					if (groups.Count > 0 && (group = groups.Pop()).Accept(c, noOfWhiteSpaceIgnored, reader.CurrentIndex, ref keepTrack, ref isStartGroup))
					{
						if (reader.HasNext)
						{
							acceptedParsers = group.GetNextParsers(c);
						}
						else
						{
							acceptedParsers.Add(group);
						}

						group.Body = BuildChain(group, group.LastSuccess);
					}
					else
					{
						ThrowFormatException(c, reader.CurrentIndex);
					}
				}
			}

			Parser lastestParser = null;
			var acceptedCount = 0;
			foreach(var acceptedParser in acceptedParsers)
			{
				acceptedParser.EndExpression();
				if (acceptedParser.Validate())
				{
					++acceptedCount;
					lastestParser = acceptedParser;
				}
			}
			if (acceptedCount != 1 || groups.Count > 0)
			{
				ThrowFormatException(reader.LastChar, reader.Length - 1);
			}

			var chain = BuildChain(null, lastestParser);

			return BuildNode(parameter, chain, arg);

			void ThrowFormatException(char c, int index) => throw new FormatException($"Incorrect syntax near '{c}', index {index}");
		}

		private static List<Parser> BuildChain(Parser root, Parser last)
		{
			var chain = new LinkedList<Parser>();
			do
			{
				chain.AddFirst(last);
				var tmp = last;
				last = last.Previous;
				tmp.Previous = null;
			} while (last != root);

			return chain.ToList();
		}

		private static IList<Parser> GetBeginningParsers()
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

		private static Node BuildNode(ParameterExpression param, List<Parser> parsers, BuildArgument arg)
		{
			foreach (var step in BuildSteps)
			{
				for (var index = 0; index < parsers.Count; ++index)
				{
					var parser = parsers[index];
					if (parsers[index] != null && step(parser.GetType(), parser))
					{
						BuildNode(param, parser, parsers, ref index, arg);
					}
				}

				parsers = parsers.Where(p => p != null).ToList();
			}

			return parsers[0].BuiltNode;
		}

		private static Node BuildNode(ParameterExpression param, Parser parser, List<Parser> list, ref int currentIndex, BuildArgument arg)
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
					return BuildNode(param, nop, list, ref currentIndex, arg);
				case NotFunctionParser nfp:
					return BuildNode(param, nfp, arg);
				case LogicalOperatorParser lop:
					return BuildNode(param, lop, list, ref currentIndex, arg);
				case GroupParser gp:
					return BuildNode(param, gp, arg);
				case ComparisonOparatorParser cop:
					return BuildNode(param, cop, list, ref currentIndex, arg);
				case ComparisonFunctionOperatorParser cfop:
					return BuildNode(param, cfop, list, ref currentIndex, arg);
				case ArrayParser ap:
					return BuildNode(ap);
				default: return null;
			}
		}

		private static Node BuildNode(ParameterExpression param, NotOperatorParser parser, List<Parser> list, ref int currentIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var index = currentIndex;
			var nextParser = list[++currentIndex];
			var result = new Not { Node = BuildNode(param, nextParser, list, ref currentIndex, arg), StartIndex = parser.StartIndex, StartChar = '!' };
			list[index + 1] = null;
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

		private static Node BuildNode(ParameterExpression param, ComparisonOparatorParser parser, List<Parser> list, ref int currentIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Comparison
			{
				Left = BuildNode(param, list[currentIndex - 1], list, ref currentIndex, arg),
				Right = BuildNode(param, list[currentIndex + 1], list, ref currentIndex, arg),
				Operator = GetStandardOperator(parser.Result),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			list[currentIndex - 1] = null;
			list[currentIndex + 1] = null;
			++currentIndex;

			return result;
		}

		private static Node BuildNode(ParameterExpression param, ComparisonFunctionOperatorParser parser, List<Parser> list, ref int currentIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Comparison
			{
				Left = BuildNode(param, list[currentIndex - 1], list, ref currentIndex, arg),
				Right = BuildNode(param, parser.Body, arg),
				Operator = GetStandardOperator(parser.Result),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			list[currentIndex - 1] = null;

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
				builtNode = new Constant { Value = result.ToLower(), Type = typeof(bool), StartIndex = parser.StartIndex, StartChar = parser.StartChar };
			}
			else if (parser.IsVariable && !parser.ForInOperator)
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

		private static Node BuildNode(ParameterExpression param, LogicalOperatorParser parser, List<Parser> list, ref int currentIndex, BuildArgument arg)
		{
			if (parser.BuiltNode != null) return parser.BuiltNode;
			var result = new Logicality
			{
				Left = BuildNode(param, list[currentIndex - 1], list, ref currentIndex, arg),
				Right = BuildNode(param, list[currentIndex + 1], list, ref currentIndex, arg),
				Operator = GetStandardOperator(parser.Result),
				StartIndex = parser.StartIndex,
				StartChar = parser.StartChar
			};
			parser.BuiltNode = result;
			list[currentIndex - 1] = null;
			list[currentIndex] = null;
			list[currentIndex + 1] = parser;
			++currentIndex;

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

		private static string GetStandardOperator(string op)
		{
			op = op.ToLower();
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
