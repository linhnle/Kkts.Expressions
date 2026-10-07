using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ConditionOptionsNotInTest
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Exclusion_InMemory_AllEntryPoints(bool structured)
        {
            using var context = DF.GetContext();
            var options = new ConditionOptions { OrderBy = "Id desc" };
            if (structured)
                options.Filters = new[] { new Filter { Property = "Id", Operator = "not in", Value = "1, 2" } };
            else
                options.Where = "Id not in [1, 2]";
            var expected = context.Entities.Where(e => e.Id != 1 && e.Id != 2).OrderByDescending(e => e.Id).Select(e => e.Id).ToArray();
            Assert.NotEmpty(expected);
            var conditions = new Condition[]
            {
                options.BuildCondition<TestEntity>(),
                options.BuildCondition(typeof(TestEntity)),
                await options.BuildConditionAsync<TestEntity>(),
                await options.BuildConditionAsync(typeof(TestEntity)),
                new ConditionOptions { Where = "!(Id in [1, 2])", OrderBy = "Id desc" }.BuildCondition<TestEntity>()
            };
            foreach (var condition in conditions)
            {
                Assert.True(condition.IsValid);
                Assert.Equal(expected, context.Entities.Where((Condition<TestEntity>)condition).Select(e => e.Id).ToArray());
            }
        }

        [Theory]
        [InlineData("Id notin [1]")]
        [InlineData("Id not in")]
        [InlineData("Id not in ['bad']")]
        [InlineData("Id not in [$missing]")]
        [InlineData("Id not in $missing")]
        public async Task InvalidWhere_AllEntryPoints(string query)
        {
            var options = new ConditionOptions { Where = query };
            foreach (var condition in new Condition[]
            {
                options.BuildCondition<TestEntity>(),
                options.BuildCondition(typeof(TestEntity)),
                await options.BuildConditionAsync<TestEntity>(),
                await options.BuildConditionAsync(typeof(TestEntity))
            })
            {
                Assert.False(condition.IsValid);
                Assert.NotEmpty(condition.Error.Exceptions);
                Assert.Null(condition.Predicates);
                if (query.Contains("$missing"))
                    Assert.Contains(condition.Error.EvaluationResult.InvalidVariables, name => name.TrimStart('$') == "missing");
            }
        }

        [Theory]
        [InlineData("'bad'")]
        [InlineData("$missing")]
        public async Task InvalidFilter_AllEntryPoints(string value)
        {
            var options = new ConditionOptions { Filters = new[] { new Filter { Property = "Id", Operator = "not in", Value = value } } };
            foreach (var condition in new Condition[]
            {
                options.BuildCondition<TestEntity>(),
                options.BuildCondition(typeof(TestEntity)),
                await options.BuildConditionAsync<TestEntity>(),
                await options.BuildConditionAsync(typeof(TestEntity))
            })
            {
                Assert.False(condition.IsValid);
                Assert.NotEmpty(condition.Error.Exceptions);
                Assert.Null(condition.Predicates);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationDuringResolution(bool structured)
        {
            foreach (var runtime in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                var options = structured
                    ? new ConditionOptions { Filters = new[] { new Filter { Property = "Id", Operator = "not in", Value = "$blocked" } } }
                    : new ConditionOptions { Where = "Id not in [$blocked]" };
                var resolver = new NotInTest.AsyncResolver(source, true);
                Condition condition = runtime
                    ? await options.BuildConditionAsync(typeof(TestEntity), resolver, cancellationToken: source.Token)
                    : await options.BuildConditionAsync<TestEntity>(resolver, cancellationToken: source.Token);
                Assert.False(condition.IsValid);
                Assert.Null(condition.Predicates);
                Assert.Contains(condition.Error.Exceptions, ex => ex is OperationCanceledException);
                Assert.Empty(condition.Error.EvaluationResult.InvalidValues);
            }
        }
    }
}
