using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
	internal class OrderByParser
	{
		private const string Desc1 = "desc";
		private const string Desc2 = "descending";
		private const string Asc1 = "asc";
		private const string Asc2 = "ascending";
		internal static readonly string[] DescendingOptions = { Desc1, Desc2 };
		internal static readonly string[] Options = { Desc1, Desc2, Asc1, Asc2 };
		

		private OrderByParser()
		{
			ThenBys = new List<(string Name, bool Descending)>();
		}

		public readonly ICollection<(string Name, bool Descending)> ThenBys;

		public string PropertyName { get; set; }

		public bool Descending { get; set; }

		public bool IsValid { get; set; } = true;

		public static EvaluationResult<OrderByClause> Parse(OrderByInfo[] orderBys, BuildArgument arg)
		{
			var parser = new OrderByParser();
			foreach (var orderBy in orderBys)
			{
				if (!parser.IsValid)
				{
					arg.IsValidProperty(orderBy.Property);
					continue;
				}

				if (!arg.IsValidProperty(orderBy.Property))
				{
					parser.IsValid = false;
					continue;
				}

				if (parser.PropertyName == null)
				{
					parser.PropertyName = arg.MapProperty(orderBy.Property);
					parser.Descending = orderBy.Descending;
				}
				else
				{
					parser.ThenBys.Add((arg.MapProperty(orderBy.Property), orderBy.Descending));
				}
			}

			return parser.IsValid
				? new EvaluationResult<OrderByClause>
				{
					Succeeded = true,
					Result = new OrderByClause(parser)
				}
				: new EvaluationResult<OrderByClause>
				{
					InvalidProperties = arg.InvalidProperties,
					Exception = new FormatException("Syntax error, Order by expression is not valid")
				};
		}

		public static EvaluationResult<OrderByClause> Parse(string expression, BuildArgument arg)
		{
			var parser = new OrderByParser();
			foreach (var segment in expression.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var parts = segment.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (!parser.IsValid)
				{
					arg.IsValidProperty(parts[0]);
					var isValid = parts.Length == 2 && !arg.IsValidOrderByDirection(parts[1]);
					continue;
				}

				if (parts.Length > 2
					|| !arg.IsValidProperty(parts[0])
					|| (parts.Length == 2 && !arg.IsValidOrderByDirection(parts[1])))
				{
					parser.IsValid = false;
					continue;
				}

				if (parser.PropertyName == null)
				{
					parser.PropertyName = arg.MapProperty(parts[0]);
					parser.Descending = IsDescending(parts);
				}
				else
				{
					parser.ThenBys.Add((arg.MapProperty(parts[0]), IsDescending(parts)));
				}
			}

			return parser.IsValid 
				? new EvaluationResult<OrderByClause>
				{
					Succeeded = true,
					Result = new OrderByClause(parser)
				}
				: new EvaluationResult<OrderByClause>
				{
					InvalidProperties = arg.InvalidProperties,
					Exception = new FormatException("Syntax error, Order by expression is not valid")
				};

			bool IsDescending(string[] parts)
				=> parts.Length >= 2 && DescendingOptions.Contains(parts[1].ToLower());
		}

	}
}
