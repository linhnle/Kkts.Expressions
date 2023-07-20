namespace Kkts.Expressions
{
    public class VariableInfo
    {
        public string Name { get; internal set; }

        public bool Resolved { get; internal set; }

        public object Value { get; internal set; }
    }
}
