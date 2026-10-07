using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public partial class ConditionOptionsTest
    {
        [Fact]
        public void BuildCondition_Plus_InMemory()
        {
            using var context = DF.GetContext();
            var numeric = new ConditionOptions { Where = $"Integer + 1 = {DF.Integer1 + 1}" }.BuildCondition<TestEntity>();
            var text = new ConditionOptions { Where = $"String + '!' = '{DF.String1}!'" }.BuildCondition<TestEntity>();
            Assert.True(numeric.IsValid);
            Assert.True(text.IsValid);
            Assert.Equal(1, context.Entities.Where(numeric).Count());
            Assert.Equal(1, context.Entities.Where(text).Count());
        }

        [Fact]
        public void BuildCondition_Plus_Invalid()
        {
            var result = new ConditionOptions { Where = "Integer + = 5" }.BuildCondition(typeof(TestEntity));
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Error.Exceptions);
            Assert.Null(result.Predicates);
            var generic = new ConditionOptions { Where = "Integer + = 5" }.BuildCondition<TestEntity>();
            Assert.False(generic.IsValid);
            Assert.NotEmpty(generic.Error.Exceptions);
            Assert.Null(generic.Predicates);
        }
    }

    public partial class ConditionOptionsAsyncTest
    {
        [Fact]
        public async Task BuildConditionAsync_Plus_InMemory()
        {
            using var context = DF.GetContext();
            var resolver = new VariableResolver();
            resolver.TryAdd("increment", 1);
            var numeric = await new ConditionOptions { Where = $"Integer + $increment = {DF.Integer1 + 1}" }
                .BuildConditionAsync<TestEntity>(resolver);
            var text = await new ConditionOptions { Where = $"String + '!' = '{DF.String1}!'" }.BuildConditionAsync<TestEntity>();
            Assert.True(numeric.IsValid);
            Assert.True(text.IsValid);
            Assert.Equal(1, context.Entities.Where(numeric).Count());
            Assert.Equal(1, context.Entities.Where(text).Count());
        }

        [Fact]
        public async Task BuildConditionAsync_Plus_Invalid()
        {
            var result = await new ConditionOptions { Where = "Integer + = 5" }.BuildConditionAsync(typeof(TestEntity));
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.Error.Exceptions);
            Assert.Null(result.Predicates);
            var generic = await new ConditionOptions { Where = "Integer + = 5" }.BuildConditionAsync<TestEntity>();
            Assert.False(generic.IsValid);
            Assert.NotEmpty(generic.Error.Exceptions);
            Assert.Null(generic.Predicates);
        }
    }
}
