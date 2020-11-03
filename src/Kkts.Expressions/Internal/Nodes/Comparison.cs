﻿using System;
using System.Linq;
using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Comparison : Node
	{
		public string Operator { get; set; }

		public Node Left { get; set; }

		public Node Right { get; set; }

		public override Expression Build(BuildArgument options)
		{
			try
			{
				Expression left = null;
				Expression right = null;
				Type inOperatorDataType = typeof(string);
				if (Left is Constant c && Right is Property p)
				{
					if (c.Type == null)
					{
						var propEx = (MemberExpression)p.Build(options);
						c.Type = propEx.Type;
						left = c.Build(options);
						right = propEx;
					}
					else
					{
						left = c.Build(options);
						right = p.Build(options);
					}
				}
				else if (Left is Property p2 && Right is Constant c2)
				{
					var propEx = (MemberExpression)p2.Build(options);
					c2.Type = propEx.Type;
					left = propEx;
					right = c2.Build(options);
				}
				else if (Left is Property p3 && Right is ArrayList al)
				{
					var propEx = (MemberExpression)p3.Build(options);
					al.Type = propEx.Type;
					left = propEx;
					right = al.Build(options);
					inOperatorDataType = al.Type;
				}
				else if (Left is Constant c3 && Right is ArrayList al2)
				{
					if (c3.Type != null) al2.Type = c3.Type;
					else if (c3.Type == null)
					{
						c3.Type = typeof(int);
						al2.Type = c3.Type;
					}

					inOperatorDataType = c3.Type;
					left = c3.Build(options);
					right = al2.Build(options);
				}
				else
				{
					left = Left.Build(options);
					right = Right?.Build(options);
				}

				if (left == null) throw new FormatException(GetErrorMessage());
				if (string.IsNullOrEmpty(Operator)) return left;

				switch (Operator)
				{
					case Interpreter.ComparisonContains:
						return Expression.Call(left, Interpreter.StringContainsMethod, right);
					case Interpreter.ComparisonStartsWith:
						return Expression.Call(left, Interpreter.StringStartsWithMethod, right);
					case Interpreter.ComparisonEndsWith:
						return Expression.Call(left, Interpreter.StringEndsWithMethod, right);
					case Interpreter.ComparisonEqual:
					case Interpreter.ComparisonEqual2:
						return Expression.Equal(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonGreaterThan:
						return Expression.GreaterThan(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonGreaterThanOrEqual:
						return Expression.GreaterThanOrEqual(left,
							right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonLessThan:
						return Expression.LessThan(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonLessThanOrEqual:
						return Expression.LessThanOrEqual(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.ComparisonNotEqual:
						return Expression.NotEqual(left, right ?? throw new FormatException(GetErrorMessage()));
					case Interpreter.LogicalNot:
						return left;
					case Interpreter.ComparisonIn:
						return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new Type[] { inOperatorDataType }, right, left);
					default:
						throw new FormatException(GetErrorMessage());
				}
			}
			catch (Exception ex)
			{
				throw new FormatException(GetErrorMessage(), ex);
			}
		}
	}
}