using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    [Trait("Provider", "OpenSearch")]
    [Collection(OpenSearchCollection.Name)]
    public sealed class EquivalenceTest : EquivalenceTestBase
    {
        public EquivalenceTest(OpenSearchFixture fixture) : base(fixture)
        {
        }

        private static bool Ci(string value, Func<string, bool> test) => value != null && test(value.ToLowerInvariant());

        private static Expression<Func<Item, bool>> Parse(string expression) => Interpreter.ParsePredicate<Item>(expression).Result;

        [Fact]
        public async Task ComparisonOperators()
        {
            var created = new DateTime(2024, 1, 2, 0, 0, 0, 250, DateTimeKind.Utc);
            var closed = new DateTime(2024, 2, 15, 0, 0, 0, DateTimeKind.Utc);
            await AssertSameIds(x => x.Id == 1);
            await AssertSameIds(x => x.Id != 1);
            await AssertSameIds(x => x.Count > 1);
            await AssertSameIds(x => x.Count >= 2);
            await AssertSameIds(x => x.Count < 3);
            await AssertSameIds(x => x.Count <= int.MaxValue);
            await AssertSameIds(x => x.Big > 9007199254740992L);
            await AssertSameIds(x => x.Big <= -9007199254740993L);
            await AssertSameIds(x => x.Price >= 10.5m);
            await AssertSameIds(x => x.Price < 20m);
            await AssertSameIds(x => x.Weight > 0.1);
            await AssertSameIds(x => x.Weight == 2.25);
            await AssertSameIds(x => x.Created >= created);
            await AssertSameIds(x => x.Created == created);
            var third = new DateTime(2024, 1, 3, 0, 0, 0, 999, DateTimeKind.Utc);
            await AssertSameIds(x => x.Created < third);
            await AssertSameIds(x => x.Closed > closed);
            await AssertSameIds(x => x.Status == ItemStatus.Active);
            await AssertSameIds(x => x.Status != ItemStatus.Active);
            await AssertSameIds(x => x.Status > ItemStatus.Inactive);
            await AssertSameIds(x => x.Code == "c3");
            await AssertSameIds(x => x.Title == "Third");
            await AssertSameIds(x => x.Name == "alpha");
            await AssertSameIds(x => x.Name != "alpha");
            await AssertSameIds(x => x.Name == string.Empty);
        }

        [Fact]
        public async Task LogicAndConstants()
        {
            await AssertSameIds(x => (x.Id == 1 || x.Id == 2) && !(x.Count > 1));
            await AssertSameIds(x => x.Status == ItemStatus.Active && (x.Rating > 2 || x.Weight < 1) || x.Code == "g7");
            await AssertSameIds(x => !(x.Price > 10m && x.Price < 50m));
            await AssertSameIds(x => true);
            await AssertSameIds(x => false);
            await AssertSameIds(Parse("(Count >= 1 and Count <= 3) or not(Status = 1)"));
        }

        [Fact]
        public async Task NullCases()
        {
            await AssertSameIds(x => x.Rating == null);
            await AssertSameIds(x => x.Rating != null);
            await AssertSameIds(x => x.Rating != 5);
            await AssertSameIds(x => x.Rating > 3);
            await AssertSameIds(x => x.Rating <= 3);
            await AssertSameIds(x => !(x.Rating > 3));
            await AssertSameIds(x => x.Weight == null);
            await AssertSameIds(x => x.Closed == null);
            await AssertSameIds(x => x.Closed != null);
            await AssertSameIds(x => x.Name == null);
            await AssertSameIds(x => x.Name != null);
            await AssertSameIds(x => x.Body == null);
            await AssertSameIds(x => x.Body != null);
#pragma warning disable CS0464
            await AssertSameIds(x => x.Rating > null);
#pragma warning restore CS0464
            await AssertSameIds(Parse("Rating = null or Name = null"));
        }

        [Fact]
        public async Task Membership()
        {
            var ids = new List<int> { 1, 2, 9 };
            await AssertSameIds(x => ids.Contains(x.Id));
            await AssertSameIds(x => !new[] { 1, 2 }.Contains(x.Id));
            await AssertSameIds(x => new int?[] { null, 3 }.Contains(x.Rating));
            await AssertSameIds(x => !new int?[] { null, 3 }.Contains(x.Rating));
            var none = new int[0];
            await AssertSameIds(x => none.Contains(x.Id));
            await AssertSameIds(x => !none.Contains(x.Id));
            await AssertSameIds(x => new[] { "alpha", "Beta", "alpha" }.Contains(x.Name));
            await AssertSameIds(x => new[] { "alpha", null }.Contains(x.Name));
            await AssertSameIds(x => new[] { ItemStatus.Active, ItemStatus.Suspended }.Contains(x.Status));
            await AssertSameIds(Parse("Id in [1, 3, 5]"));
            await AssertSameIds(Parse("Id not in [1, 3, 5]"));
            await AssertSameIds(Parse("Rating in [null, 1]"));
        }

        [Fact]
        public async Task StringFunctions()
        {
            await AssertSameIds(x => x.Name.StartsWith("alpha"), x => x.Name != null && x.Name.StartsWith("alpha", StringComparison.Ordinal));
            await AssertSameIds(x => !x.Name.StartsWith("alpha"), x => !(x.Name != null && x.Name.StartsWith("alpha", StringComparison.Ordinal)));
            await AssertSameIds(x => x.Name.StartsWith("Alpha"), x => false);
            await AssertSameIds(x => x.Name.EndsWith("bet"), x => x.Name != null && x.Name.EndsWith("bet", StringComparison.Ordinal));
            await AssertSameIds(x => x.Name.Contains("ph"), x => x.Name != null && x.Name.Contains("ph"));
            await AssertSameIds(x => x.Name.Contains("*?\\"), x => x.Name != null && x.Name.Contains("*?\\"));
            await AssertSameIds(x => x.Name.Contains("?"), x => x.Name != null && x.Name.Contains("?"));
            await AssertSameIds(x => x.Name.Contains(""), x => x.Name != null);
            await AssertSameIds(x => x.Name.StartsWith(""), x => x.Name != null);
            await AssertSameIds(x => x.Title.EndsWith("title"), x => x.Title != null && x.Title.EndsWith("title", StringComparison.Ordinal));
            await AssertSameIds(Parse("Name.contains('alph') and not(Name.endsWith('a'))"),
                x => x.Name != null && x.Name.Contains("alph") && !x.Name.EndsWith("a", StringComparison.Ordinal));
        }

        [Fact]
        public async Task CaseInsensitiveField()
        {
            await AssertSameIds(x => x.Email == "ann@example.com", x => Ci(x.Email, e => e == "ann@example.com"));
            await AssertSameIds(x => x.Email != "ANN@EXAMPLE.COM", x => !Ci(x.Email, e => e == "ann@example.com"));
            await AssertSameIds(x => x.Email.StartsWith("CAROL"), x => Ci(x.Email, e => e.StartsWith("carol")));
            await AssertSameIds(x => x.Email.EndsWith("EXAMPLE.COM"), x => Ci(x.Email, e => e.EndsWith("example.com")));
            await AssertSameIds(x => x.Email.Contains("@Ex"), x => Ci(x.Email, e => e.Contains("@ex")));
            await AssertSameIds(x => new[] { "ANN@example.com", "Bob@Example.com" }.Contains(x.Email),
                x => Ci(x.Email, e => e == "ann@example.com" || e == "bob@example.com"));
            await AssertSameIds(x => x.Email == string.Empty, x => x.Email == string.Empty);
            await AssertSameIds(x => x.Email != string.Empty, x => x.Email != string.Empty);
            await AssertSameIds(x => new[] { "", "EVE@example.com" }.Contains(x.Email), x => x.Email == string.Empty || Ci(x.Email, e => e == "eve@example.com"));
        }

        [Fact]
        public async Task NestedObjects()
        {
            await AssertSameIds(x => x.Customer.Name == "ann", x => x.Customer != null && x.Customer.Name == "ann");
            await AssertSameIds(x => x.Customer.Name != "ann", x => !(x.Customer != null && x.Customer.Name == "ann"));
            await AssertSameIds(x => x.Customer.Age > 40, x => x.Customer != null && x.Customer.Age > 40);
            await AssertSameIds(x => !(x.Customer.Age > 40), x => !(x.Customer != null && x.Customer.Age > 40));
            await AssertSameIds(x => x.Customer.Name == null, x => !(x.Customer != null && x.Customer.Name != null));
            await AssertSameIds(x => x.Customer.Name.StartsWith("a"), x => x.Customer?.Name != null && x.Customer.Name.StartsWith("a", StringComparison.Ordinal));
            await AssertSameIds(x => x.Customer == null);
            await AssertSameIds(x => x.Customer != null);
            await AssertSameIds(x => x.Customer != null && x.Customer.Name == "ann" && x.Customer.Age < 50,
                x => x.Customer != null && x.Customer.Name == "ann" && x.Customer.Age < 50);
        }

        [Fact]
        public async Task NestedCollections()
        {
            Func<Item, IEnumerable<Line>> lines = x => x.Lines ?? new List<Line>();
            await AssertSameIds(x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2), x => lines(x).Any(l => l.Sku == "A" && l.Quantity > 2));
            await AssertSameIds(x => x.Lines.Any(l => l.Sku == "A") && x.Lines.Any(l => l.Quantity > 2),
                x => lines(x).Any(l => l.Sku == "A") && lines(x).Any(l => l.Quantity > 2));
            await AssertSameIds(x => x.Lines.Any(), x => lines(x).Any());
            await AssertSameIds(x => !x.Lines.Any(), x => !lines(x).Any());
            await AssertSameIds(x => x.Lines.Any(l => l.Sku != "A"), x => lines(x).Any(l => l.Sku != "A"));
            await AssertSameIds(x => !x.Lines.Any(l => l.Sku == "A"), x => !lines(x).Any(l => l.Sku == "A"));
            await AssertSameIds(x => x.Lines.Any(l => l.Discount == null), x => lines(x).Any(l => l.Discount == null));
            await AssertSameIds(x => x.Lines.Any(l => l.Discount >= 0.25m), x => lines(x).Any(l => l.Discount >= 0.25m));
            await AssertSameIds(x => x.Lines.Any(l => l.Product == null), x => lines(x).Any(l => l.Product == null));
            await AssertSameIds(x => x.Lines.Any(l => l.Product != null && l.Product.Name == null), x => lines(x).Any(l => l.Product != null && l.Product.Name == null));
            await AssertSameIds(x => x.Lines.Any(l => l.Product.Name == "pa"), x => lines(x).Any(l => l.Product != null && l.Product.Name == "pa"));
            await AssertSameIds(x => x.Lines.Any(l => l.Taxes.Any(t => t.Code == "VAT" && t.Rate > 0.15)),
                x => lines(x).Any(l => l.Taxes != null && l.Taxes.Any(t => t.Code == "VAT" && t.Rate > 0.15)));
            await AssertSameIds(x => x.Lines.Any(l => l.Sku == "C" && l.Taxes.Any(t => t.Code == "GST")),
                x => lines(x).Any(l => l.Sku == "C" && l.Taxes != null && l.Taxes.Any(t => t.Code == "GST")));
            await AssertSameIds(x => x.Lines.Any(l => !l.Taxes.Any()), x => lines(x).Any(l => l.Taxes == null || !l.Taxes.Any()));
            await AssertSameIds(x => x.Lines.Any(l => new[] { "B", "D" }.Contains(l.Sku)), x => lines(x).Any(l => l.Sku == "B" || l.Sku == "D"));
        }

        [Fact]
        public async Task FlattenedCollections()
        {
            await AssertSameIds(x => x.Tags.Contains("red"), x => x.Tags != null && x.Tags.Contains("red"));
            await AssertSameIds(x => !x.Tags.Contains("red"), x => !(x.Tags != null && x.Tags.Contains("red")));
            await AssertSameIds(x => x.Tags.Any(t => t.StartsWith("gr")), x => x.Tags != null && x.Tags.Any(t => t.StartsWith("gr", StringComparison.Ordinal)));
            await AssertSameIds(x => x.Tags.Any(t => new[] { "blue", "green" }.Contains(t)), x => x.Tags != null && x.Tags.Any(t => t == "blue" || t == "green"));
            await AssertSameIds(x => x.Numbers.Contains(2), x => x.Numbers != null && x.Numbers.Contains(2));
            await AssertSameIds(x => x.Numbers.Any(n => n > 4), x => x.Numbers != null && x.Numbers.Any(n => n > 4));
            await AssertSameIds(x => x.Values.Contains(1), x => x.Values != null && x.Values.Contains(1));
            await AssertSameIds(x => x.Values.Any(v => v >= 2), x => x.Values != null && x.Values.Any(v => v >= 2));
            await AssertSameIds(x => x.Parts.Any(p => p.Kind == "bolt"), x => x.Parts != null && x.Parts.Any(p => p.Kind == "bolt"));
            await AssertSameIds(x => x.Parts.Any(p => p.Size > 4), x => x.Parts != null && x.Parts.Any(p => p.Size > 4));
            await AssertSameIds(x => x.Parts.Any(p => new[] { "nut", "washer" }.Contains(p.Kind)), x => x.Parts != null && x.Parts.Any(p => p.Kind == "nut" || p.Kind == "washer"));
        }

        [Fact]
        public async Task MultiKeySortWithMissingValues()
        {
            var items = Fixture.Items;
            async Task Check(string orderBy, IEnumerable<Item> expected, Expression<Func<Item, bool>> predicate = null) =>
                await AssertSameOrder(expected, predicate ?? (x => true), orderBy.TryBuildOrderByClause(typeof(Item)).Result, new Pagination { Limit = 100 });

            await Check("Code desc", items.OrderByDescending(x => x.Code, StringComparer.Ordinal));
            await Check("Rating, Id", items.OrderBy(x => x.Rating).ThenBy(x => x.Id));
            await Check("Rating desc, Id desc", items.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Id));
            await Check("Weight, Code", items.OrderBy(x => x.Weight).ThenBy(x => x.Code, StringComparer.Ordinal));
            await Check("Closed desc, Created", items.OrderByDescending(x => x.Closed).ThenBy(x => x.Created));
            await Check("Status desc, Price", items.OrderByDescending(x => x.Status).ThenBy(x => x.Price));
            await Check("Big", items.OrderBy(x => x.Big));
            await Check("Title, Id", items.OrderBy(x => x.Title, StringComparer.Ordinal).ThenBy(x => x.Id));
            await Check("Customer.Age desc, Id", items.OrderByDescending(x => x.Customer?.Age).ThenBy(x => x.Id));
            await Check("Customer.Age, Id", items.OrderBy(x => x.Customer?.Age).ThenBy(x => x.Id));
            await Check("Count desc", items.Where(x => x.Rating != null).OrderByDescending(x => x.Count), x => x.Rating != null);
        }

        [Fact]
        public async Task PageWindows()
        {
            var ordered = Fixture.Items.OrderBy(x => x.Rating).ThenBy(x => x.Id).ToArray();
            var orderBy = "Rating, Id".TryBuildOrderByClause(typeof(Item)).Result;
            foreach (var (offset, limit) in new[] { (0, 3), (2, 3), (5, 3), (6, 10), (7, 5), (100, 2) })
            {
                await AssertSameOrder(ordered.Skip(offset).Take(limit), x => true, orderBy, new Pagination { Offset = offset, Limit = limit });
            }

            var page = new Pagination { PageSize = 2, Page = 3 };
            await AssertSameOrder(ordered.Skip(4).Take(2), x => true, orderBy, page);

            var condition = new ConditionOptions { Where = "Count >= 0", OrderBy = "Price desc" }.BuildCondition<Item>();
            var body = Translator.TranslateSearch(condition, new Pagination { Offset = 1, Limit = 2 });
            var expected = Fixture.Items.Where(x => x.Count >= 0).OrderByDescending(x => x.Price).Skip(1).Take(2).Select(x => x.Id);
            Assert.Equal(expected, await SearchRaw(body.Value));
        }
    }
}
