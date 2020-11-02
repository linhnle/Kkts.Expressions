namespace Kkts.Expressions
{
	public class Pagination : ConditionOptions
	{
		public static uint DefaultLimit = 50;
		public static uint MaxLimit = 100;
		private int _offset;
		private int _limit = (int)DefaultLimit;
		private int _page;

		public int Offset
		{
			get => _offset;
			set
			{
				if (value < 0) value = 0;
				_offset = value;
				CalculatePage();
			}
		}

		public int Page
		{
			get => _page;
			set
			{
				if (value < 1) value = 1;
				_page = value;
				CalculateOffset();
			}
		}

		public int Limit
		{
			get => _limit;
			set
			{
				_limit = value < 0 ? (int)DefaultLimit : value > MaxLimit ? (int)MaxLimit : value;
				CalculateOffset();
				CalculatePage();
			}
		}

		public int PageSize
		{
			get => _limit;
			set => Limit = value;
		}
		
		private void CalculatePage()
		{
			_page = _limit > 0 ? _offset / _limit + 1 : 1;
		}

		private void CalculateOffset()
		{
			_offset = _limit * (_page - 1);
		}
	}
}
