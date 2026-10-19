using System;
using System.Linq;
using System.Text.Json.Nodes;
using Kkts.Expressions.OpenSearch.Internal;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class SerializationTest
    {
        private static Clause Term(string field, int value) => new TermClause(field, JsonValue.Create(value));

        [Fact]
        public void EveryClauseKind_SerializesToDocumentedShape()
        {
            JsonAssert.Equal("{\"match_all\":{}}", MatchAllClause.Instance.ToJson());
            JsonAssert.Equal("{\"match_none\":{}}", MatchNoneClause.Instance.ToJson());
            JsonAssert.Equal("{\"term\":{\"a\":1}}", Term("a", 1).ToJson());
            JsonAssert.Equal("{\"term\":{\"a\":{\"value\":\"x\",\"case_insensitive\":true}}}", new TermClause("a", JsonValue.Create("x"), true).ToJson());
            JsonAssert.Equal("{\"terms\":{\"a\":[1,\"b\",true]}}", new TermsClause("a", new JsonNode[] { 1, "b", true }).ToJson());
            JsonAssert.Equal("{\"range\":{\"a\":{\"gt\":1,\"gte\":2,\"lt\":3,\"lte\":4}}}", new RangeClause("a", 1, 2, 3, 4).ToJson());
            JsonAssert.Equal("{\"range\":{\"a\":{\"lte\":4}}}", new RangeClause("a", lte: 4).ToJson());
            JsonAssert.Equal("{\"exists\":{\"field\":\"a\"}}", new ExistsClause("a").ToJson());
            JsonAssert.Equal("{\"prefix\":{\"a\":\"x\"}}", new PrefixClause("a", "x", false).ToJson());
            JsonAssert.Equal("{\"prefix\":{\"a\":{\"value\":\"x\",\"case_insensitive\":true}}}", new PrefixClause("a", "x", true).ToJson());
            JsonAssert.Equal("{\"wildcard\":{\"a\":\"*x*\"}}", new WildcardClause("a", "*x*", false).ToJson());
            JsonAssert.Equal("{\"wildcard\":{\"a\":{\"value\":\"*x\",\"case_insensitive\":true}}}", new WildcardClause("a", "*x", true).ToJson());
            JsonAssert.Equal("{\"nested\":{\"path\":\"lines\",\"query\":{\"term\":{\"lines.a\":1}}}}", new NestedClause("lines", Term("lines.a", 1)).ToJson());
            JsonAssert.Equal(
                "{\"bool\":{\"filter\":[{\"term\":{\"a\":1}}],\"should\":[{\"term\":{\"b\":2}}],\"minimum_should_match\":1,\"must_not\":[{\"term\":{\"c\":3}}]}}",
                new BoolClause(new[] { Term("a", 1) }, new[] { Term("b", 2) }, new[] { Term("c", 3) }).ToJson());
        }

        [Fact]
        public void PropertyOrder_IsDeterministic()
        {
            var json = new BoolClause(new[] { Term("a", 1) }, new[] { Term("b", 2) }, new[] { new RangeClause("c", 1, 2, 3, 4) }).ToJson().ToJsonString();

            Assert.Equal(
                "{\"bool\":{\"filter\":[{\"term\":{\"a\":1}}],\"should\":[{\"term\":{\"b\":2}}],\"minimum_should_match\":1,\"must_not\":[{\"range\":{\"c\":{\"gt\":1,\"gte\":2,\"lt\":3,\"lte\":4}}}]}}",
                json);
        }

        [Fact]
        public void Combinators_FlattenAndSimplify()
        {
            Assert.Same(MatchAllClause.Instance, Clauses.And(Array.Empty<Clause>()));
            Assert.Same(MatchNoneClause.Instance, Clauses.Or(Array.Empty<Clause>()));
            Assert.Same(MatchNoneClause.Instance, Clauses.And(new[] { Term("a", 1), MatchNoneClause.Instance }));
            Assert.Same(MatchAllClause.Instance, Clauses.Or(new[] { Term("a", 1), MatchAllClause.Instance }));
            Assert.Same(MatchNoneClause.Instance, Clauses.Not(MatchAllClause.Instance));
            Assert.Same(MatchAllClause.Instance, Clauses.Not(MatchNoneClause.Instance));
            var term = Term("a", 1);
            Assert.Same(term, Clauses.Not(Clauses.Not(term)));
            Assert.Same(term, Clauses.And(new[] { MatchAllClause.Instance, term }));
            JsonAssert.Equal(
                "{\"bool\":{\"filter\":[{\"term\":{\"a\":1}},{\"term\":{\"b\":2}},{\"term\":{\"c\":3}}]}}",
                Clauses.And(new[] { Clauses.And(new[] { Term("a", 1), Term("b", 2) }), Term("c", 3) }).ToJson());
            JsonAssert.Equal(
                "{\"nested\":{\"path\":\"a\",\"query\":{\"nested\":{\"path\":\"a.b\",\"query\":{\"term\":{\"a.b.c\":1}}}}}}",
                Clauses.WrapNested(Term("a.b.c", 1), new[] { "a", "a.b" }).ToJson());
            JsonAssert.Equal("{\"bool\":{\"filter\":[{\"match_all\":{}}]}}", Clauses.ToRootQuery(MatchAllClause.Instance));
        }

        [Fact]
        public void RepeatedTranslation_IsByteIdentical()
        {
            var translator = new OpenSearchQueryTranslator<Data>();
            var values = new[] { 3, 1, 2 };
            var first = translator.TranslatePredicate(x => (x.Id == 1 || values.Contains(x.Id)) && x.Name.StartsWith("a") && !(x.Price > 2m)).ToJsonString();
            var second = translator.TranslatePredicate(x => (x.Id == 1 || values.Contains(x.Id)) && x.Name.StartsWith("a") && !(x.Price > 2m)).ToJsonString();
            var third = new OpenSearchQueryTranslator<Data>().TranslatePredicate(x => (x.Id == 1 || values.Contains(x.Id)) && x.Name.StartsWith("a") && !(x.Price > 2m)).ToJsonString();

            Assert.Equal(first, second);
            Assert.Equal(first, third);
        }

        [Fact]
        public void Result_ExposesValueOrDiagnostics()
        {
            var translator = new OpenSearchQueryTranslator<Data>();
            var success = translator.TranslatePredicate(x => x.Id == 1);
            Assert.True(success.Succeeded);
            Assert.Empty(success.Diagnostics);
            Assert.Same(success.Value, success.GetValueOrThrow());
            Assert.Equal("{\"bool\":{\"filter\":[{\"term\":{\"id\":1}}]}}", success.ToJsonString());

            var failure = translator.TranslatePredicate(x => x.Id + 1 > 5);
            Assert.False(failure.Succeeded);
            Assert.Null(failure.Value);
            var exception = Assert.Throws<OpenSearchTranslationException>(() => failure.GetValueOrThrow());
            Assert.Equal(failure.Diagnostics, exception.Diagnostics);
            Assert.Contains(OpenSearchDiagnosticCodes.UnsupportedExpression, exception.Message);
            Assert.Throws<OpenSearchTranslationException>(() => failure.ToJsonString());
        }

        [Fact]
        public void Diagnostic_HasValueEquality()
        {
            var a = new OpenSearchTranslationDiagnostic("c", "m", "P", "x");
            Assert.Equal(a, new OpenSearchTranslationDiagnostic("c", "m", "P", "x"));
            Assert.Equal(a.GetHashCode(), new OpenSearchTranslationDiagnostic("c", "m", "P", "x").GetHashCode());
            Assert.NotEqual(a, new OpenSearchTranslationDiagnostic("c", "m", "Q", "x"));
            Assert.Contains("c", a.ToString());
            Assert.ThrowsAny<ArgumentException>(() => new OpenSearchTranslationDiagnostic(null, "m"));
        }

        [Fact]
        public void DiagnosticCodes_AreStable()
        {
            var codes = typeof(OpenSearchDiagnosticCodes).GetFields().Select(f => (string)f.GetValue(null)).ToArray();
            Assert.Equal(10, codes.Length);
            Assert.All(codes, c => Assert.StartsWith("opensearch-", c));
            Assert.Equal(codes.Length, codes.Distinct().Count());
        }
    }
}
