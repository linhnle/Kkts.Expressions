using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
    public class PagedResult
	{
		public IEnumerable Records { get; set; }

		public int TotalRecords { get; set; }
	}

	public class PagedResult<T> : PagedResult
	{
		public new IEnumerable<T> Records
		{
			get => base.Records?.OfType<T>();
			set => base.Records = value;
		}
	}
}
