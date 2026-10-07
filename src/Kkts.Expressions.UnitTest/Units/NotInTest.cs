using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class NotInTest
    {
        [Theory]
        [InlineData("Id not in [1, 2]", false)]
        [InlineData("Id NOT IN [1, 2]", false)]
        [InlineData("Id NoT   iN [1, 2]", false)]
        [InlineData("Id not\tin [1, 2]", false)]
        [InlineData("Id not\r\nin [1, 2]", false)]
        [InlineData("Id not in[1, 2]", false)]
        [InlineData("Id not in [2, 3]", true)]
        [InlineData("Id not in []", true)]
        [InlineData("Id not in [1, 1, 2]", false)]
        [InlineData("Id + 1 not in [2, 3]", false)]
        [InlineData("(Id - 1) not in [0, 1]", false)]
        [InlineData("(Id + 1) not in $values", false)]
        [InlineData("String + '!' not in ['not in!']", false)]
        [InlineData("String not in ['not in', 'notin']", false)]
        [InlineData("NotInside not in [2]", true)]
        [InlineData("Id not in [2] and Boolean", false)]
        [InlineData("Id not in [1] or Boolean", false)]
        [InlineData("Id not in [2] or Boolean", true)]
        [InlineData("!(Id not in [1, 2])", true)]
        [InlineData("not(Id not in [1, 2])", true)]
        [InlineData("!!(Id not in [1, 2])", false)]
        [InlineData("not(Id in [1, 2])", false)]
        [InlineData("!Boolean", true)]
        [InlineData("not(Boolean)", true)]
        public async Task SyntaxAndComposition_AllEntryPoints(string query, bool expected)
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("values", new[] { 2, 3 });
            var entity = new MembershipEntity { Id = 1, String = "not in", NotInside = 1 };
            await AssertParsed(query, entity, expected, resolver);
        }

        [Theory]
        [InlineData("Id not [1]")]
        [InlineData("Id notin [1]")]
        [InlineData("Id not inside [1]")]
        [InlineData("Id not in")]
        [InlineData("Id not i [1]")]
        [InlineData("Id not invalue")]
        [InlineData("Id not in1")]
        [InlineData("Id not i n [1]")]
        [InlineData("Id !in [1]")]
        [InlineData("Id not in ['bad']")]
        [InlineData("Id not in [null]")]
        [InlineData("Id not in [nullish]")]
        public async Task InvalidSyntax_AllEntryPoints(string query)
        {
            var results = new EvaluationResult[]
            {
                Interpreter.ParsePredicate(query, typeof(MembershipEntity)),
                await Interpreter.ParsePredicateAsync(query, typeof(MembershipEntity))
            };
            foreach (var result in results) AssertFailure(result);
            var generic = Interpreter.ParsePredicate<MembershipEntity>(query);
            var genericAsync = await Interpreter.ParsePredicateAsync<MembershipEntity>(query);
            Assert.False(generic.Succeeded);
            Assert.NotNull(generic.Exception);
            Assert.Null(generic.Result);
            Assert.False(genericAsync.Succeeded);
            Assert.NotNull(genericAsync.Exception);
            Assert.Null(genericAsync.Result);
        }

        public static IEnumerable<object[]> TypeCases()
        {
            yield return new object[] { "Integer", "1, 2", 1, 3 };
            yield return new object[] { "IntegerNullable", "1, 2", 1, null };
            yield return new object[] { "IntegerNullable", "null, 1", null, 3 };
            yield return new object[] { "IntegerNullable", "'', 1", null, 3 };
            yield return new object[] { "Double", "1.5, 2", 1.5, 3.5 };
            yield return new object[] { "DoubleNullable", "null, 1.5", null, 3.5 };
            yield return new object[] { "Decimal", "1.5, 2", 1.5m, 3.5m };
            yield return new object[] { "String", "'one', 'two'", "one", "three" };
            yield return new object[] { "String", "null, 'null'", null, "three" };
            yield return new object[] { "Boolean", "'true'", true, false };
            yield return new object[] { "BooleanNullable", "null, 'true'", null, false };
            yield return new object[] { "Guid", "'00000000-0000-0000-0000-000000000001'", Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Empty };
            yield return new object[] { "GuidNullable", "null, '00000000-0000-0000-0000-000000000001'", null, Guid.Empty };
            yield return new object[] { "Option", "'Option1'", TestOptions.Option1, TestOptions.Option2 };
            yield return new object[] { "OptionNullable", "null, 'Option1'", null, TestOptions.Option2 };
            yield return new object[] { "DateTime", "'2020-01-01'", new DateTime(2020, 1, 1), new DateTime(2020, 1, 2) };
            yield return new object[] { "DateTimeNullable", "null, '2020-01-01'", null, new DateTime(2020, 1, 2) };
            yield return new object[] { "DateTimeOffset", "'2020-01-01T00:00:00+00:00'", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MinValue };
            yield return new object[] { "DateTimeOffsetNullable", "null, '2020-01-01T00:00:00+00:00'", null, DateTimeOffset.MinValue };
            yield return new object[] { "Duration", "'01:00:00'", TimeSpan.FromHours(1), TimeSpan.Zero };
            yield return new object[] { "NullableDuration", "null, '01:00:00'", null, TimeSpan.Zero };
        }

        [Theory]
        [MemberData(nameof(TypeCases))]
        public async Task TypeMatrix_ParsedAndBuiltComplement(string property, string list, object matching, object nonMatching)
        {
            var entity = new MembershipEntity();
            var member = typeof(MembershipEntity).GetProperty(property);
            foreach (var pair in new[] { (Value: matching, Expected: false), (Value: nonMatching, Expected: true) })
            {
                member.SetValue(entity, pair.Value);
                await AssertParsed($"{property} not in [{list}]", entity, pair.Expected);
                AssertNativeTree(Interpreter.ParsePredicate<MembershipEntity>($"{property} not in [{list}]").Result);
                await AssertParsed($"!({property} in [{list}])", entity, pair.Expected);
                foreach (var expression in await BuildAll(property, list))
                {
                    Assert.Equal(pair.Expected, expression.Compile()(entity));
                    AssertNativeTree(expression);
                }
                Assert.Equal(!pair.Expected, Interpreter.BuildPredicate<MembershipEntity>(property, ComparisonOperator.In, list).Compile()(entity));
                Assert.Equal(!pair.Expected, (await Interpreter.BuildPredicateAsync<MembershipEntity>(property, ComparisonOperator.In, list)).Compile()(entity));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("2, 2")]
        public async Task EmptyAndDuplicateLists_AllBuilders(string list)
        {
            foreach (var expression in await BuildAll("Id", list))
            {
                Assert.True(expression.Compile()(new MembershipEntity { Id = 1 }));
                Assert.Equal(list.Length == 0, expression.Compile()(new MembershipEntity { Id = 2 }));
            }
        }

        [Theory]
        [InlineData("Integer")]
        [InlineData("IntegerNullable")]
        [InlineData("Double")]
        [InlineData("DoubleNullable")]
        [InlineData("Decimal")]
        [InlineData("Boolean")]
        [InlineData("BooleanNullable")]
        [InlineData("Guid")]
        [InlineData("GuidNullable")]
        [InlineData("Option")]
        [InlineData("OptionNullable")]
        [InlineData("DateTime")]
        [InlineData("DateTimeNullable")]
        [InlineData("DateTimeOffset")]
        [InlineData("DateTimeOffsetNullable")]
        [InlineData("Duration")]
        [InlineData("NullableDuration")]
        public async Task InvalidConversions_MatchInFailureContracts(string property)
        {
            foreach (var spelling in new[] { "in", "not in" })
            {
                var parsed = Interpreter.ParsePredicate(property + " " + spelling + " ['bad']", typeof(MembershipEntity));
                var parsedAsync = await Interpreter.ParsePredicateAsync(property + " " + spelling + " ['bad']", typeof(MembershipEntity));
                AssertFailure(parsed);
                AssertFailure(parsedAsync);
                var filter = new Filter { Property = property, Operator = spelling, Value = "'bad'" };
                var sync = filter.TryBuildPredicate(typeof(MembershipEntity));
                var asyncResult = await filter.TryBuildPredicateAsync(typeof(MembershipEntity));
                AssertFailure(sync);
                AssertFailure(asyncResult);
                Assert.Equal(sync.Exception.GetType(), asyncResult.Exception.GetType());
            }
        }

        [Fact]
        public async Task NullLiteral_DoesNotChangeQuotedText()
        {
            foreach (var value in new[] { null, "null", "NULL", "other" })
            {
                await AssertParsed("String not in [null]", new MembershipEntity { String = value }, value != null);
                await AssertParsed("String not in ['null']", new MembershipEntity { String = value }, value != "null");
                await AssertParsed("String not in [NULL]", new MembershipEntity { String = value }, value != null);
                await AssertParsed("String in [null]", new MembershipEntity { String = value }, value == null);
            }
        }

        [Theory]
        [InlineData("not in")]
        [InlineData(" NOT IN ")]
        [InlineData("NoT   iN")]
        [InlineData("not\tin")]
        [InlineData("not\r\nin")]
        public async Task StructuredSpelling_AllEntryPoints(string spelling)
        {
            var filter = new Filter { Property = "Id", Operator = spelling, Value = "1, 2" };
            foreach (var expression in new[]
            {
                filter.BuildPredicate<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)filter.BuildPredicate(typeof(MembershipEntity)),
                await filter.BuildPredicateAsync<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)await filter.BuildPredicateAsync(typeof(MembershipEntity))
            })
            {
                Assert.False(expression.Compile()(new MembershipEntity { Id = 1 }));
                Assert.True(expression.Compile()(new MembershipEntity { Id = 3 }));
            }
            Assert.True(filter.TryBuildPredicate<MembershipEntity>().Succeeded);
            Assert.True(filter.TryBuildPredicate(typeof(MembershipEntity)).Succeeded);
            Assert.True((await filter.TryBuildPredicateAsync<MembershipEntity>()).Succeeded);
            Assert.True((await filter.TryBuildPredicateAsync(typeof(MembershipEntity))).Succeeded);
        }

        [Theory]
        [InlineData("notin")]
        [InlineData("!in")]
        [InlineData("not inside")]
        [InlineData("not i n")]
        [InlineData("not\vin")]
        public async Task StructuredInvalidSpelling(string spelling)
        {
            var filter = new Filter { Property = "Id", Operator = spelling, Value = "1, 2" };
            Assert.Throws<InvalidOperationException>(() => filter.BuildPredicate<MembershipEntity>());
            await Assert.ThrowsAsync<InvalidOperationException>(() => filter.BuildPredicateAsync<MembershipEntity>());
            var result = filter.TryBuildPredicate<MembershipEntity>();
            var asyncResult = await filter.TryBuildPredicateAsync<MembershipEntity>();
            foreach (var failure in new EvaluationResultBase[] { result, asyncResult })
            {
                Assert.False(failure.Succeeded);
                Assert.Contains(filter.Operator, failure.InvalidOperators);
            }
            Assert.Null(result.Result);
            Assert.Null(asyncResult.Result);
        }

        [Fact]
        public async Task VariablesAndMappings_AllEntryPoints()
        {
            var resolver = new VariableResolver();
            resolver.TryAdd("blocked", 2);
            resolver.TryAdd("excluded", new[] { 1, 2 });
            var mapping = new Dictionary<string, string> { ["alias"] = "Id" };
            var allowed = new[] { "alias" };
            foreach (var rhs in new[] { "[1, $blocked]", "$excluded" })
            {
                foreach (var id in new[] { 2, 3 })
                {
                    var query = $"alias not in {rhs}";
                    var generic = Interpreter.ParsePredicate<MembershipEntity>(query, resolver, allowed, mapping);
                    var genericAsync = await Interpreter.ParsePredicateAsync<MembershipEntity>(query, resolver, allowed, mapping);
                    var runtime = Interpreter.ParsePredicate(query, typeof(MembershipEntity), resolver, allowed, mapping);
                    var runtimeAsync = await Interpreter.ParsePredicateAsync(query, typeof(MembershipEntity), resolver, allowed, mapping);
                    foreach (var result in new[] { generic, genericAsync })
                    {
                        Assert.True(result.Succeeded, result.Exception?.ToString());
                        Assert.Equal(id == 3, result.Result.Compile()(new MembershipEntity { Id = id }));
                    }
                    foreach (var result in new[] { runtime, runtimeAsync })
                    {
                        Assert.True(result.Succeeded, result.Exception?.ToString());
                        Assert.Equal(id == 3, ((Expression<Func<MembershipEntity, bool>>)result.Result).Compile()(new MembershipEntity { Id = id }));
                    }
                }
            }
            var filter = new Filter { Property = "alias", Operator = "not in", Value = "1, $blocked" };
            Assert.True(filter.BuildPredicate<MembershipEntity>(resolver, allowed, mapping).Compile()(new MembershipEntity { Id = 3 }));
            Assert.True((await filter.BuildPredicateAsync<MembershipEntity>(resolver, allowed, mapping)).Compile()(new MembershipEntity { Id = 3 }));
            foreach (var query in new[] { "Id not in $missing", "Id not in [$missing]" })
            {
                var parsed = Interpreter.ParsePredicate<MembershipEntity>(query);
                var parsedAsync = await Interpreter.ParsePredicateAsync<MembershipEntity>(query);
                Assert.False(parsed.Succeeded);
                Assert.False(parsedAsync.Succeeded);
                Assert.Contains(parsed.InvalidVariables, name => name.TrimStart('$') == "missing");
                Assert.Contains(parsedAsync.InvalidVariables, name => name.TrimStart('$') == "missing");
                Assert.Null(parsed.Result);
                Assert.Null(parsedAsync.Result);
                foreach (var result in new[]
                {
                    Interpreter.ParsePredicate(query, typeof(MembershipEntity)),
                    await Interpreter.ParsePredicateAsync(query, typeof(MembershipEntity))
                })
                {
                    AssertFailure(result);
                    Assert.Contains(result.InvalidVariables, name => name.TrimStart('$') == "missing");
                }
            }
            var restricted = Interpreter.ParsePredicate<MembershipEntity>("Id not in [1]", validProperties: allowed);
            Assert.False(restricted.Succeeded);
            Assert.Contains("Id", restricted.InvalidProperties);
            var restrictedAsync = await Interpreter.ParsePredicateAsync<MembershipEntity>("Id not in [1]", validProperties: allowed);
            Assert.False(restrictedAsync.Succeeded);
            Assert.Contains("Id", restrictedAsync.InvalidProperties);
            Assert.Contains("alias", filter.TryBuildPredicate<MembershipEntity>(validProperties: new[] { "String" }).InvalidProperties);
            Assert.Contains("alias", (await filter.TryBuildPredicateAsync<MembershipEntity>(validProperties: new[] { "String" })).InvalidProperties);
        }

        [Theory]
        [InlineData("'bad'")]
        [InlineData("$missing")]
        public async Task BuilderFailuresRemainExplicit(string value)
        {
            var filter = new Filter { Property = "Id", Operator = "not in", Value = value };
            Assert.Throws<FormatException>(() => filter.BuildPredicate<MembershipEntity>());
            await Assert.ThrowsAsync<FormatException>(() => filter.BuildPredicateAsync<MembershipEntity>());
            var sync = filter.TryBuildPredicate<MembershipEntity>();
            var asyncResult = await filter.TryBuildPredicateAsync<MembershipEntity>();
            Assert.False(sync.Succeeded);
            Assert.NotNull(sync.Exception);
            Assert.Null(sync.Result);
            Assert.False(asyncResult.Succeeded);
            Assert.NotNull(asyncResult.Exception);
            Assert.Null(asyncResult.Result);
            AssertFailure(filter.TryBuildPredicate(typeof(MembershipEntity)));
            AssertFailure(await filter.TryBuildPredicateAsync(typeof(MembershipEntity)));
        }

        [Fact]
        public async Task AsyncMembership_ForwardsTokenAndAwaitsListElements()
        {
            using var source = new CancellationTokenSource();
            foreach (var op in new[] { ComparisonOperator.In, ComparisonOperator.NotIn })
            {
                var expected = op == ComparisonOperator.In;
                var resolver = new AsyncResolver(source);
                var generic = await Interpreter.BuildPredicateAsync<MembershipEntity>("Id", op, "$blocked", resolver, source.Token);
                var runtime = await Interpreter.BuildPredicateAsync("Id", op, "$blocked", typeof(MembershipEntity), new AsyncResolver(source), source.Token);
                Assert.Equal(expected, generic.Compile()(new MembershipEntity { Id = 2 }));
                Assert.Equal(expected, ((Expression<Func<MembershipEntity, bool>>)runtime).Compile()(new MembershipEntity { Id = 2 }));
                var filter = new Filter { Property = "Id", Operator = expected ? "in" : "not in", Value = "$blocked" };
                Assert.Equal(expected, (await filter.BuildPredicateAsync<MembershipEntity>(new AsyncResolver(source), cancellationToken: source.Token)).Compile()(new MembershipEntity { Id = 2 }));
                Assert.Equal(expected, ((Expression<Func<MembershipEntity, bool>>)await filter.BuildPredicateAsync(typeof(MembershipEntity), new AsyncResolver(source), cancellationToken: source.Token)).Compile()(new MembershipEntity { Id = 2 }));
            }
            foreach (var rhs in new[] { "[$blocked]", "$excluded" })
            {
                var result = await Interpreter.ParsePredicateAsync<MembershipEntity>($"Id not in {rhs}", new AsyncResolver(source), cancellationToken: source.Token);
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.False(result.Result.Compile()(new MembershipEntity { Id = 2 }));
                var runtime = await Interpreter.ParsePredicateAsync($"Id not in {rhs}", typeof(MembershipEntity), new AsyncResolver(source), cancellationToken: source.Token);
                Assert.True(runtime.Succeeded, runtime.Exception?.ToString());
                Assert.False(((Expression<Func<MembershipEntity, bool>>)runtime.Result).Compile()(new MembershipEntity { Id = 2 }));
            }
        }

        [Fact]
        public async Task CancellationDuringResolution_UsesExistingFailureChannels()
        {
            foreach (var rhs in new[] { "[$blocked]", "$excluded" })
            {
                using var parseSource = new CancellationTokenSource();
                var parsed = await Interpreter.ParsePredicateAsync<MembershipEntity>($"Id not in {rhs}",
                    new AsyncResolver(parseSource, true), cancellationToken: parseSource.Token);
                Assert.False(parsed.Succeeded);
                Assert.Null(parsed.Result);
                Assert.IsAssignableFrom<OperationCanceledException>(parsed.Exception);
                Assert.Empty(parsed.InvalidValues);
                using var runtimeSource = new CancellationTokenSource();
                var runtime = await Interpreter.ParsePredicateAsync($"Id not in {rhs}", typeof(MembershipEntity),
                    new AsyncResolver(runtimeSource, true), cancellationToken: runtimeSource.Token);
                AssertFailure(runtime);
                Assert.IsAssignableFrom<OperationCanceledException>(runtime.Exception);
            }
            using var buildSource = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Interpreter.BuildPredicateAsync<MembershipEntity>("Id", ComparisonOperator.NotIn, "$blocked",
                    new AsyncResolver(buildSource, true), buildSource.Token));
            using var filterSource = new CancellationTokenSource();
            var filter = new Filter { Property = "Id", Operator = "not in", Value = "$blocked" };
            var result = await filter.TryBuildPredicateAsync<MembershipEntity>(new AsyncResolver(filterSource, true), cancellationToken: filterSource.Token);
            Assert.False(result.Succeeded);
            Assert.IsAssignableFrom<OperationCanceledException>(result.Exception);
            Assert.Null(result.InvalidValues);
            using var runtimeBuildSource = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                Interpreter.BuildPredicateAsync("Id", ComparisonOperator.NotIn, "$blocked", typeof(MembershipEntity),
                    new AsyncResolver(runtimeBuildSource, true), runtimeBuildSource.Token));
            using var runtimeFilterSource = new CancellationTokenSource();
            var runtimeFilter = await filter.TryBuildPredicateAsync(typeof(MembershipEntity), new AsyncResolver(runtimeFilterSource, true), cancellationToken: runtimeFilterSource.Token);
            AssertFailure(runtimeFilter);
            Assert.IsAssignableFrom<OperationCanceledException>(runtimeFilter.Exception);
            using var throwingFilterSource = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                filter.BuildPredicateAsync<MembershipEntity>(new AsyncResolver(throwingFilterSource, true), cancellationToken: throwingFilterSource.Token));
        }

        [Fact]
        public async Task CollectionsAndGroups_AllEntryPoints()
        {
            var entities = Enumerable.Range(1, 4).Select(id => new MembershipEntity { Id = id }).ToArray();
            var filters = new[]
            {
                new Filter { Property = "Id", Operator = "not in", Value = "1, 2" },
                new Filter { Property = "Id", Operator = "<", Value = "4" }
            };
            var groups = new[]
            {
                new FilterGroup { Filters = filters.ToList() },
                new FilterGroup { Filters = new List<Filter> { new Filter { Property = "Id", Operator = "=", Value = "1" } } }
            };
            var collections = new[]
            {
                filters.BuildPredicate<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)filters.BuildPredicate(typeof(MembershipEntity)),
                await filters.BuildPredicateAsync<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)await filters.BuildPredicateAsync(typeof(MembershipEntity))
            };
            var grouped = new[]
            {
                groups.BuildPredicate<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)groups.BuildPredicate(typeof(MembershipEntity)),
                await groups.BuildPredicateAsync<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)await groups.BuildPredicateAsync(typeof(MembershipEntity))
            };
            foreach (var expression in collections) Assert.Equal(new[] { 3 }, entities.Where(expression.Compile()).Select(e => e.Id));
            foreach (var expression in grouped) Assert.Equal(new[] { 1, 3 }, entities.Where(expression.Compile()).Select(e => e.Id));
        }

        [Fact]
        public async Task CultureAndEnumValues()
        {
            Assert.Equal(new[] { 0, 1, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, Enum.GetValues<ComparisonOperator>().Select(value => (int)value));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                await AssertParsed("Integer NOT IN [1, 2]", new MembershipEntity { Integer = 3 }, true);
                Assert.True(new Filter { Property = "Integer", Operator = " NOT\tIN ", Value = "1, 2" }.BuildPredicate<MembershipEntity>().Compile()(new MembershipEntity { Integer = 3 }));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static async Task AssertParsed(string query, MembershipEntity entity, bool expected, VariableResolver resolver = null)
        {
            foreach (var result in new[]
            {
                Interpreter.ParsePredicate<MembershipEntity>(query, resolver),
                await Interpreter.ParsePredicateAsync<MembershipEntity>(query, resolver)
            })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, result.Result.Compile()(entity));
                if (query.StartsWith("Integer not in", StringComparison.Ordinal)) AssertNativeTree(result.Result);
            }
            foreach (var result in new[]
            {
                Interpreter.ParsePredicate(query, typeof(MembershipEntity), resolver),
                await Interpreter.ParsePredicateAsync(query, typeof(MembershipEntity), resolver)
            })
            {
                Assert.True(result.Succeeded, result.Exception?.ToString());
                Assert.Equal(expected, ((Expression<Func<MembershipEntity, bool>>)result.Result).Compile()(entity));
            }
        }

        private static async Task<Expression<Func<MembershipEntity, bool>>[]> BuildAll(string property, string list)
        {
            var filter = new Filter { Property = property, Operator = "not in", Value = list };
            return new[]
            {
                Interpreter.BuildPredicate<MembershipEntity>(property, ComparisonOperator.NotIn, list),
                (Expression<Func<MembershipEntity, bool>>)Interpreter.BuildPredicate(property, ComparisonOperator.NotIn, list, typeof(MembershipEntity)),
                await Interpreter.BuildPredicateAsync<MembershipEntity>(property, ComparisonOperator.NotIn, list),
                (Expression<Func<MembershipEntity, bool>>)await Interpreter.BuildPredicateAsync(property, ComparisonOperator.NotIn, list, typeof(MembershipEntity)),
                filter.BuildPredicate<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)filter.BuildPredicate(typeof(MembershipEntity)),
                await filter.BuildPredicateAsync<MembershipEntity>(),
                (Expression<Func<MembershipEntity, bool>>)await filter.BuildPredicateAsync(typeof(MembershipEntity))
            };
        }

        private static void AssertFailure(EvaluationResult result)
        {
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Exception);
            Assert.Null(result.Result);
        }

        private static void AssertNativeTree(Expression<Func<MembershipEntity, bool>> expression)
        {
            var body = Assert.IsAssignableFrom<UnaryExpression>(expression.Body);
            Assert.Equal(ExpressionType.Not, body.NodeType);
            var call = Assert.IsAssignableFrom<MethodCallExpression>(body.Operand);
            Assert.Equal(typeof(Enumerable), call.Method.DeclaringType);
            Assert.Equal(nameof(Enumerable.Contains), call.Method.Name);
            Assert.IsType<ConstantExpression>(call.Arguments[0]);
            Assert.IsAssignableFrom<MemberExpression>(call.Arguments[1]);
        }

        public class MembershipEntity : TestEntity
        {
            public int NotInside { get; set; }
            public decimal Decimal { get; set; }
            public TimeSpan Duration { get; set; }
            public TimeSpan? NullableDuration { get; set; }
        }

        internal sealed class AsyncResolver : VariableResolver
        {
            private readonly CancellationTokenSource _source;
            private readonly bool _cancel;

            public AsyncResolver(CancellationTokenSource source, bool cancel = false)
            {
                _source = source;
                _cancel = cancel;
            }

            protected override async Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                Assert.Equal(_source.Token, cancellationToken);
                await Task.Yield();
                if (_cancel) _source.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return new VariableInfo { Name = name, Resolved = true, Value = name == "excluded" ? (object)new[] { 1, 2 } : 2 };
            }
        }
    }
}
