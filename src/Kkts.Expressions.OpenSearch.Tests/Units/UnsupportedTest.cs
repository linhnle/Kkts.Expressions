using System;
using System.Linq.Expressions;
using Xunit;

namespace Kkts.Expressions.OpenSearch.Tests.Units
{
    public class UnsupportedTest : PredicateTestBase
    {
        private static void AssertDiagnostic(OpenSearchTranslationDiagnostic diagnostic, string memberPath, string textFragment)
        {
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
            Assert.Equal(memberPath, diagnostic.MemberPath);
            Assert.Contains(textFragment, diagnostic.ExpressionText);
        }

        [Fact]
        public void Arithmetic()
        {
            var interpreted = Interpreter.ParsePredicate<Data>("Id + 1 > 5").Result;
            var diagnostic = JsonAssert.Fails(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertDiagnostic(diagnostic, "Id", "+");
            AssertFails(x => x.Id * 2 == 4, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertFails(x => -x.Id == 4, OpenSearchDiagnosticCodes.UnsupportedExpression);
        }

        [Fact]
        public void Concatenation()
        {
            var interpreted = Interpreter.ParsePredicate<Data>("Name + '!' = 'Test!'").Result;
            var diagnostic = JsonAssert.Fails(new OpenSearchQueryTranslator<Data>().TranslatePredicate(interpreted), OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertDiagnostic(diagnostic, "Name", "Name");
        }

        [Fact]
        public void FieldToFieldComparison()
        {
            var diagnostic = AssertFails(x => x.Start < x.End, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertDiagnostic(diagnostic, "Start", "x.End");
            AssertFails(x => x.Name == x.Code, OpenSearchDiagnosticCodes.UnsupportedExpression);
        }

        [Fact]
        public void ParameterFreeComparison_IsNotEvaluated()
        {
            var a = 1;
            var b = 2;
            AssertFails(x => a == b, OpenSearchDiagnosticCodes.UnsupportedValue);
        }

        [Fact]
        public void UnsupportedMethods()
        {
            AssertDiagnostic(AssertFails(x => x.Name.ToLower() == "a", OpenSearchDiagnosticCodes.UnsupportedMethod), "Name", "ToLower");
            AssertFails(x => x.Name.Length > 2, OpenSearchDiagnosticCodes.UnmappedField);
            AssertFails(x => x.Created.Year == 2024, OpenSearchDiagnosticCodes.UnmappedField);
            AssertFails(x => string.IsNullOrEmpty(x.Name), OpenSearchDiagnosticCodes.UnsupportedMethod);
            AssertFails(x => x.Id.Equals(1), OpenSearchDiagnosticCodes.UnsupportedMethod);
        }

        [Fact]
        public void ConditionalsAndTypeTests()
        {
            AssertFails(x => (x.IsEnabled ? x.Id : 0) == 1, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertFails(x => x.IsEnabled ? x.Id == 1 : x.Id == 2, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertFails(x => x.Parent is Data, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertFails(x => (x.Parent as object) == null, OpenSearchDiagnosticCodes.UnsupportedExpression);
            AssertFails(x => (x.Name ?? "a") == "a", OpenSearchDiagnosticCodes.UnsupportedExpression);
        }

        [Fact]
        public void ForeignParameters()
        {
            var x = Expression.Parameter(typeof(Data), "x");
            var other = Expression.Parameter(typeof(Data), "other");
            var predicate = Expression.Lambda<Func<Data, bool>>(
                Expression.Equal(Expression.Property(other, nameof(Data.Id)), Expression.Constant(1)), x);

            var diagnostic = AssertFails(predicate, OpenSearchDiagnosticCodes.UnsupportedExpression);
            Assert.Contains("other", diagnostic.ExpressionText);

            var nonGeneric = new OpenSearchQueryTranslator(OpenSearchMapping<Data>.Default);
            var wrongType = Expression.Lambda(Expression.Constant(true), Expression.Parameter(typeof(Customer), "c"));
            JsonAssert.Fails(nonGeneric.TranslatePredicate(wrongType), OpenSearchDiagnosticCodes.UnsupportedExpression);
            var wrongReturn = Expression.Lambda(Expression.Property(x, nameof(Data.Id)), x);
            JsonAssert.Fails(nonGeneric.TranslatePredicate(wrongReturn), OpenSearchDiagnosticCodes.UnsupportedExpression);
        }

        [Fact]
        public void ComputedPublicExpressionField()
        {
            var context = new ExpressionQueryContext(
                new QuerySchema<Data>()
                    .Field("display", item => item.Name + item.Code)
                    .Field("id", item => item.Id)
                    .Build(),
                new QueryPolicy());
            var predicate = context.ParsePredicate<Data>("display = 'ab'");
            Assert.True(predicate.Succeeded);

            var diagnostic = JsonAssert.Fails(new OpenSearchQueryTranslator<Data>().TranslatePredicate(predicate.Result), OpenSearchDiagnosticCodes.UnsupportedExpression);

            Assert.Contains("Name", diagnostic.ExpressionText);
            Assert.Contains("Code", diagnostic.ExpressionText);
        }

        [Fact]
        public void DiagnosticsDoNotEmbedCapturedValues()
        {
            var secret = "s3cr3t-value";
            var diagnostic = AssertFails(x => x.Name.ToUpper() == secret, OpenSearchDiagnosticCodes.UnsupportedMethod);
            Assert.DoesNotContain(secret, diagnostic.Message);
            Assert.DoesNotContain(secret, diagnostic.ExpressionText);
        }
    }
}
