using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionContractTest
    {
        [Fact]
        public void ResultsOwnReadOnlySnapshotsAndReusePublicTypeMetadata()
        {
            var type = ExpressionTypeInfo.ForClr(typeof(decimal), ExpressionNullability.NonNullable);
            var item = new ExpressionCompletionItem(
                ExpressionCompletionKind.Field, "Total amount", "total", 2, 3, "Public amount.", type);
            var items = new List<ExpressionCompletionItem> { item };
            var diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic, "completion-context-unavailable", "Unknown context.", 5, 0);
            var diagnostics = new List<ExpressionDiagnostic> { diagnostic };
            var result = new ExpressionCompletionResult(items, diagnostics, ExpressionCompletionStatus.Available, true);
            items.Clear();
            diagnostics.Clear();

            Assert.Same(item, Assert.Single(result.Items));
            Assert.Same(diagnostic, Assert.Single(result.Diagnostics));
            Assert.Throws<NotSupportedException>(() => ((IList<ExpressionCompletionItem>)result.Items).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<ExpressionDiagnostic>)result.Diagnostics).Clear());
            Assert.Equal("Total amount", item.Label);
            Assert.Equal("total", item.InsertionText);
            Assert.Equal((2, 3), (item.Start, item.Length));
            Assert.Equal("Public amount.", item.Description);
            Assert.Equal(typeof(decimal), item.TypeInfo.ClrType);
            Assert.Equal(ExpressionNullability.NonNullable, item.TypeInfo.Nullability);
            Assert.True(result.IsIncomplete);
            Assert.All(new[]
            {
                typeof(ExpressionCompletionItem), typeof(ExpressionCompletionResult), typeof(ExpressionCompletionOptions)
            }, contract => Assert.All(contract.GetProperties(), property => Assert.False(property.CanWrite)));
        }

        [Fact]
        public void UntypedConstructsHaveNoInventedType()
        {
            var item = new ExpressionCompletionItem(ExpressionCompletionKind.Delimiter, ")", ")", 0, 0);
            Assert.Null(item.TypeInfo);
            Assert.Equal("", item.Description);
            var collection = ExpressionTypeInfo.ForClr(
                typeof(int[]), ExpressionNullability.Unknown, typeof(int));
            Assert.Equal(typeof(int), collection.ElementType);
            Assert.Equal(ExpressionTypeKind.Null, ExpressionTypeInfo.Null().Kind);
        }

        [Fact]
        public void EnumValuesAreExplicitAndStable()
        {
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 },
                Enum.GetValues(typeof(ExpressionCompletionKind)).Cast<ExpressionCompletionKind>().Select(value => (int)value));
            Assert.Equal(new[] { 0, 1, 2, 3 },
                Enum.GetValues(typeof(ExpressionCompletionStatus)).Cast<ExpressionCompletionStatus>().Select(value => (int)value));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        [InlineData(200)]
        public void OptionsAcceptDocumentedBounds(int maximum)
        {
            Assert.Equal(maximum, new ExpressionCompletionOptions(maximum).MaxResults);
            Assert.Equal(50, new ExpressionCompletionOptions().MaxResults);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(201)]
        public void OptionsRejectInvalidBounds(int maximum) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExpressionCompletionOptions(maximum));

        [Fact]
        public void InternalConstructionRejectsInvalidContracts()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ExpressionCompletionItem(ExpressionCompletionKind.Field, null, "Price", 0, 0));
            Assert.Throws<ArgumentNullException>(() =>
                new ExpressionCompletionItem(ExpressionCompletionKind.Field, "Price", null, 0, 0));
            Assert.Throws<ArgumentNullException>(() =>
                new ExpressionCompletionItem(ExpressionCompletionKind.Field, "Price", "Price", 0, 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ExpressionCompletionItem(ExpressionCompletionKind.Field, "Price", "Price", -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ExpressionCompletionItem(ExpressionCompletionKind.Field, "Price", "Price", 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ExpressionCompletionItem((ExpressionCompletionKind)99, "Price", "Price", 0, 0));
            Assert.Throws<ArgumentException>(() =>
                new ExpressionCompletionResult(new ExpressionCompletionItem[] { null },
                    Array.Empty<ExpressionDiagnostic>(), ExpressionCompletionStatus.NoMatches));
        }
    }
}
