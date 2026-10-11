using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
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
			ThenBys = new List<(string Name, bool Descending, ResolvedQueryField Field)>();
		}

		public readonly ICollection<(string Name, bool Descending, ResolvedQueryField Field)> ThenBys;

		public string PropertyName { get; set; }

		public ResolvedQueryField PropertyField { get; set; }

		public bool Descending { get; set; }

		public bool IsValid { get; set; } = true;

		public static EvaluationResult<OrderByClause> Parse(OrderByInfo[] orderBys, BuildArgument arg)
		{
			var parser = new OrderByParser();
			foreach (var orderBy in orderBys)
			{
				if (!parser.IsValid)
				{
					arg.IsValidOrderByProperty(orderBy.Property);
					continue;
				}

				if (!arg.IsValidOrderByProperty(orderBy.Property))
				{
					parser.IsValid = false;
					continue;
				}

				if (parser.PropertyName == null)
				{
					parser.SetFirst(orderBy.Property, orderBy.Descending, arg);
				}
				else
				{
					parser.ThenBys.Add(ResolveKey(orderBy.Property, orderBy.Descending, arg));
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
					arg.IsValidOrderByProperty(parts[0]);
					if (parts.Length == 2) arg.IsValidOrderByDirection(parts[1]);
					continue;
				}

				if (parts.Length > 2
					|| !arg.IsValidOrderByProperty(parts[0])
					|| (parts.Length == 2 && !arg.IsValidOrderByDirection(parts[1])))
				{
					parser.IsValid = false;
					continue;
				}

				if (parser.PropertyName == null)
				{
					parser.SetFirst(parts[0], IsDescending(parts), arg);
				}
				else
				{
					parser.ThenBys.Add(ResolveKey(parts[0], IsDescending(parts), arg));
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
				=> parts.Length >= 2 && DescendingOptions.Contains(parts[1], StringComparer.OrdinalIgnoreCase);
		}

		private void SetFirst(string sourceName, bool descending, BuildArgument arg)
		{
			var key = ResolveKey(sourceName, descending, arg);
			PropertyName = key.Name;
			PropertyField = key.Field;
			Descending = key.Descending;
		}

		private static (string Name, bool Descending, ResolvedQueryField Field) ResolveKey(
			string sourceName,
			bool descending,
			BuildArgument arg)
		{
			arg.TryResolveQueryField(sourceName, out var field);
			return (
				field?.Identity ?? arg.MapProperty(sourceName),
				descending,
				field);
		}

	}
}
