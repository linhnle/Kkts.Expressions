using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Internal;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class MappingTest
    {
        private static ResolvedMember Resolve<TField>(OpenSearchMapping<Data> mapping, Expression<Func<Data, TField>> selector) =>
            mapping.Resolve(SelectorParser.Parse(selector, "selector"));

        [Fact]
        public void Default_HasDocumentedSettings()
        {
            var mapping = OpenSearchMapping<Data>.Default;

            Assert.Same(mapping, OpenSearchMapping<Data>.Default);
            Assert.Equal(typeof(Data), mapping.ElementType);
            Assert.Same(JsonNamingPolicy.CamelCase, mapping.NamingPolicy);
            Assert.Equal(StringFieldKind.Default, mapping.DefaultStringField);
            Assert.Equal(StringFieldKind.Text("keyword"), mapping.DefaultStringField);
            Assert.Equal(EnumFormat.Number, mapping.EnumFormat);
            Assert.Equal(10000, mapping.MaxResultWindow);
        }

        [Fact]
        public void CreateDefault_ReturnsMappingForType()
        {
            var mapping = OpenSearchMapping.CreateDefault(typeof(Data));
            Assert.Equal(typeof(Data), mapping.ElementType);
            Assert.Throws<ArgumentNullException>(() => OpenSearchMapping.CreateDefault(null));
        }

        [Fact]
        public void Builder_CannotBeUsedAfterBuild()
        {
            var builder = OpenSearchMapping.For<Data>().MaxResultWindow(50);
            var mapping = builder.Build();

            Assert.Equal(50, mapping.MaxResultWindow);
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.MaxResultWindow(10));
            Assert.Throws<InvalidOperationException>(() => builder.Field(x => x.Name, f => f.Keyword()));
            Assert.Equal(50, mapping.MaxResultWindow);
        }

        [Fact]
        public void Builder_RejectsInvalidSettings()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => OpenSearchMapping.For<Data>().MaxResultWindow(0));
            Assert.Throws<ArgumentNullException>(() => OpenSearchMapping.For<Data>().DefaultStringField(null));
            Assert.ThrowsAny<ArgumentException>(() => StringFieldKind.Text(""));
            Assert.ThrowsAny<ArgumentException>(() => StringFieldKind.Text("a.b"));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Converter<int?>(v => 1));
        }

        [Fact]
        public void Builder_RejectsInvalidSelectors()
        {
            var other = Expression.Parameter(typeof(Data), "other");
            var foreign = Expression.Lambda<Func<Data, string>>(Expression.Property(other, nameof(Data.Name)), Expression.Parameter(typeof(Data), "x"));

            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x.Name + "a", f => f.Keyword()));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x.NullableInteger.Value, f => f.Name("v")));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x.Name.Length, f => f.Name("len")));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x, f => f.Name("self")));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(foreign, f => f.Keyword()));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x.Lines.First().Sku, f => f.Keyword()));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => DateTime.Now, f => f.Name("now")));
            Assert.Throws<ArgumentNullException>(() => OpenSearchMapping.For<Data>().Field<string>(null, f => f.Keyword()));
        }

        [Fact]
        public void Builder_RejectsDuplicateOrConflictingFieldRegistrations()
        {
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>()
                .Field(x => x.Name, f => f.Keyword())
                .Field(x => x.Name, f => f.CaseInsensitive()));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>().Field(x => x.Name, f => f.Keyword().TextOnly()));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>().Field(x => x.Id, f => f.Keyword()));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>().Field(x => x.Id, f => f.CaseInsensitive()));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>().Field(x => x.Id, f => f.EnumFormat(EnumFormat.Name)));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>().Field(x => x.Id, f => f.Converter<string>(v => v)));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Field(x => x.Id, f => f.Name("a..b")));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>()
                .Converter<TimeSpan>(v => v.Ticks)
                .Converter<TimeSpan>(v => v.TotalSeconds));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Field(x => x.Name, f => f.Name("same"))
                .Field(x => x.Code, f => f.Name("same"))
                .Build());
        }

        [Fact]
        public void Builder_RejectsInvalidNestedDeclarations()
        {
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Nested(x => x.Id));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Nested(x => x.Tags));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Nested(x => x.Name));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>().Nested(x => x.Lines).Nested(x => x.Lines));
            // Overriding a nested path's child outside the nested path would change nested scoping.
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Nested(x => x.Customer)
                .Field(x => x.Customer.Name, f => f.Name("customerName"))
                .Build());
        }

        [Fact]
        public void Builder_RejectsInvalidPresenceMarkers()
        {
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Presence(x => x.Name, "namePresent"));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Presence(x => x.Lines, "linesPresent"));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Presence(x => x.Customer, ""));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Presence(x => x.Customer, "a..b"));
            Assert.ThrowsAny<ArgumentException>(() => OpenSearchMapping.For<Data>().Presence(x => x.Customer, null));
            Assert.Throws<ArgumentException>(() => OpenSearchMapping.For<Data>()
                .Presence(x => x.Customer, "customerPresent")
                .Presence(x => x.Customer, "customerPresent2"));
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Presence(x => x.Customer, "present")
                .Presence(x => x.Meta, "present")
                .Build());
            // A nested object uses nested presence queries instead of markers.
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Nested(x => x.Customer)
                .Presence(x => x.Customer, "customerPresent")
                .Build());
            // A marker under a nested path must live inside that nested path.
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Nested(x => x.Lines, l => l.Presence(e => e.Product, "productPresent"))
                .Build());
            Assert.Throws<InvalidOperationException>(() => OpenSearchMapping.For<Data>()
                .Nested(x => x.Lines, l => l.Presence(e => e.Product, "other.productPresent"))
                .Build());

            var valid = OpenSearchMapping.For<Data>()
                .Nested(x => x.Lines, l => l.Presence(e => e.Product, "lines.productPresent"))
                .Presence(x => x.Customer, "customerPresent")
                .Build();
            Assert.Equal("customerPresent", Resolve(valid, x => x.Customer).PresencePath);
        }

        [Fact]
        public void Resolve_DefaultCamelCaseMultiSegmentPaths()
        {
            var mapping = OpenSearchMapping<Data>.Default;

            var city = Resolve(mapping, x => x.Customer.Address.City);
            Assert.Equal("customer.address.city", city.Path);
            Assert.Equal("customer.address.city.keyword", city.ExactPath);
            Assert.Equal("Customer.Address.City", city.MemberPath);
            Assert.Equal("nullableInteger", Resolve(mapping, x => x.NullableInteger).Path);
            Assert.Equal("lines.sku", Resolve(mapping, x => x.Lines).Path + "." + "sku");
        }

        [Fact]
        public void Resolve_AttributeAndOverridePrecedence()
        {
            var mapping = OpenSearchMapping.For<Data>()
                .Field(x => x.Created, f => f.Name("meta.created"))
                .Field(x => x.Customer.Address, f => f.Name("addr"))
                .Build();

            Assert.Equal("full_name", Resolve(mapping, x => x.FullName).Path);
            Assert.Equal("meta.created", Resolve(mapping, x => x.Created).Path);
            Assert.Equal("addr.city", Resolve(mapping, x => x.Customer.Address.City).Path);
            Assert.Equal("customer.name", Resolve(mapping, x => x.Customer.Name).Path);

            var overridden = OpenSearchMapping.For<Data>().Field(x => x.FullName, f => f.Name("name_full")).Build();
            Assert.Equal("name_full", Resolve(overridden, x => x.FullName).Path);
        }

        [Fact]
        public void Resolve_NamingPolicy()
        {
            var snake = OpenSearchMapping.For<Data>().NamingPolicy(JsonNamingPolicy.SnakeCaseLower).Build();
            Assert.Equal("nullable_integer", Resolve(snake, x => x.NullableInteger).Path);
            Assert.Equal("full_name", Resolve(snake, x => x.FullName).Path);

            var clr = OpenSearchMapping.For<Data>().NamingPolicy(null).Build();
            Assert.Equal("Customer.Address.City", Resolve(clr, x => x.Customer.Address.City).Path);
        }

        [Fact]
        public void Resolve_StringFieldKinds()
        {
            var mapping = OpenSearchMapping.For<Data>()
                .DefaultStringField(StringFieldKind.Keyword)
                .Field(x => x.Name, f => f.Text("raw"))
                .Field(x => x.Description, f => f.TextOnly())
                .Field(x => x.Code, f => f.CaseInsensitive())
                .Build();

            Assert.Equal("code", Resolve(mapping, x => x.Code).ExactPath);
            Assert.True(Resolve(mapping, x => x.Code).CaseInsensitive);
            Assert.Equal("name.raw", Resolve(mapping, x => x.Name).ExactPath);
            Assert.Null(Resolve(mapping, x => x.Description).ExactPath);
            Assert.Equal("description", Resolve(mapping, x => x.Description).Path);
            Assert.Equal("tags", Resolve(mapping, x => x.Tags).ExactPath);
            Assert.Equal("full_name.keyword", Resolve(OpenSearchMapping<Data>.Default, x => x.FullName).ExactPath);
        }

        [Fact]
        public void Resolve_NestedPathDetection()
        {
            var mapping = OpenSearchMapping.For<Data>()
                .Nested(x => x.Customer, c => c.Nested(e => e.Contacts))
                .Nested(x => x.Lines, l => l.Nested(e => e.Taxes))
                .Build();

            var email = mapping.Resolve(SelectorParser.Parse((Expression<Func<Contact, string>>)(c => c.Email), "s")
                .Concat(SelectorParser.Parse((Expression<Func<Data, Customer>>)(x => x.Customer), "s").Append(typeof(Customer).GetProperty(nameof(Customer.Contacts)))));
            Assert.NotNull(email);

            var customer = Resolve(mapping, x => x.Customer);
            Assert.True(customer.IsNested);
            Assert.Empty(customer.EnclosingNested);

            var name = Resolve(mapping, x => x.Customer.Name);
            Assert.False(name.IsNested);
            Assert.Equal(new[] { "customer" }, name.EnclosingNested.Select(s => s.Path));

            var lines = Resolve(mapping, x => x.Lines);
            Assert.True(lines.IsNested);
            Assert.True(lines.IsCollection);
            Assert.Equal(typeof(OrderLine), lines.ValueType);

            var contacts = Resolve(mapping, x => x.Customer.Contacts);
            Assert.True(contacts.IsNested);
            Assert.Equal(new[] { "customer" }, contacts.EnclosingNested.Select(s => s.Path));

            Assert.False(Resolve(OpenSearchMapping<Data>.Default, x => x.Customer).IsNested);
        }

        [Fact]
        public void Resolve_ReusesCachePerMapping()
        {
            var mapping = OpenSearchMapping.For<Data>().Build();
            var translator = new OpenSearchQueryTranslator<Data>(mapping);

            translator.TranslatePredicate(x => x.Name == "a" && x.Id > 1);
            var count = mapping.CachedMemberCount;
            translator.TranslatePredicate(x => x.Name == "b" || x.Id < 3);

            Assert.Equal(count, mapping.CachedMemberCount);
            Assert.Same(Resolve(mapping, x => x.Name), Resolve(mapping, x => x.Name));
            Assert.Equal(0, OpenSearchMapping.For<Data>().Build().CachedMemberCount);
        }

        [Fact]
        public void Resolve_ConvertersAndEnumFormat()
        {
            Func<TimeSpan, JsonNode> fieldConverter = v => v.Ticks;
            var mapping = OpenSearchMapping.For<Data>()
                .EnumFormat(EnumFormat.Name)
                .Converter<TimeSpan>(v => v.TotalSeconds)
                .Field(x => x.NullableStatus, f => f.EnumFormat(EnumFormat.Number))
                .Build();

            Assert.Equal(EnumFormat.Name, Resolve(mapping, x => x.Status).EnumFormat);
            Assert.Equal(EnumFormat.Number, Resolve(mapping, x => x.NullableStatus).EnumFormat);
            Assert.NotNull(Resolve(mapping, x => x.Duration).Converter);
        }
    }
}
