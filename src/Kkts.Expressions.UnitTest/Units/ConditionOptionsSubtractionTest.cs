using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public partial class ConditionOptionsTest
    {
        [Theory]
        [InlineData("Integer - $amount = $target")]
        [InlineData("(Integer + 1) - $amount = $target")]
        [InlineData("IntegerNullable - 1 = null")]
        public async Task BuildCondition_Subtraction_AllEntryPoints(string query)
        {
            using var context = DF.GetContext();
            var resolver = new VariableResolver();
            resolver.TryAdd("amount", query.Contains("+") ? 2 : 1);
            resolver.TryAdd("target", DF.Integer1 - 1);
            var options = new ConditionOptions { Where = query };
            var generic = options.BuildCondition<TestEntity>(resolver);
            var runtime = options.BuildCondition(typeof(TestEntity), resolver);
            var asyncGeneric = await options.BuildConditionAsync<TestEntity>(resolver);
            var asyncRuntime = await options.BuildConditionAsync(typeof(TestEntity), resolver);
            Assert.True(generic.IsValid);
            Assert.True(runtime.IsValid);
            Assert.True(asyncGeneric.IsValid);
            Assert.True(asyncRuntime.IsValid);
            var expected = query.Contains("Nullable")
                ? context.Entities.Count(entity => entity.IntegerNullable == null)
                : 1;
            Assert.Equal(expected, context.Entities.Where(generic).Count());
            Assert.Equal(expected, context.Entities.Where(asyncGeneric).Count());
            Assert.Equal(expected, context.Entities.Where((Condition<TestEntity>)runtime).Count());
            Assert.Equal(expected, context.Entities.Where((Condition<TestEntity>)asyncRuntime).Count());
        }

        [Theory]
        [InlineData("Integer - = 5")]
        [InlineData("String - 1 = 0")]
        [InlineData("Integer - 1")]
        public async Task BuildCondition_Subtraction_Invalid(string query)
        {
            var options = new ConditionOptions { Where = query };
            var generic = options.BuildCondition<TestEntity>();
            var runtime = options.BuildCondition(typeof(TestEntity));
            var asyncGeneric = await options.BuildConditionAsync<TestEntity>();
            var asyncRuntime = await options.BuildConditionAsync(typeof(TestEntity));
            Assert.False(generic.IsValid);
            Assert.False(runtime.IsValid);
            Assert.False(asyncGeneric.IsValid);
            Assert.False(asyncRuntime.IsValid);
            Assert.NotEmpty(generic.Error.Exceptions);
            Assert.NotEmpty(runtime.Error.Exceptions);
            Assert.NotEmpty(asyncGeneric.Error.Exceptions);
            Assert.NotEmpty(asyncRuntime.Error.Exceptions);
            Assert.Null(generic.Predicates);
            Assert.Null(runtime.Predicates);
            Assert.Null(asyncGeneric.Predicates);
            Assert.Null(asyncRuntime.Predicates);
        }
    }
}
