using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterCompatibilityCharacterizationTest
    {
        [Fact]
        public void EmptyLegacyInputsRetainTheirAlwaysTrueBehavior()
        {
            var entity = new TestEntity();
            var emptyFilters = Array.Empty<Filter>();
            var emptyGroups = Array.Empty<FilterGroup>();
            var emptyGroup = new FilterGroup { Filters = new List<Filter>() };

            Assert.True(emptyFilters.BuildPredicate<TestEntity>().Compile()(entity));
            Assert.True(emptyFilters.TryBuildPredicate<TestEntity>().Result.Compile()(entity));
            Assert.True(emptyGroups.BuildPredicate<TestEntity>().Compile()(entity));
            Assert.True(emptyGroups.TryBuildPredicate<TestEntity>().Result.Compile()(entity));
            Assert.True(emptyGroup.BuildPredicate<TestEntity>().Compile()(entity));
            Assert.True(emptyGroup.TryBuildPredicate<TestEntity>().Result.Compile()(entity));
        }

        [Fact]
        public void NullAndEmptyLegacyGroupFailureModesRemainSurfaceSpecific()
        {
            var nullGroup = new FilterGroup[] { null };
            Assert.Throws<InvalidOperationException>(
                () => nullGroup.BuildPredicate<TestEntity>());
            var nullGroupTryError = Assert.Throws<InvalidOperationException>(
                () => nullGroup.TryBuildPredicate<TestEntity>());
            Assert.Equal("FilterGroup can not be null", nullGroupTryError.Message);

            var nullFilters = new FilterGroup { Filters = null };
            Assert.Throws<ArgumentNullException>(
                () => nullFilters.BuildPredicate<TestEntity>());
            Assert.Throws<NullReferenceException>(
                () => nullFilters.TryBuildPredicate<TestEntity>());

            var emptyGroupSequence = new FilterGroup[]
            {
                new FilterGroup { Filters = new List<Filter>() }
            };
            Assert.Throws<InvalidCastException>(
                () => emptyGroupSequence.TryBuildPredicate<TestEntity>());
        }

        [Fact]
        public async Task InvalidLeavesRetainThrowingAndEvaluationFailures()
        {
            var invalidProperty = new Filter { Property = "Missing", Operator = "=", Value = "1" };
            var propertyFailure = invalidProperty.TryBuildPredicate<TestEntity>();
            Assert.False(propertyFailure.Succeeded);
            Assert.Null(propertyFailure.Result);
            Assert.Empty(propertyFailure.InvalidProperties);
            Assert.Throws<InvalidOperationException>(() => invalidProperty.BuildPredicate<TestEntity>());

            var invalidValue = new Filter { Property = "Integer", Operator = "=", Value = "not-an-integer" };
            var valueFailure = invalidValue.TryBuildPredicate<TestEntity>();
            Assert.False(valueFailure.Succeeded);
            Assert.Null(valueFailure.Result);
            Assert.NotNull(valueFailure.Exception);
            Assert.ThrowsAny<Exception>(() => invalidValue.BuildPredicate<TestEntity>());

            var asyncPropertyFailure = await invalidProperty.TryBuildPredicateAsync<TestEntity>();
            Assert.False(asyncPropertyFailure.Succeeded);
            Assert.Null(asyncPropertyFailure.Result);
            Assert.Empty(asyncPropertyFailure.InvalidProperties);

            var asyncValueFailure = await invalidValue.TryBuildPredicateAsync<TestEntity>();
            Assert.False(asyncValueFailure.Succeeded);
            Assert.Null(asyncValueFailure.Result);
            Assert.NotNull(asyncValueFailure.Exception);
        }

        [Fact]
        public async Task VariableLookingFilterValuesKeepLegacyTypeDependentResolution()
        {
            var resolver = new VariableResolver();
            Assert.True(resolver.TryAdd("id", 7));
            var numeric = new Filter { Property = "Integer", Operator = "=", Value = "$id" };
            var stringFilter = new Filter { Property = "String", Operator = "=", Value = "$id" };
            var entity = new TestEntity { Integer = 7, String = "$id" };

            Assert.True(numeric.BuildPredicate<TestEntity>(resolver).Compile()(entity));
            Assert.True(stringFilter.BuildPredicate<TestEntity>(resolver).Compile()(entity));

            var asyncNumeric = await numeric.BuildPredicateAsync<TestEntity>(resolver);
            var asyncString = await stringFilter.BuildPredicateAsync<TestEntity>(resolver);
            Assert.True(asyncNumeric.Compile()(entity));
            Assert.True(asyncString.Compile()(entity));
        }

        [Fact]
        public void ConditionOptionsKeepsLegacyPredicateCompositionOrder()
        {
            var options = new ConditionOptions
            {
                Filters = new[] { new Filter { Property = "Integer", Operator = "=", Value = "1" } },
                FilterGroups = new[]
                {
                    new FilterGroup
                    {
                        Filters = new List<Filter>
                        {
                            new Filter { Property = "Integer", Operator = "=", Value = "2" }
                        }
                    }
                },
                Where = "Integer = 3"
            };

            var condition = options.BuildCondition<TestEntity>();

            Assert.True(condition.IsValid);
            var predicates = condition.Predicates.ToArray();
            Assert.Equal(3, predicates.Length);
            Assert.Contains("== 1", predicates[0].ToString());
            Assert.Contains("== 2", predicates[1].ToString());
            Assert.Contains("== 3", predicates[2].ToString());
        }

        [Fact]
        public void PolicyContextKeepsItsConditionInputOrderAndDoesNotExposePartialFailures()
        {
            var queryContext = new ExpressionQueryContext(
                ExpressionSchema.FromType<TestEntity>(),
                new QueryPolicy());
            var options = new ConditionOptions
            {
                Filters = new[] { new Filter { Property = "Integer", Operator = "=", Value = "1" } },
                FilterGroups = new[]
                {
                    new FilterGroup
                    {
                        Filters = new List<Filter>
                        {
                            new Filter { Property = "Integer", Operator = "=", Value = "2" }
                        }
                    }
                },
                Where = "Integer = 3"
            };

            var condition = queryContext.BuildCondition(options);
            Assert.True(condition.IsValid);
            var predicates = condition.Predicates.ToArray();
            Assert.Equal(3, predicates.Length);
            Assert.Contains("== 3", predicates[0].ToString());
            Assert.Contains("== 1", predicates[1].ToString());
            Assert.Contains("== 2", predicates[2].ToString());

            options.Filters = new[] { new Filter { Property = "Missing", Operator = "=", Value = "1" } };
            var failed = queryContext.BuildCondition(options);
            Assert.False(failed.IsValid);
            Assert.Null(failed.Predicates);
        }

        [Fact]
        public void ExistingStructuredPayloadsKeepDefaultJsonPropertyNamesAndShapes()
        {
            var filter = new Filter { Property = "Id", Operator = "=", Value = "1" };
            var group = new FilterGroup { Filters = new List<Filter> { filter } };
            var options = new ConditionOptions
            {
                Filters = new[] { filter },
                FilterGroups = new[] { group },
                Where = "Id = 1"
            };

            Assert.Equal(
                "{\"Property\":\"Id\",\"Operator\":\"=\",\"Value\":\"1\"}",
                JsonSerializer.Serialize(filter));
            Assert.Equal(
                "{\"Filters\":[{\"Property\":\"Id\",\"Operator\":\"=\",\"Value\":\"1\"}]}",
                JsonSerializer.Serialize(group));

            var payload = JsonSerializer.Serialize(options);
            Assert.Contains("\"FilterGroups\":[{\"Filters\":[", payload);
            Assert.Contains("\"Filters\":[{\"Property\":\"Id\"", payload);
            Assert.Contains("\"Where\":\"Id = 1\"", payload);
            Assert.DoesNotContain("\"filter\"", payload);
            Assert.DoesNotContain("\"and\"", payload);
        }
    }
}
