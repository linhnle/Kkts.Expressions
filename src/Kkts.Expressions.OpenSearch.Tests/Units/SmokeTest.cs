using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class SmokeTest
    {
        [Fact]
        public void InterpretedPredicate_TranslatesToFilterTerms()
        {
            var predicate = Interpreter.ParsePredicate<Data>("id = 1 and name = 'Test'").Result;

            var result = new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate);

            JsonAssert.Filter(result, "{\"term\":{\"id\":1}}", "{\"term\":{\"name.keyword\":\"Test\"}}");
        }
    }
}
