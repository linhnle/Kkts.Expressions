using System.Collections.Generic;

namespace Kkts.Expressions
{
	public sealed class FilterGroup : IFilter
	{
		public List<Filter> Filters { get; set; }
	}
}
