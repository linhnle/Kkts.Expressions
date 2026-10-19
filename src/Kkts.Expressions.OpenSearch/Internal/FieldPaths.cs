using System.Linq;

namespace Kkts.Expressions.OpenSearch.Internal
{
    internal static class FieldPaths
    {
        public static bool IsValidSegment(string segment)
        {
            return !string.IsNullOrEmpty(segment) && segment.All(c => c != '.' && !char.IsWhiteSpace(c));
        }

        public static bool IsValidPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.Split('.').All(IsValidSegment);
        }

        public static bool IsUnder(string path, string parent) => path.StartsWith(parent + ".", System.StringComparison.Ordinal);
    }
}
