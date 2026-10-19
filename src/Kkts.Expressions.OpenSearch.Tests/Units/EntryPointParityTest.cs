using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class EntryPointParityTest
    {
        private static string Json(Expression<Func<Data, bool>> predicate) =>
            new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate).ToJsonString();

        [Fact]
        public void StringFilterListAndTree_ProduceIdenticalJson()
        {
            var fromString = Interpreter.ParsePredicate<Data>("Id = 1 and Name = 'Test' and Price >= 2.5").Result;
            var fromFilters = new[]
            {
                new Filter { Property = "Id", Operator = "=", Value = "1" },
                new Filter { Property = "Name", Operator = "=", Value = "Test" },
                new Filter { Property = "Price", Operator = ">=", Value = "2.5" }
            }.BuildPredicate<Data>();
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<Data>(), QueryPolicy.Recommended);
            var fromTree = context.BuildPredicate<Data>(FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Number("1")),
                FilterNode.Condition("Name", "=", FilterValue.String("Test")),
                FilterNode.Condition("Price", ">=", FilterValue.Number("2.5"))
            }));

            var expected = Json(fromString);
            Assert.Equal("{\"bool\":{\"filter\":[{\"term\":{\"id\":1}},{\"term\":{\"name.keyword\":\"Test\"}},{\"range\":{\"price\":{\"gte\":2.5}}}]}}", expected);
            Assert.Equal(expected, Json(fromFilters));
            Assert.Equal(expected, Json(fromTree));
        }

        [Fact]
        public void FilterGroupsAndOrTree_ProduceIdenticalJson()
        {
            var fromString = Interpreter.ParsePredicate<Data>("(Id = 1 and Name = 'A') or (Id = 2 and Name = 'B')").Result;
            var fromGroups = new[]
            {
                new FilterGroup { Filters = new List<Filter> { new Filter { Property = "Id", Operator = "=", Value = "1" }, new Filter { Property = "Name", Operator = "=", Value = "A" } } },
                new FilterGroup { Filters = new List<Filter> { new Filter { Property = "Id", Operator = "=", Value = "2" }, new Filter { Property = "Name", Operator = "=", Value = "B" } } }
            }.BuildPredicate<Data>();
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<Data>(), QueryPolicy.Recommended);
            var fromTree = context.BuildPredicate<Data>(FilterNode.Or(new[]
            {
                FilterNode.And(new[] { FilterNode.Condition("Id", "=", FilterValue.Number("1")), FilterNode.Condition("Name", "=", FilterValue.String("A")) }),
                FilterNode.And(new[] { FilterNode.Condition("Id", "=", FilterValue.Number("2")), FilterNode.Condition("Name", "=", FilterValue.String("B")) })
            }));

            var expected = Json(fromString);
            Assert.Equal(expected, Json(fromGroups));
            Assert.Equal(expected, Json(fromTree));
        }

        [Fact]
        public void MembershipAndNegation_AcrossEntryPoints()
        {
            var fromString = Interpreter.ParsePredicate<Data>("Id not in [1, 2] and not(Name.startsWith('x'))").Result;
            var fromFilters = new[]
            {
                new Filter { Property = "Id", Operator = "not in", Value = "1, 2" }
            }.BuildPredicate<Data>();
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<Data>(), QueryPolicy.Recommended);
            var fromTree = context.BuildPredicate<Data>(FilterNode.Not(FilterNode.Condition("Id", "in",
                FilterValue.Collection(new[] { FilterValue.Number("1"), FilterValue.Number("2") }))));

            Assert.Equal(Json(fromFilters), Json(fromTree));
            Assert.Contains(Json(fromFilters).Replace("{\"bool\":{\"filter\":[", string.Empty).TrimEnd(']', '}'), Json(fromString));
        }

        [Fact]
        public void ExpressionQueryContextWithPolicy_TranslatesWithoutChangingPolicyOutcomes()
        {
            var policy = new QueryPolicy(
                maxInItems: 2,
                allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                {
                    ["Name"] = new[] { ComparisonOperator.Equal }
                });
            var context = new ExpressionQueryContext(ExpressionSchema.FromType<Data>(), policy);

            var allowed = context.ParsePredicate<Data>("Name = 'Test' and Id in [1, 2]");
            var rejectedOperator = context.ParsePredicate<Data>("Name.startsWith('T')");
            var rejectedItems = context.ParsePredicate<Data>("Id in [1, 2, 3]");

            Assert.True(allowed.Succeeded);
            Assert.False(rejectedOperator.Succeeded);
            Assert.False(rejectedItems.Succeeded);
            Assert.Equal(Json(Interpreter.ParsePredicate<Data>("Name = 'Test' and Id in [1, 2]").Result), Json(allowed.Result));

            // Translating does not re-evaluate, relax, or otherwise change policy outcomes.
            Assert.Equal(allowed.Result.ToString(), context.ParsePredicate<Data>("Name = 'Test' and Id in [1, 2]").Result.ToString());
            Assert.False(context.ParsePredicate<Data>("Name.startsWith('T')").Succeeded);
            Assert.False(context.ParsePredicate<Data>("Id in [1, 2, 3]").Succeeded);
        }

        [Fact]
        public void Condition_AndTypedLambda_ProduceIdenticalJson()
        {
            var condition = new ConditionOptions { Where = "Id = 1 and Name = 'Test'" }.BuildCondition<Data>();
            var typed = Json(x => x.Id == 1 && x.Name == "Test");

            var fromCondition = new OpenSearchQueryTranslator<Data>().TranslateSearch(condition).Value["query"].ToJsonString();

            Assert.Equal(typed, fromCondition);
            Assert.Equal(typed, Json(Interpreter.ParsePredicate<Data>("Id = 1 and Name = 'Test'").Result));
        }
    }
}
