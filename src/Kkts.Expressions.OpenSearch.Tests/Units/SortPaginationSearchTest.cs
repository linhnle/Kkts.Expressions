using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class SortPaginationSearchTest
    {
        private static OrderByClause OrderBy(string expression) =>
            expression.TryBuildOrderByClause(typeof(Data)).Result;

        // 6.1 sort

        [Fact]
        public void MultiKeySort()
        {
            var sort = new OpenSearchQueryTranslator<Data>().TranslateSort(OrderBy("name desc, id"));
            JsonAssert.Equal("[{\"name.keyword\":{\"order\":\"desc\",\"missing\":\"_last\"}},{\"id\":{\"order\":\"asc\",\"missing\":\"_first\"}}]", sort.Value);
        }

        [Fact]
        public void SortUsesFieldMapping()
        {
            var mapping = OpenSearchMapping.For<Data>()
                .Field(x => x.Code, f => f.Name("sku").Keyword())
                .Field(x => x.Price, f => f.Name("amount"))
                .Build();
            var sort = new OpenSearchQueryTranslator<Data>(mapping).TranslateSort(OrderBy("Code, Price desc, FullName, Created desc, Status"));
            JsonAssert.Equal(
                "[{\"sku\":{\"order\":\"asc\",\"missing\":\"_first\"}},{\"amount\":{\"order\":\"desc\",\"missing\":\"_last\"}}," +
                "{\"full_name.keyword\":{\"order\":\"asc\",\"missing\":\"_first\"}},{\"created\":{\"order\":\"desc\",\"missing\":\"_last\"}}," +
                "{\"status\":{\"order\":\"asc\",\"missing\":\"_first\"}}]",
                sort.Value);
        }

        [Fact]
        public void NestedSortKey_IncludesNestedOption()
        {
            var mapping = OpenSearchMapping.For<Data>().Nested(x => x.Customer, c => c.Nested(e => e.Address)).Build();
            var sort = new OpenSearchQueryTranslator<Data>(mapping).TranslateSort(OrderBy("Customer.Age desc, Customer.Address.City"));
            JsonAssert.Equal(
                "[{\"customer.age\":{\"order\":\"desc\",\"missing\":\"_last\",\"nested\":{\"path\":\"customer\"}}}," +
                "{\"customer.address.city.keyword\":{\"order\":\"asc\",\"missing\":\"_first\",\"nested\":{\"path\":\"customer\",\"nested\":{\"path\":\"customer.address\"}}}}]",
                sort.Value);
        }

        [Fact]
        public void ComputedSortKey_Fails()
        {
            var schema = new QuerySchema<Data>()
                .Field("display", x => x.Name + x.Code)
                .Field("id", x => x.Id)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var orderBy = context.TryBuildOrderByClause("id, display desc");
            Assert.True(orderBy.Succeeded);

            var result = new OpenSearchQueryTranslator<Data>().TranslateSort(orderBy.Result);

            var diagnostic = JsonAssert.Fails(result, OpenSearchDiagnosticCodes.UnmappedField);
            Assert.Null(result.Value);
            Assert.Contains("Name", diagnostic.ExpressionText);
            Assert.Contains("Code", diagnostic.ExpressionText);
        }

        [Fact]
        public void PublicSchemaMemberSortKey_UsesMappedField()
        {
            var schema = new QuerySchema<Data>().Field("displayName", x => x.Name).Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var sort = new OpenSearchQueryTranslator<Data>().TranslateSort(context.TryBuildOrderByClause("displayName desc").Result);
            JsonAssert.Equal("[{\"name.keyword\":{\"order\":\"desc\",\"missing\":\"_last\"}}]", sort.Value);
        }

        [Fact]
        public void TextOnlyAndUnsupportedSortKeys_Fail()
        {
            var textOnly = OpenSearchMapping.For<Data>().Field(x => x.Description, f => f.TextOnly()).Build();
            JsonAssert.Fails(new OpenSearchQueryTranslator<Data>(textOnly).TranslateSort(OrderBy("Description")), OpenSearchDiagnosticCodes.TextFieldExactMatch);

            var names = OpenSearchMapping.For<Data>().EnumFormat(EnumFormat.Name).Build();
            JsonAssert.Fails(new OpenSearchQueryTranslator<Data>(names).TranslateSort(OrderBy("Status")), OpenSearchDiagnosticCodes.UnsupportedValue);

            JsonAssert.Fails(new OpenSearchQueryTranslator<Data>().TranslateSort(OrderBy("Customer")), OpenSearchDiagnosticCodes.UnsupportedExpression);
        }

        [Fact]
        public void SortOrderMatchesLinqNullOrdering()
        {
            var items = new[]
            {
                new Data { Id = 1, NullableInteger = 5 },
                new Data { Id = 2, NullableInteger = null },
                new Data { Id = 3, NullableInteger = 1 }
            };
            Assert.Equal(new[] { 2, 3, 1 }, items.OrderBy(x => x.NullableInteger).Select(x => x.Id));
            Assert.Equal(new[] { 1, 3, 2 }, items.OrderByDescending(x => x.NullableInteger).Select(x => x.Id));
            var sort = new OpenSearchQueryTranslator<Data>().TranslateSort(OrderBy("NullableInteger, NullableInteger desc"));
            JsonAssert.Equal("[{\"nullableInteger\":{\"order\":\"asc\",\"missing\":\"_first\"}},{\"nullableInteger\":{\"order\":\"desc\",\"missing\":\"_last\"}}]", sort.Value);
        }

        // 6.2 pagination

        [Fact]
        public void PageTranslation()
        {
            var result = new OpenSearchQueryTranslator<Data>().TranslatePagination(new Pagination { Limit = 20, Offset = 40 });
            JsonAssert.Equal("{\"from\":40,\"size\":20}", result.Value);
            var page = new OpenSearchQueryTranslator<Data>().TranslatePagination(new Pagination { Limit = 10, Page = 3 });
            JsonAssert.Equal("{\"from\":20,\"size\":10}", page.Value);
        }

        [Fact]
        public void ResultWindowExceeded()
        {
            var translator = new OpenSearchQueryTranslator<Data>();
            JsonAssert.Equal("{\"from\":9900,\"size\":100}", translator.TranslatePagination(new Pagination { Limit = 100, Offset = 9900 }).Value);

            var result = translator.TranslatePagination(new Pagination { Limit = 100, Offset = 9920 });

            var diagnostic = JsonAssert.Fails(result, OpenSearchDiagnosticCodes.ResultWindowExceeded);
            Assert.Contains("10020", diagnostic.Message);
            Assert.Contains("10000", diagnostic.Message);
        }

        [Fact]
        public void ResultWindow_IsConfigurableAndOverflowSafe()
        {
            var small = new OpenSearchQueryTranslator<Data>(OpenSearchMapping.For<Data>().MaxResultWindow(50).Build());
            Assert.True(small.TranslatePagination(new Pagination { Limit = 10, Offset = 40 }).Succeeded);
            JsonAssert.Fails(small.TranslatePagination(new Pagination { Limit = 10, Offset = 41 }), OpenSearchDiagnosticCodes.ResultWindowExceeded);

            var huge = new OpenSearchQueryTranslator<Data>().TranslatePagination(new Pagination { Limit = 100, Offset = int.MaxValue });
            Assert.Contains("2147483747", JsonAssert.Fails(huge, OpenSearchDiagnosticCodes.ResultWindowExceeded).Message);
        }

        // 6.3 search body

        [Fact]
        public void FullBody()
        {
            var condition = new ConditionOptions
            {
                Filters = new[]
                {
                    new Filter { Property = "Id", Operator = ">", Value = "1" },
                },
                Where = "Name = 'Test'",
                OrderBy = "Name desc, Id"
            }.BuildCondition<Data>();
            Assert.True(condition.IsValid);
            Assert.Equal(2, condition.Predicates.Count);

            var body = new OpenSearchQueryTranslator<Data>().TranslateSearch(condition, new Pagination { Offset = 0, Limit = 10 });

            Assert.True(body.Succeeded);
            Assert.Equal(2, body.Value["query"]["bool"]["filter"].AsArray().Count);
            Assert.Contains("{\"range\":{\"id\":{\"gt\":1}}}", body.Value["query"].ToJsonString());
            Assert.Contains("{\"term\":{\"name.keyword\":\"Test\"}}", body.Value["query"].ToJsonString());
            JsonAssert.Equal("[{\"name.keyword\":{\"order\":\"desc\",\"missing\":\"_last\"}},{\"id\":{\"order\":\"asc\",\"missing\":\"_first\"}}]", body.Value["sort"]);
            Assert.Equal(0, (int)body.Value["from"]);
            Assert.Equal(10, (int)body.Value["size"]);
        }

        [Fact]
        public void BodyOmitsOptionalParts()
        {
            var body = new OpenSearchQueryTranslator<Data>().TranslateSearch(x => x.Id == 1);
            JsonAssert.Equal("{\"query\":{\"bool\":{\"filter\":[{\"term\":{\"id\":1}}]}}}", body.Value);
            var empty = new OpenSearchQueryTranslator<Data>().TranslateSearch(Array.Empty<Expression<Func<Data, bool>>>(), OrderBy("Id"));
            JsonAssert.Equal("{\"query\":{\"bool\":{\"filter\":[{\"match_all\":{}}]}},\"sort\":[{\"id\":{\"order\":\"asc\",\"missing\":\"_first\"}}]}", empty.Value);
        }

        [Fact]
        public void InvalidCondition()
        {
            var condition = new ConditionOptions { Where = "Missing = 1" }.BuildCondition<Data>();
            Assert.False(condition.IsValid);

            var result = new OpenSearchQueryTranslator<Data>().TranslateSearch(condition, new Pagination());

            JsonAssert.Fails(result, OpenSearchDiagnosticCodes.InvalidCondition);
            Assert.Null(result.Value);
        }

        [Fact]
        public void DiagnosticsFromAllPartsAreCombined()
        {
            var textOnly = OpenSearchMapping.For<Data>().Field(x => x.Description, f => f.TextOnly()).Build();
            var result = new OpenSearchQueryTranslator<Data>(textOnly).TranslateSearch(
                new Expression<Func<Data, bool>>[] { x => x.Id + 1 == 2, x => x.Tags.Any(t => t != "a") },
                OrderBy("Description"),
                new Pagination { Limit = 100, Offset = 9950 });

            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            Assert.Equal(
                new[]
                {
                    OpenSearchDiagnosticCodes.UnsupportedExpression,
                    OpenSearchDiagnosticCodes.FlattenedCollection,
                    OpenSearchDiagnosticCodes.TextFieldExactMatch,
                    OpenSearchDiagnosticCodes.ResultWindowExceeded
                },
                result.Diagnostics.Select(d => d.Code));
        }

        [Fact]
        public void NonGenericOverloads()
        {
            var translator = new OpenSearchQueryTranslator(OpenSearchMapping<Data>.Default);
            LambdaExpression predicate = Interpreter.ParsePredicate("Id = 1", typeof(Data)).Result;
            Condition condition = new ConditionOptions { Where = "Id = 1", OrderBy = "Id" }.BuildCondition(typeof(Data));

            JsonAssert.Equal("{\"bool\":{\"filter\":[{\"term\":{\"id\":1}}]}}", translator.TranslatePredicate(predicate).Value);
            JsonAssert.Equal("{\"bool\":{\"filter\":[{\"term\":{\"id\":1}}]}}", translator.TranslatePredicates(new[] { predicate }).Value);
            JsonAssert.Equal(
                "{\"query\":{\"bool\":{\"filter\":[{\"term\":{\"id\":1}}]}},\"sort\":[{\"id\":{\"order\":\"asc\",\"missing\":\"_first\"}}],\"from\":0,\"size\":5}",
                translator.TranslateSearch(condition, new Pagination { Limit = 5 }).Value);
            JsonAssert.Equal(
                translator.TranslateSearch(predicate, OrderBy("Id")).ToJsonString(),
                translator.TranslateSearch(new[] { predicate }, OrderBy("Id")).Value);

            Expression<Func<Customer, bool>> wrongType = c => c.Age > 1;
            JsonAssert.Fails(translator.TranslatePredicate(wrongType), OpenSearchDiagnosticCodes.UnsupportedExpression);
            Assert.Throws<ArgumentNullException>(() => translator.TranslateSort(null));
            Assert.Throws<ArgumentNullException>(() => translator.TranslatePagination(null));
            Assert.Throws<ArgumentNullException>(() => translator.TranslateSearch((Condition)null));
        }
    }
}
