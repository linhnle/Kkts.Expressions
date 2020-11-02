namespace Kkts.Expressions.Internal
{
	internal class ExpressionReader
	{
		private readonly string _exp;
		private int _index;
		public ExpressionReader(string exp)
		{
			_exp = exp.Trim();
		}

		public bool IsEnd => _index >= _exp.Length;

		public bool HasNext => _index < _exp.Length - 1;

		public int CurrentIndex => _index >= _exp.Length ? _exp.Length - 1 : _index;

		public char Current => _exp[CurrentIndex];

		public char LastChar => _exp[_exp.Length - 1];

		public int Length => _exp.Length;

		public int IgnoreWhiteSpace()
		{
			var count = 0;
			while (_exp[_index].IsWhiteSpace())
			{
				++count;
				++_index;
			}

			return count;
		}

		public char Read()
		{
			return _index >= _exp.Length ? char.MinValue : _exp[_index++];
		}

		public void Reset()
		{
			_index = 0;
		}
	}
}
