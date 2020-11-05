namespace Kkts.Expressions
{
	public class Pagination
	{
		public static uint DefaultLimit = 50;
		public static uint MaxLimit = 100;
		private int _offset;
		private int _limit = (int)DefaultLimit;
		private int _page;

		public int Offset
		{
			get => _offset;
			set => _offset = value < 0 ? 0 : value;
		}

		public int Page
		{
			get => _page;
			set
			{
				_page = value < 1 ? 1 : value;
				_offset = _limit * (_page - 1);
			}
		}

		public int Limit
		{
			get => _limit;
			set
			{
				_limit = value <= 0 ? (int)DefaultLimit : value > MaxLimit ? (int)MaxLimit : value;
			}
		}

		public int PageSize
		{
			get => _limit;
			set => Limit = value;
		}
	}
}
