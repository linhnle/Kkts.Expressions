namespace Kkts.Expressions.Internal
{
    internal static class ExpressionCompletionBudget
    {
        internal const int MaximumSourceLength = 16384;
        internal const int MaximumTokens = 8192;
        internal const int MaximumScopes = 64;
        internal const int MaximumDescriptors = 4096;
    }
}
