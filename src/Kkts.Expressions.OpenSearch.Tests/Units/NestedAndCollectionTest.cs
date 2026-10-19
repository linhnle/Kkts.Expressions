using System.Linq;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class NestedAndCollectionTest : PredicateTestBase
    {
        private static readonly OpenSearchMapping<Data> NestedMapping = OpenSearchMapping.For<Data>()
            .Nested(x => x.Customer, c => c.Nested(e => e.Contacts))
            .Nested(x => x.Lines, l => l.Nested(e => e.Taxes).Presence(e => e.Product, "lines.productPresent"))
            .Presence(x => x.Meta, "metaPresent")
            .Build();

        private static readonly OpenSearchMapping<Data> PresenceMapping = OpenSearchMapping.For<Data>()
            .Presence(x => x.Customer, "customerPresent")
            .Presence(x => x.Customer.Address, "customer.addressPresent")
            .Build();

        // 5.1 single-valued nested objects

        [Fact]
        public void NestedObject_LeafIsWrapped()
        {
            AssertClause(x => x.Customer.Name == "a",
                "{\"nested\":{\"path\":\"customer\",\"query\":{\"term\":{\"customer.name.keyword\":\"a\"}}}}", NestedMapping);
            AssertClause(x => x.Customer.Address.City == "Hanoi",
                "{\"nested\":{\"path\":\"customer\",\"query\":{\"term\":{\"customer.address.city.keyword\":\"Hanoi\"}}}}", NestedMapping);
        }

        [Fact]
        public void NegationOutsideNested()
        {
            var interpreted = Interpreter.ParsePredicate<Data>("Customer.Name != 'a'").Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>(NestedMapping).TranslatePredicate(interpreted),
                "{\"bool\":{\"must_not\":[{\"nested\":{\"path\":\"customer\",\"query\":{\"term\":{\"customer.name.keyword\":\"a\"}}}}]}}");
            AssertClause(x => !(x.Customer.Age > 3),
                "{\"bool\":{\"must_not\":[{\"nested\":{\"path\":\"customer\",\"query\":{\"range\":{\"customer.age\":{\"gt\":3}}}}}]}}", NestedMapping);
            AssertClause(x => x.Customer.Name == null,
                "{\"bool\":{\"must_not\":[{\"nested\":{\"path\":\"customer\",\"query\":{\"exists\":{\"field\":\"customer.name\"}}}}]}}", NestedMapping);
        }

        [Fact]
        public void NestedInNestedChains()
        {
            AssertClause(x => x.Customer.Contacts.Any(c => c.Email == "e"),
                "{\"nested\":{\"path\":\"customer\",\"query\":{\"nested\":{\"path\":\"customer.contacts\",\"query\":{\"term\":{\"customer.contacts.email.keyword\":\"e\"}}}}}}",
                NestedMapping);
        }

        [Fact]
        public void NestedObjectNullChecks()
        {
            AssertClause(x => x.Customer == null,
                "{\"bool\":{\"must_not\":[{\"nested\":{\"path\":\"customer\",\"query\":{\"match_all\":{}}}}]}}", NestedMapping);
            AssertClause(x => x.Customer != null,
                "{\"nested\":{\"path\":\"customer\",\"query\":{\"match_all\":{}}}}", NestedMapping);
        }

        // 5.1a presence markers

        [Fact]
        public void PresenceMarkers_TranslateToMarkerTerms()
        {
            AssertClause(x => x.Customer == null, "{\"term\":{\"customerPresent\":false}}", PresenceMapping);
            AssertClause(x => x.Customer != null, "{\"term\":{\"customerPresent\":true}}", PresenceMapping);
            AssertClause(x => null != x.Customer.Address, "{\"term\":{\"customer.addressPresent\":true}}", PresenceMapping);
            AssertClause(x => !(x.Customer == null), "{\"bool\":{\"must_not\":[{\"term\":{\"customerPresent\":false}}]}}", PresenceMapping);
            AssertClause(x => x.Meta == null, "{\"term\":{\"metaPresent\":false}}", NestedMapping);
        }

        [Fact]
        public void PresenceMarkers_InsideNestedScopes()
        {
            AssertClause(x => x.Lines.Any(l => l.Product == null),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"term\":{\"lines.productPresent\":false}}}}", NestedMapping);
            AssertClause(x => x.Lines.Any(l => l.Product != null && l.Product.Name == "p"),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"bool\":{\"filter\":[{\"term\":{\"lines.productPresent\":true}},{\"term\":{\"lines.product.name.keyword\":\"p\"}}]}}}}",
                NestedMapping);
        }

        [Theory]
        [InlineData("null", false, true)]
        [InlineData("empty", true, false)]
        [InlineData("all-null", true, false)]
        [InlineData("populated", true, false)]
        public void PresenceMarkers_DistinguishNullEmptyAllNullAndPopulated(string shape, bool marker, bool isNull)
        {
            // Child fields are deliberately absent for every non-populated shape; only the marker decides.
            var document = shape == "populated"
                ? Doc(("customerPresent", marker), ("customer.name.keyword", "n"))
                : Doc(("customerPresent", marker));
            var customer = shape == "null" ? null : shape == "populated" ? new Customer { Name = "n" } : new Customer();

            Assert.Equal(isNull, Matches(Translate(x => x.Customer == null, PresenceMapping), document));
            Assert.Equal(!isNull, Matches(Translate(x => x.Customer != null, PresenceMapping), document));
            Assert.Equal(isNull, new Data { Customer = customer }.Customer == null);
        }

        [Fact]
        public void UnconfiguredObjectNullCheck_Fails()
        {
            var diagnostic = AssertFails(x => x.Customer == null, OpenSearchDiagnosticCodes.PresenceMarkerRequired);
            Assert.Equal("Customer", diagnostic.MemberPath);
            AssertFails(x => x.Customer.Address != null, OpenSearchDiagnosticCodes.PresenceMarkerRequired, OpenSearchMapping.For<Data>().Presence(x => x.Customer, "customerPresent").Build());
            AssertFails(x => x.Lines.Any(l => l.Product == null), OpenSearchDiagnosticCodes.PresenceMarkerRequired,
                OpenSearchMapping.For<Data>().Nested(x => x.Lines).Build());
        }

        // 5.2 Any over nested collections

        [Fact]
        public void SameElementMatching()
        {
            AssertClause(x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"bool\":{\"filter\":[{\"term\":{\"lines.sku.keyword\":\"A\"}},{\"range\":{\"lines.quantity\":{\"gt\":2}}}]}}}}",
                NestedMapping);
        }

        [Fact]
        public void AnyWithoutPredicate()
        {
            AssertClause(x => x.Lines.Any(), "{\"nested\":{\"path\":\"lines\",\"query\":{\"match_all\":{}}}}", NestedMapping);
            AssertClause(x => !x.Lines.Any(), "{\"bool\":{\"must_not\":[{\"nested\":{\"path\":\"lines\",\"query\":{\"match_all\":{}}}}]}}", NestedMapping);
        }

        [Fact]
        public void NestedInNestedAny_SuppressesOpenedPaths()
        {
            AssertClause(x => x.Lines.Any(l => l.Sku == "A" && l.Taxes.Any(t => t.Code == "VAT" && t.Rate > 0.1m)),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"bool\":{\"filter\":[" +
                "{\"term\":{\"lines.sku.keyword\":\"A\"}}," +
                "{\"nested\":{\"path\":\"lines.taxes\",\"query\":{\"bool\":{\"filter\":[{\"term\":{\"lines.taxes.code.keyword\":\"VAT\"}},{\"range\":{\"lines.taxes.rate\":{\"gt\":0.1}}}]}}}}" +
                "]}}}}",
                NestedMapping);
            AssertClause(x => x.Lines.Any(l => l.Taxes.Any()),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"nested\":{\"path\":\"lines.taxes\",\"query\":{\"match_all\":{}}}}}}", NestedMapping);
        }

        [Fact]
        public void NestedElementScope_SupportsNegationNullAndScalarCollections()
        {
            AssertClause(x => x.Lines.Any(l => l.Sku != "A" && l.Discount == null),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"bool\":{\"filter\":[{\"bool\":{\"must_not\":[{\"term\":{\"lines.sku.keyword\":\"A\"}}]}},{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"lines.discount\"}}]}}]}}}}",
                NestedMapping);
            AssertClause(x => x.Lines.Any(l => l.Labels.Contains("x")),
                "{\"nested\":{\"path\":\"lines\",\"query\":{\"term\":{\"lines.labels.keyword\":\"x\"}}}}", NestedMapping);
        }

        [Fact]
        public void AnyOnlyReferencesCurrentElement()
        {
            AssertFails(x => x.Lines.Any(l => x.Id == 1), OpenSearchDiagnosticCodes.UnsupportedExpression, NestedMapping);
            AssertFails(x => x.Lines.Any(l => l.Taxes.Any(t => l.Sku == "a")), OpenSearchDiagnosticCodes.UnsupportedExpression, NestedMapping);
        }

        // 5.3 non-nested collections

        [Fact]
        public void ScalarArray()
        {
            AssertClause(x => x.Tags.Contains("red"), "{\"term\":{\"tags.keyword\":\"red\"}}");
            AssertClause(x => x.Numbers.Contains(3), "{\"term\":{\"numbers\":3}}");
            AssertClause(x => x.Values.Contains(3), "{\"term\":{\"values\":3}}");
            AssertClause(x => x.Tags.Any(t => t == "red"), "{\"term\":{\"tags.keyword\":\"red\"}}");
            AssertClause(x => x.Tags.Any(t => t.StartsWith("r")), "{\"prefix\":{\"tags.keyword\":\"r\"}}");
            AssertClause(x => x.Numbers.Any(n => n > 3), "{\"range\":{\"numbers\":{\"gt\":3}}}");
            AssertClause(x => x.Numbers.Any(n => new[] { 1, 2 }.Contains(n)), "{\"terms\":{\"numbers\":[1,2]}}");
            AssertClause(x => x.Lines.Any(l => l.Sku == "A"), "{\"term\":{\"lines.sku.keyword\":\"A\"}}");
            AssertClause(x => !x.Tags.Contains("red"), "{\"bool\":{\"must_not\":[{\"term\":{\"tags.keyword\":\"red\"}}]}}");
        }

        [Fact]
        public void UnsafeFlattenedPredicates_Fail()
        {
            var diagnostic = AssertFails(x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2), OpenSearchDiagnosticCodes.FlattenedCollection);
            Assert.Contains("nested", diagnostic.Message);
            Assert.Equal("Lines", diagnostic.MemberPath);
            AssertFails(x => x.Tags.Any(t => t != "red"), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Tags.Any(t => !t.StartsWith("r")), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Tags.Any(t => t == null), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Tags.Any(t => t == "a" || t == "b"), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Lines.Any(l => l.Discount == null), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Lines.Any(), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Tags.Any(), OpenSearchDiagnosticCodes.FlattenedCollection);
        }

        [Fact]
        public void FlattenedNullElementMembership_Fails()
        {
            AssertFails(x => x.Values.Contains(null), OpenSearchDiagnosticCodes.FlattenedCollection);
            AssertFails(x => x.Tags.Contains(null), OpenSearchDiagnosticCodes.FlattenedCollection);
            var diagnostic = AssertFails(x => x.Values.Any(v => new int?[] { null, 1 }.Contains(v)), OpenSearchDiagnosticCodes.FlattenedCollection);
            Assert.Contains("null", diagnostic.Message);
            AssertFails(x => x.Values.Contains(null) || x.Id == 1, OpenSearchDiagnosticCodes.FlattenedCollection);
        }

        [Fact]
        public void ScalarNullableMembershipAndNestedNullChecks_AreRetained()
        {
            var interpreted = Interpreter.ParsePredicate<Data>("NullableInteger in [null, 1]").Result;
            Assert.True(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted).Succeeded);
            Assert.True(Translate(x => x.Lines.Any(l => l.Discount == null), NestedMapping).Succeeded);
            Assert.True(Translate(x => x.Lines.Any(l => new int?[] { null, 1 }.Contains(l.Discount)), NestedMapping).Succeeded);
        }
    }
}
