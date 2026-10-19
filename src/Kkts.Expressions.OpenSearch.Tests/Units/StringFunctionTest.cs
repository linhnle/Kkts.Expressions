using System.Linq;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class StringFunctionTest : PredicateTestBase
    {
        [Fact]
        public void Functions()
        {
            AssertClause(x => x.Name.StartsWith("Al"), "{\"prefix\":{\"name.keyword\":\"Al\"}}");
            AssertClause(x => x.Name.Contains("Al"), "{\"wildcard\":{\"name.keyword\":\"*Al*\"}}");
            AssertClause(x => x.Name.EndsWith("Al"), "{\"wildcard\":{\"name.keyword\":\"*Al\"}}");
            AssertClause(x => x.Name.Contains('A'), "{\"wildcard\":{\"name.keyword\":\"*A*\"}}");
            AssertClause(x => !x.Name.StartsWith("Al"), "{\"bool\":{\"must_not\":[{\"prefix\":{\"name.keyword\":\"Al\"}}]}}");
        }

        [Fact]
        public void EscapedWildcard()
        {
            var predicate = Interpreter.ParsePredicate<Data>("Name.contains('50%*off?')").Result;
            JsonAssert.Filter(new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate), "{\"wildcard\":{\"name.keyword\":\"*50%\\\\*off\\\\?*\"}}");
            AssertClause(x => x.Name.EndsWith("a\\b"), "{\"wildcard\":{\"name.keyword\":\"*a\\\\\\\\b\"}}");
            Assert.True(Matches(Translate(x => x.Name.Contains("50%*off?")), Doc(("name.keyword", "get 50%*off? now"))));
            Assert.False(Matches(Translate(x => x.Name.Contains("50%*off?")), Doc(("name.keyword", "get 50%Xoff! now"))));
        }

        [Fact]
        public void EmptyOperand_IsExists()
        {
            AssertClause(x => x.Name.StartsWith(""), "{\"exists\":{\"field\":\"name\"}}");
            AssertClause(x => x.Name.Contains(""), "{\"exists\":{\"field\":\"name\"}}");
            AssertClause(x => x.Name.EndsWith(""), "{\"exists\":{\"field\":\"name\"}}");
        }

        [Fact]
        public void NullOperand_Fails()
        {
            string value = null;
            AssertFails(x => x.Name.Contains(value), OpenSearchDiagnosticCodes.UnsupportedValue);
        }

        [Fact]
        public void CaseInsensitiveField()
        {
            var mapping = OpenSearchMapping.For<Data>().Field(x => x.Name, f => f.CaseInsensitive()).Build();

            AssertClause(x => x.Name == "abc", "{\"term\":{\"name.keyword\":{\"value\":\"abc\",\"case_insensitive\":true}}}", mapping);
            AssertClause(x => x.Name.StartsWith("ab"), "{\"prefix\":{\"name.keyword\":{\"value\":\"ab\",\"case_insensitive\":true}}}", mapping);
            AssertClause(x => x.Name.Contains("b"), "{\"wildcard\":{\"name.keyword\":{\"value\":\"*b*\",\"case_insensitive\":true}}}", mapping);
            Assert.True(Matches(Translate(x => x.Name == "abc", mapping), Doc(("name.keyword", "ABC"))));
            AssertClause(x => x.Name == "", "{\"term\":{\"name.keyword\":\"\"}}", mapping);
        }

        [Fact]
        public void CaseSensitiveDefault()
        {
            Assert.False(Matches(Translate(x => x.Name == "abc"), Doc(("name.keyword", "ABC"))));
            Assert.False(Matches(Translate(x => x.Name.StartsWith("ab")), Doc(("name.keyword", "ABC"))));
        }

        [Fact]
        public void KeywordAndCustomSubfield()
        {
            var mapping = OpenSearchMapping.For<Data>()
                .Field(x => x.Code, f => f.Keyword())
                .Field(x => x.Name, f => f.Text("raw"))
                .Build();

            AssertClause(x => x.Code == "A1", "{\"term\":{\"code\":\"A1\"}}", mapping);
            AssertClause(x => x.Name.StartsWith("Al"), "{\"prefix\":{\"name.raw\":\"Al\"}}", mapping);
            AssertClause(x => x.Name == null, "{\"bool\":{\"must_not\":[{\"exists\":{\"field\":\"name\"}}]}}", mapping);
            var globalKeyword = OpenSearchMapping.For<Data>().DefaultStringField(StringFieldKind.Keyword).Build();
            AssertClause(x => x.Name == "a", "{\"term\":{\"name\":\"a\"}}", globalKeyword);
        }

        [Fact]
        public void TextOnlyField_FailsExactOperations()
        {
            var mapping = OpenSearchMapping.For<Data>().Field(x => x.Description, f => f.TextOnly()).Build();
            var names = new[] { "a" };

            var diagnostic = AssertFails(x => x.Description == "a", OpenSearchDiagnosticCodes.TextFieldExactMatch, mapping);
            Assert.Equal("Description", diagnostic.MemberPath);
            Assert.Contains("keyword", diagnostic.Message);
            AssertFails(x => names.Contains(x.Description), OpenSearchDiagnosticCodes.TextFieldExactMatch, mapping);
            AssertFails(x => x.Description.StartsWith("a"), OpenSearchDiagnosticCodes.TextFieldExactMatch, mapping);
            AssertFails(x => x.Description.Contains("a"), OpenSearchDiagnosticCodes.TextFieldExactMatch, mapping);
            AssertClause(x => x.Description != null, "{\"exists\":{\"field\":\"description\"}}", mapping);
        }

        [Fact]
        public void UnsupportedStringOverloads_Fail()
        {
            AssertFails(x => x.Name.StartsWith("a", System.StringComparison.OrdinalIgnoreCase), OpenSearchDiagnosticCodes.UnsupportedMethod);
            AssertFails(x => x.Name.Contains("a", System.StringComparison.Ordinal), OpenSearchDiagnosticCodes.UnsupportedMethod);
        }
    }
}
