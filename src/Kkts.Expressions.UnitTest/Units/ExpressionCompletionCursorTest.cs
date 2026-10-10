using System;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionCursorTest
    {
        [Theory]
        [InlineData("Sta|Tus = 'Active' and Price > 1", 0, 6, "Sta", "")]
        [InlineData("Customer.Na|me = 'a'", 0, 13, "Customer.Na", "")]
        [InlineData("Customer.Ad|dress.City = 'a'", 0, 21, "Customer.Ad", ".City")]
        [InlineData("Customer|.Address.City = 'a'", 0, 21, "Customer", ".Address.City")]
        [InlineData("$u|tcnow", 0, 7, "$u", "")]
        [InlineData("CreatedAt > $|", 12, 1, "$", "")]
        [InlineData("Price not i|", 6, 5, "not i", "")]
        [InlineData("Price not   i|n [1]", 6, 8, "not i", "")]
        [InlineData("Price >|= 1", 6, 2, ">", "")]
        [InlineData("Status = 'Ac|ZZ' and Price > 1", 9, 6, "Ac", "")]
        [InlineData("Status = \"Ac|", 9, 3, "Ac", "")]
        public void CursorOwnsTheEntireEditableTokenAndPreservesDeeperSuffix(
            string marked, int start, int length, string prefix, string suffix)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var cursor = Locate(text, position);
            Assert.Equal(start, cursor.Start);
            Assert.Equal(length, cursor.Length);
            Assert.Equal(prefix, cursor.Prefix);
            Assert.Equal(suffix, cursor.PathSuffix);
            Assert.True(cursor.HasReliablePrefix);
        }

        [Theory]
        [InlineData("Price   | > 1")]
        [InlineData("  |")]
        [InlineData("( |Price > 1)")]
        [InlineData("Status in ['a', |]")]
        public void GapsAndDelimitersDoNotConsumeSurvivingText(string marked)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var cursor = Locate(text, position);
            // An identifier beginning exactly at the cursor is an editable token.
            if (position < text.Length && ExpressionGrammar.IsIdentifierStart(text[position]))
                Assert.True(cursor.HasToken);
            else
            {
                Assert.False(cursor.HasToken);
                Assert.Equal(position, cursor.Start);
                Assert.Equal(0, cursor.Length);
            }
        }

        [Fact]
        public void ClosedQuoteBoundaryCanBecomeAZeroLengthContinuation()
        {
            const string text = "Status = 'Active'";
            var cursor = Locate(text, text.Length);
            Assert.True(cursor.IsAtTokenEnd);
            Assert.True(cursor.IsClosedQuote);
            var insertion = cursor.AsInsertion();
            Assert.False(insertion.HasToken);
            Assert.Equal(text.Length, insertion.Start);
            Assert.Equal(0, insertion.Length);
        }

        [Fact]
        public void ExistingDelimiterAtCursorRemainsInTheSuffix()
        {
            var cursor = Locate("(", 0);
            Assert.False(cursor.HasToken);
            Assert.Equal(0, cursor.TokenStart);
            Assert.Equal(0, cursor.TokenEnd);
            Assert.Equal(0, cursor.Length);
        }

        [Fact]
        public void EscapesUseTheActiveQuoteAndSuppressASplitEscapeSequence()
        {
            const string text = "Name = 'a\\'bZZ'";
            var completeEscape = Locate(text, 12);
            Assert.Equal("a'b", completeEscape.Prefix);
            Assert.True(completeEscape.HasReliablePrefix);
            var splitEscape = Locate(text, 10);
            Assert.False(splitEscape.HasReliablePrefix);
            Assert.Equal(7, splitEscape.Start);
            Assert.Equal(8, splitEscape.Length);
        }

        [Fact]
        public void ExactOffsetsIncludeCrLfAndSupplementaryCharacters()
        {
            const string text = "Name = '\U0001F600'\r\nand StaTus = 'a'";
            var position = text.IndexOf("StaTus", StringComparison.Ordinal) + 3;
            var cursor = Locate(text, position);
            Assert.Equal(text.IndexOf("StaTus", StringComparison.Ordinal), cursor.Start);
            Assert.Equal(6, cursor.Length);
            Assert.Equal("Name = '\U0001F600'\r\nand Status = 'a'",
                text.Substring(0, cursor.Start) + "Status" + text.Substring(cursor.Start + cursor.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => Locate(text, 9));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(6)]
        public void InvalidPositionsFailExplicitly(int position) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => Locate("Price", position));

        private static ExpressionCompletionCursor Locate(string text, int position) =>
            ExpressionCompletionCursor.Locate(text, position, Interpreter.AnalyzeExpression(text).Tokens);
    }
}
