using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions
{
	public class PagedResult
	{
		private int _totalRecords;
		private int _pageSize;

		public PagedResult(Pagination pagination)
		{
			if(pagination == null) throw new ArgumentNullException(nameof(pagination));
			Offset = pagination.Offset;
			Limit = pagination.Limit;
			Page = pagination.Page;
			PageSize = pagination.PageSize;
		}

		public IEnumerable Records { get; set; }

		public int Offset { get; }

		public int Page { get; }

		public int Limit { get; }

		public int PageSize
		{
			get => _pageSize;
			set
			{
				_pageSize = value;
				CalculateTotalPages();
			}
		}

		public int TotalRecords
		{
			get => _totalRecords;
			set
			{
				_totalRecords = value;
				CalculateTotalPages();
			}
		}

		public int TotalPages { get; private set; }

		private void CalculateTotalPages()
		{
			TotalPages = 0;
			if (PageSize <= 0) return;
			TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
		}
	}

	public class PagedResult<T> : PagedResult
	{
		public PagedResult(Pagination pagination) : base(pagination) { }

		public new IEnumerable<T> Records
		{
			get => base.Records?.OfType<T>();
			set => base.Records = value;
		}
	}
}
