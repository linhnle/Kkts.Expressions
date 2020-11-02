namespace Kkts.Expressions
{
	public sealed class Filter : IFilter
	{
		private string _property;
		private string _operator;

		public string Property { get => _property; set => _property = value?.Trim(); }

		public string Operator { get => _operator; set => _operator = value?.Trim().ToLower(); }

		public string Value { get; set; }
	}
}
