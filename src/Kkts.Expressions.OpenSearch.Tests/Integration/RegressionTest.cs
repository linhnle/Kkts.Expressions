using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    [Trait("Provider", "OpenSearch")]
    [Collection(OpenSearchCollection.Name)]
    public sealed class RegressionTest : EquivalenceTestBase
    {
        public RegressionTest(OpenSearchFixture fixture) : base(fixture)
        {
        }

        public static TheoryData<double> DoubleBoundaries => new TheoryData<double>
        {
            -2147483648.5, -2147483648, -5.5, -5, -4.5, 0, 0.5, 1, 1.5, 2, 2.5, 2147483646.5, 2147483647, 2147483647.5, 2147483648,
            9007199254740991, 9007199254740992, 9007199254740994, -9007199254740992, -9007199254740994,
            9.2233720368547758E18, -9.2233720368547758E18, 1e19, -1e19
        };

        [Theory]
        [MemberData(nameof(DoubleBoundaries))]
        public async Task ConvertedDoubleComparisons(double value)
        {
            await AssertSameIds(x => x.Count == value);
            await AssertSameIds(x => x.Count != value);
            await AssertSameIds(x => x.Count < value);
            await AssertSameIds(x => x.Count <= value);
            await AssertSameIds(x => x.Count > value);
            await AssertSameIds(x => x.Count >= value);
            await AssertSameIds(x => x.Big == value);
            await AssertSameIds(x => x.Big != value);
            await AssertSameIds(x => x.Big < value);
            await AssertSameIds(x => x.Big <= value);
            await AssertSameIds(x => x.Big > value);
            await AssertSameIds(x => x.Big >= value);
            await AssertSameIds(x => x.Rating == value);
            await AssertSameIds(x => x.Rating != value);
            await AssertSameIds(x => !(x.Rating > value));
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("2")]
        [InlineData("-4.5")]
        [InlineData("9007199254740992.5")]
        [InlineData("9223372036854775807")]
        [InlineData("9223372036854775807.5")]
        [InlineData("-9223372036854775808.5")]
        public async Task ConvertedDecimalAndFloatComparisons(string text)
        {
            var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            var single = (float)value;
            await AssertSameIds(x => x.Count > value);
            await AssertSameIds(x => x.Count == value);
            await AssertSameIds(x => x.Big >= value);
            await AssertSameIds(x => x.Big != value);
            await AssertSameIds(x => x.Big < value);
            await AssertSameIds(x => x.Count <= single);
            await AssertSameIds(x => x.Big == single);
            await AssertSameIds(x => x.Big > single);
        }

        [Fact]
        public async Task ConvertedMembership()
        {
            var doubles = new[] { 1.0, 2.5, 3.0, 9007199254740992 };
            var withNull = new double?[] { null, 1.0, 1.5 };
            var decimals = new[] { 2m, 2.5m, -5m };
            await AssertSameIds(x => doubles.Contains(x.Count));
            await AssertSameIds(x => !doubles.Contains(x.Count));
            await AssertSameIds(x => doubles.Contains(x.Big));
            await AssertSameIds(x => !doubles.Contains(x.Big));
            await AssertSameIds(x => withNull.Contains(x.Rating));
            await AssertSameIds(x => !withNull.Contains(x.Rating));
            await AssertSameIds(x => decimals.Contains(x.Count));
            await AssertSameIds(x => x.Numbers.Any(n => doubles.Contains(n)), x => x.Numbers != null && x.Numbers.Any(n => doubles.Contains(n)));
        }

        [Fact]
        public async Task PresenceMarkersAcrossObjectShapes()
        {
            // Meta shapes: populated (1, 5, 7), all-null (4), empty object (3), null (2, 6).
            Assert.Contains(Fixture.Items, x => x.IndexMetaAsEmptyObject && x.Meta != null);
            Assert.Contains(Fixture.Items, x => !x.IndexMetaAsEmptyObject && x.Meta != null && x.Meta.Label == null && x.Meta.Score == null);
            await AssertSameIds(x => x.Meta == null);
            await AssertSameIds(x => x.Meta != null);
            await AssertSameIds(x => !(x.Meta == null));
            await AssertSameIds(x => x.Meta != null && x.Meta.Label == null, x => x.Meta != null && x.Meta.Label == null);
            await AssertSameIds(x => x.Meta == null || x.Meta.Score > 1, x => x.Meta == null || x.Meta.Score > 1);
            await AssertSameIds(x => x.Lines.Any(l => l.Product == null), x => x.Lines != null && x.Lines.Any(l => l.Product == null));
            await AssertSameIds(x => x.Lines.Any(l => l.Product != null), x => x.Lines != null && x.Lines.Any(l => l.Product != null));
            await AssertSameIds(x => x.Lines.Any(l => l.Product != null && l.Product.Name == null),
                x => x.Lines != null && x.Lines.Any(l => l.Product != null && l.Product.Name == null));
        }

        [Fact]
        public void FlattenedNullElementMembership_EmitsNoQuery()
        {
            Expression<Func<Item, bool>>[] rejected =
            {
                x => x.Values.Contains(null),
                x => x.Tags.Contains(null),
                x => x.Values.Any(v => new int?[] { null, 7 }.Contains(v)),
                x => x.Parts.Any(p => new[] { null, "nut" }.Contains(p.Kind)),
                x => x.Id == 1 || x.Values.Contains(null)
            };

            foreach (var predicate in rejected)
            {
                var result = Translator.TranslateSearch(predicate, pagination: new Pagination());
                Assert.False(result.Succeeded, predicate.ToString());
                Assert.Null(result.Value);
                Assert.Equal(OpenSearchDiagnosticCodes.FlattenedCollection, Assert.Single(result.Diagnostics).Code);
            }
        }
    }
}
