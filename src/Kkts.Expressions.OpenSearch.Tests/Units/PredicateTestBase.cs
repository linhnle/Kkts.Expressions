using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public abstract class PredicateTestBase
    {
        internal static OpenSearchTranslationResult<JsonObject> Translate(Expression<Func<Data, bool>> predicate, OpenSearchMapping<Data> mapping = null) =>
            new OpenSearchQueryTranslator<Data>(mapping ?? OpenSearchMapping<Data>.Default).TranslatePredicate(predicate);

        internal static void AssertClause(Expression<Func<Data, bool>> predicate, string clause, OpenSearchMapping<Data> mapping = null) =>
            JsonAssert.Filter(Translate(predicate, mapping), clause);

        internal static OpenSearchTranslationDiagnostic AssertFails(Expression<Func<Data, bool>> predicate, string code, OpenSearchMapping<Data> mapping = null) =>
            JsonAssert.Fails(Translate(predicate, mapping), code);

        internal static Dictionary<string, JsonNode[]> Doc(params (string Field, JsonNode Value)[] fields)
        {
            var document = new Dictionary<string, JsonNode[]>();
            foreach (var (field, value) in fields) document[field] = new[] { value };
            return document;
        }

        internal static bool Matches(OpenSearchTranslationResult<JsonObject> result, Dictionary<string, JsonNode[]> document)
        {
            Xunit.Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics));
            return QueryEvaluator.Matches(result.Value, document);
        }
    }
}
