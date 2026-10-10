using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionSyntaxTreeTest
    {
        [Fact]
        public void CompleteInputRetainsPrecedenceFunctionsAndMembershipElements()
        {
            const string source = "(Integer + 2) > 5 and Name.contains('a+b') and Id in [1, $ids, null]";
            var tree = ExpressionSyntaxTree.Parse(source);

            Assert.True(tree.Analysis.IsComplete);
            Assert.Empty(tree.Analysis.Diagnostics);
            Assert.Equal(ExpressionSyntaxNodeKind.Binary, tree.Root.Kind);
            Assert.Equal(Interpreter.LogicalAnd, tree.Root.NormalizedText);
            Assert.Equal(ExpressionSyntaxNodeKind.Binary, tree.Root.Children[0].Kind);
            Assert.Equal(ExpressionSyntaxNodeKind.Function, tree.Root.Children[0].Children[1].Kind);

            var arithmeticComparison = tree.Root.Children[0].Children[0];
            Assert.Equal(">", arithmeticComparison.NormalizedText);
            Assert.Equal(ExpressionSyntaxNodeKind.Group, arithmeticComparison.Children[0].Kind);
            Assert.Equal("+", arithmeticComparison.Children[0].Children[0].NormalizedText);

            var membership = tree.Root.Children[1];
            Assert.Equal("in", membership.NormalizedText);
            Assert.Equal(ExpressionSyntaxNodeKind.List, membership.Children[1].Kind);
            Assert.Equal(
                new[]
                {
                    ExpressionSyntaxNodeKind.Literal,
                    ExpressionSyntaxNodeKind.Variable,
                    ExpressionSyntaxNodeKind.Literal
                },
                membership.Children[1].Children.Select(child => child.Kind));
            Assert.Equal(
                new[] { "1", "$ids", "null" },
                membership.Children[1].Children
                    .Select(child => source.Substring(child.Start, child.Length)));
        }

        [Fact]
        public void PositionedNodesUseOriginalUtf16SourceRanges()
        {
            const string source = "Name = '😀' and Id = 1";
            var tree = ExpressionSyntaxTree.Parse(source);

            Assert.True(tree.Analysis.IsComplete);
            Assert.Equal((7, 4), (
                tree.Root.Children[0].Children[1].Start,
                tree.Root.Children[0].Children[1].Length));
            Assert.Equal("😀",
                source.Substring(
                    tree.Root.Children[0].Children[1].Start + 1,
                    tree.Root.Children[0].Children[1].Length - 2));
            Assert.Equal("Id",
                source.Substring(tree.Root.Children[1].Children[0].Start,
                    tree.Root.Children[1].Children[0].Length));
        }

        [Fact]
        public void UnaryGroupingAndArithmeticRetainOperatorAssociativity()
        {
            const string source = "not (Id = 1 or Id = 2) and Integer + 1 - 2 > 0";
            var tree = ExpressionSyntaxTree.Parse(source);

            Assert.True(tree.Analysis.IsComplete);
            Assert.Equal(Interpreter.LogicalAnd, tree.Root.NormalizedText);
            var negation = tree.Root.Children[0];
            Assert.Equal(ExpressionSyntaxNodeKind.Unary, negation.Kind);
            Assert.Equal(ExpressionSyntaxNodeKind.Group, negation.Children[0].Kind);
            Assert.Equal(Interpreter.LogicalOr, negation.Children[0].Children[0].NormalizedText);

            var comparison = tree.Root.Children[1];
            Assert.Equal(">", comparison.NormalizedText);
            Assert.Equal("-", comparison.Children[0].NormalizedText);
            Assert.Equal("+", comparison.Children[0].Children[0].NormalizedText);
        }

        [Fact]
        public void IncompleteSourceRetainsDiagnosticsWithoutInventingRootNodes()
        {
            var tree = ExpressionSyntaxTree.Parse("  Id = ");

            Assert.False(tree.Analysis.IsComplete);
            Assert.Null(tree.Root);
            Assert.Contains(tree.Analysis.Diagnostics,
                diagnostic => diagnostic.Start == 7 && diagnostic.Length == 0);
        }
    }
}
