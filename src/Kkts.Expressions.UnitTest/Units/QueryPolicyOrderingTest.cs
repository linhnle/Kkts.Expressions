using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyOrderingTest
    {
        [Fact]
        public void Ordering_UsesFieldPermissionsButNotComparisonOperatorPermissions()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(
                    propertyMapping: new Dictionary<string, string> { ["cost"] = "Price" }),
                new QueryPolicy(
                    maxExpressionLength: 100,
                    maxNavigationDepth: 0,
                    allowCollectionAccess: false,
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new ComparisonOperator[0]
                    }));

            var allowed = context.TryBuildOrderByClause("cost desc");
            var denied = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "NotQueryable" }
            });

            Assert.True(allowed.Succeeded);
            Assert.False(denied.Succeeded);
            Assert.Null(denied.Result);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal("OrderBys[1].Property", denied.Diagnostics[0].InputPath);
        }

        [Fact]
        public void PublicSchema_SeparatesFilteringSortingAndOperatorPermissions()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field(
                    "total",
                    entity => entity.Price * entity.Id,
                    canSort: false,
                    allowedOperators: new[] { ComparisonOperator.Equal })
                .Field(
                    "label",
                    entity => entity.Price,
                    canFilter: false,
                    allowedOperators: Array.Empty<ComparisonOperator>())
                .Field(
                    "hidden",
                    entity => entity.Id,
                    canFilter: false,
                    canSort: false)
                .Build();
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(
                    allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["TOTAL"] = new[]
                        {
                            ComparisonOperator.Equal,
                            ComparisonOperator.GreaterThan
                        }
                    }));

            Assert.True(context.IsPropertyQueryable("total"));
            Assert.False(context.IsPropertySortable("total"));
            Assert.True(context.IsOperatorAllowed("total", ComparisonOperator.Equal));
            Assert.False(context.IsOperatorAllowed("total", ComparisonOperator.GreaterThan));
            Assert.False(context.IsPropertyQueryable("label"));
            Assert.True(context.IsPropertySortable("label"));
            Assert.False(context.IsOperatorAllowed("label", ComparisonOperator.Equal));
            Assert.False(context.IsPropertyQueryable("hidden"));
            Assert.False(context.IsPropertySortable("hidden"));
            Assert.Equal(
                new[] { ComparisonOperator.Equal },
                context.PublicFields[0].AllowedOperators);
            Assert.False(context.PublicFields[1].CanFilter);
            Assert.True(context.PublicFields[1].CanSort);
            Assert.Empty(context.PublicFields[1].AllowedOperators);
            Assert.Empty(new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy()).PublicFields);

        }

        [Fact]
        public async Task PublicSchema_ComposesComputedFieldsForStringAndStructuredPredicates()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field("total", entity => entity.Price * entity.Id)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var entities = new[]
            {
                new OrderingEntity { Id = 2, Price = 5m },
                new OrderingEntity { Id = 3, Price = 4m },
                new OrderingEntity { Id = 4, Price = 3m }
            };

            var parsed = context.ParsePredicate("total = 12");
            var direct = context.TryBuildPredicate(
                "total",
                ComparisonOperator.Equal,
                12m);
            var structured = context.TryBuildPredicate(new Filter
            {
                Property = "total",
                Operator = "=",
                Value = "12"
            });
            var asyncParsed = await context.ParsePredicateAsync("total = 12");
            var asyncDirect = await context.TryBuildPredicateAsync(
                "total",
                ComparisonOperator.Equal,
                12m);
            var tree = context.TryBuildPredicate(
                FilterNode.Condition("total", "=", FilterValue.Number("12")));
            var membership = context.TryBuildPredicate(
                "total",
                ComparisonOperator.In,
                new[] { 10m, 12m });
            var treeMembership = context.TryBuildPredicate(
                FilterNode.Condition(
                    "total",
                    "in",
                    FilterValue.Collection(new[]
                    {
                        FilterValue.Number("10"),
                        FilterValue.Number("12")
                    })));
            var parsedTree = context.ParseFilterTree("total = 12");
            var formattedTree = context.FormatFilterTree(parsedTree.Result);
            var flatFilters = context.TryBuildPredicate(new[]
            {
                new Filter { Property = "total", Operator = ">=", Value = "10" },
                new Filter { Property = "total", Operator = "<", Value = "13" }
            });
            var filterGroup = new FilterGroup
            {
                Filters = new List<Filter>
                {
                    new Filter { Property = "total", Operator = "=", Value = "10" }
                }
            };
            var grouped = context.TryBuildPredicate(filterGroup);
            var asyncFlatFilters = await context.TryBuildPredicateAsync(new[]
            {
                new Filter { Property = "total", Operator = ">=", Value = "10" },
                new Filter { Property = "total", Operator = "<", Value = "13" }
            });
            var asyncGrouped = await context.TryBuildPredicateAsync(new[] { filterGroup });

            Assert.True(parsed.Succeeded, parsed.Exception?.ToString());
            Assert.True(direct.Succeeded, direct.Exception?.ToString());
            Assert.True(structured.Succeeded, structured.Exception?.ToString());
            Assert.True(asyncParsed.Succeeded, asyncParsed.Exception?.ToString());
            Assert.True(asyncDirect.Succeeded, asyncDirect.Exception?.ToString());
            Assert.True(tree.Succeeded, tree.Exception?.ToString());
            Assert.True(membership.Succeeded, membership.Exception?.ToString());
            Assert.True(treeMembership.Succeeded, treeMembership.Exception?.ToString());
            Assert.True(parsedTree.Succeeded);
            Assert.Equal("total", parsedTree.Result.Field);
            Assert.True(formattedTree.Succeeded);
            Assert.Equal("total = 12", formattedTree.Result);
            Assert.DoesNotContain("Price", formattedTree.Result);
            Assert.True(flatFilters.Succeeded, flatFilters.Exception?.ToString());
            Assert.True(grouped.Succeeded, grouped.Exception?.ToString());
            Assert.True(asyncFlatFilters.Succeeded, asyncFlatFilters.Exception?.ToString());
            Assert.True(asyncGrouped.Succeeded, asyncGrouped.Exception?.ToString());
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)parsed.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)direct.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)structured.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)asyncParsed.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)asyncDirect.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)tree.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 2, 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)flatFilters.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 2 },
                entities.Where((Func<OrderingEntity, bool>)grouped.Result.Compile())
                    .Select(entity => entity.Id));
            Assert.Equal(
                new[] { 2, 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)asyncFlatFilters.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 2 },
                entities.Where((Func<OrderingEntity, bool>)asyncGrouped.Result.Compile())
                    .Select(entity => entity.Id));
            Assert.Equal(
                new[] { 2, 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)membership.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));
            Assert.Equal(
                new[] { 2, 3, 4 },
                entities.Where((Func<OrderingEntity, bool>)treeMembership.Result.Compile())
                    .Select(entity => entity.Id)
                    .OrderBy(id => id));

            var deniedSchema = new QuerySchema<OrderingEntity>()
                .Field("total", entity => entity.Price * entity.Id, canFilter: false)
                .Build();
            var denied = new ExpressionQueryContext(deniedSchema, new QueryPolicy())
                .ParsePredicate("total = 12");
            Assert.False(denied.Succeeded);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
        }

        [Fact]
        public void PublicSchema_DoesNotResolveUnknownOrDeniedBareNamesAsVariables()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field("total", entity => entity.Price * entity.Id, canFilter: false)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var resolver = new ProbeVariableResolver();

            var unknown = context.ParsePredicate("internalName = 1", resolver);
            var denied = context.ParsePredicate("total = 1", resolver);

            Assert.False(unknown.Succeeded);
            Assert.False(denied.Succeeded);
            Assert.Equal(0, resolver.ResolutionAttempts);
        }

        [Fact]
        public void PublicSchema_ComposesTypedQueryableAndEnumerableOrderingKeys()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field("total", entity => entity.Price * entity.Id)
                .Field("id", entity => entity.Id)
                .Field("filterOnly", entity => entity.Price, canSort: false)
                .Field("sortOnly", entity => entity.Price, canFilter: false)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var entities = new[]
            {
                new OrderingEntity { Id = 1, Price = 5m },
                new OrderingEntity { Id = 2, Price = 5m },
                new OrderingEntity { Id = 3, Price = 4m },
                new OrderingEntity { Id = 4, Price = 3m }
            };
            var clause = context.TryBuildOrderByClause("total desc, id asc");
            var structuredClause = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "total", Descending = true },
                new OrderByInfo { Property = "id" }
            });
            var denied = context.TryBuildOrderByClause("filterOnly");
            var sortOnly = context.TryBuildOrderByClause("sortOnly");
            var unknown = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "internalName" }
            });

            Assert.True(clause.Succeeded, clause.Exception?.ToString());
            Assert.True(structuredClause.Succeeded, structuredClause.Exception?.ToString());
            Assert.False(denied.Succeeded);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
            Assert.True(sortOnly.Succeeded, sortOnly.Exception?.ToString());
            Assert.False(unknown.Succeeded);
            Assert.Equal("OrderBys[0].Property",
                Assert.Single(unknown.Diagnostics).InputPath);

            var queryable = clause.Result.Sort(entities.AsQueryable());
            Assert.Equal(new[] { 3, 4, 2, 1 }, queryable.Select(entity => entity.Id));
            Assert.Equal(new[] { 3, 4, 2, 1 },
                structuredClause.Result.Sort(entities.AsQueryable()).Select(entity => entity.Id));
            Assert.Equal(new[] { 3, 4, 2, 1 },
                clause.Result.Sort(entities).Select(entity => entity.Id));
            Assert.DoesNotContain("Invoke", queryable.Expression.ToString());

            var queryOrderBy = FindQueryableMethod(
                queryable.Expression,
                nameof(Queryable.OrderByDescending));
            Assert.Equal(typeof(decimal), queryOrderBy.Method.GetGenericArguments()[1]);
        }

        [Fact]
        public void PublicSchema_EnumerableOrderingDefersSelectorExecutionUntilEnumeration()
        {
            OrderingEntity.SortKeyCalls = 0;
            var schema = new QuerySchema<OrderingEntity>()
                .Field("sortKey", entity => entity.GetSortKey())
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var clause = context.TryBuildOrderByClause("sortKey");

            Assert.True(clause.Succeeded, clause.Exception?.ToString());
            Assert.Equal(0, OrderingEntity.SortKeyCalls);

            var ordered = clause.Result.Sort(new[]
            {
                new OrderingEntity { Id = 2 },
                new OrderingEntity { Id = 1 }
            });
            Assert.Equal(0, OrderingEntity.SortKeyCalls);
            Assert.Equal(new[] { 1, 2 }, ordered.Select(entity => entity.Id));
            Assert.Equal(2, OrderingEntity.SortKeyCalls);
        }

        [Fact]
        public void PublicSchema_ReportsConsistentFailuresWithoutExposingSelectorDetails()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field(
                    "computed",
                    entity => entity.Price * entity.Id,
                    allowedOperators: new[] { ComparisonOperator.Equal })
                .Field("filterDenied", entity => entity.Price, canFilter: false)
                .Field("sortDenied", entity => entity.Id, canSort: false)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var unknown = context.AnalyzeExpression("Price = 1");
            var denied = context.ParsePredicate("filterDenied = 1");
            var disallowedOperator = context.AnalyzeExpression("computed > 1");
            var incompatibleOperator = context.AnalyzeExpression("computed.contains('x')");
            var incompatibleValue = context.AnalyzeExpression("computed = 'bad'");
            var suffix = context.AnalyzeExpression("computed.Year = 1");
            var flatDenied = context.TryBuildPredicate(new Filter
            {
                Property = "filterDenied",
                Operator = "=",
                Value = "1"
            });
            var treeDenied = context.TryBuildPredicate(
                FilterNode.Condition("filterDenied", "=", FilterValue.Number("1")));
            var sortDenied = context.TryBuildOrderByClause("sortDenied");

            Assert.Equal("unknown-property", Assert.Single(unknown.SemanticDiagnostics).Code);
            Assert.False(denied.Succeeded);
            Assert.Equal("property-not-queryable", Assert.Single(denied.Diagnostics).Code);
            Assert.Equal("query-policy-operator-denied",
                Assert.Single(disallowedOperator.SemanticDiagnostics).Code);
            Assert.Contains(incompatibleOperator.SemanticDiagnostics, diagnostic =>
                diagnostic.Code == "operator-not-applicable");
            Assert.Equal("incompatible-operand",
                Assert.Single(incompatibleValue.SemanticDiagnostics).Code);
            Assert.Equal("unknown-property", Assert.Single(suffix.SemanticDiagnostics).Code);
            Assert.False(flatDenied.Succeeded);
            Assert.Null(flatDenied.Result);
            Assert.False(treeDenied.Succeeded);
            Assert.Null(treeDenied.Result);
            Assert.False(sortDenied.Succeeded);
            Assert.Null(sortDenied.Result);

            var diagnosticText = string.Join(
                " ",
                denied.Diagnostics.Select(diagnostic => diagnostic.Message));
            Assert.DoesNotContain("Price", diagnosticText);
        }

        [Fact]
        public void PublicSchema_ConditionOptionsComposesAllPredicateAndOrderingSurfaces()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field("total", entity => entity.Price * entity.Id)
                .Field("id", entity => entity.Id)
                .Field("filterOnly", entity => entity.Price, canSort: false)
                .Build();
            var context = new ExpressionQueryContext(schema, new QueryPolicy());
            var options = new ConditionOptions
            {
                Where = "total > 9",
                Filters = new[]
                {
                    new Filter { Property = "total", Operator = "<", Value = "20" }
                },
                FilterGroups = new[]
                {
                    new FilterGroup
                    {
                        Filters = new List<Filter>
                        {
                            new Filter { Property = "total", Operator = ">=", Value = "10" }
                        }
                    }
                },
                FilterTree = FilterNode.Condition(
                    "total",
                    "!=",
                    FilterValue.Number("0")),
                OrderBy = "total desc, id asc"
            };
            var condition = context.BuildCondition<OrderingEntity>(options);
            var entities = new[]
            {
                new OrderingEntity { Id = 1, Price = 5m },
                new OrderingEntity { Id = 2, Price = 5m },
                new OrderingEntity { Id = 3, Price = 4m },
                new OrderingEntity { Id = 4, Price = 3m }
            };
            var result = condition.OrderByClause.Sort(
                    entities.Where(entity =>
                        condition.Predicates.All(predicate => predicate.Compile()(entity))))
                .Select(entity => entity.Id);

            Assert.True(condition.IsValid, condition.Error?.EvaluationResult?.Exception?.ToString());
            Assert.Equal(new[] { 3, 4, 2 }, result);

            var failed = context.BuildCondition<OrderingEntity>(new ConditionOptions
            {
                Where = "total > 9",
                OrderBy = "filterOnly"
            });
            Assert.False(failed.IsValid);
            Assert.Null(failed.OrderByClause);
        }

        private static System.Linq.Expressions.MethodCallExpression FindQueryableMethod(
            System.Linq.Expressions.Expression expression,
            string methodName)
        {
            while (expression is System.Linq.Expressions.MethodCallExpression call)
            {
                if (call.Method.DeclaringType == typeof(Queryable) &&
                    call.Method.Name == methodName)
                    return call;
                expression = call.Arguments[0];
            }
            throw new InvalidOperationException($"Queryable method '{methodName}' was not found.");
        }

        [Fact]
        public void PublicSchema_AdditionalNamesOnlyNarrowRegisteredFields()
        {
            var schema = new QuerySchema<OrderingEntity>()
                .Field("amount", entity => entity.Price)
                .Field("id", entity => entity.Id)
                .Build();
            var allowedNames = new List<string> { "AMOUNT" };
            var narrowed = new ExpressionQueryContext(
                schema,
                new QueryPolicy(),
                validProperties: allowedNames);
            allowedNames[0] = "id";
            var emptyNarrowing = new ExpressionQueryContext(
                schema,
                new QueryPolicy(),
                validProperties: System.Array.Empty<string>());

            Assert.True(narrowed.IsPropertyQueryable("amount"));
            Assert.False(narrowed.IsPropertyQueryable("id"));
            Assert.True(emptyNarrowing.IsPropertyQueryable("amount"));
            Assert.True(emptyNarrowing.IsPropertyQueryable("id"));
            Assert.Throws<System.ArgumentException>(() =>
                emptyNarrowing.ParsePredicate<OtherOrderingEntity>("Id = 1"));
            Assert.Throws<System.ArgumentException>(() =>
                new ExpressionQueryContext(
                    schema,
                    new QueryPolicy(),
                    validProperties: new[] { "Price" }));
            Assert.Throws<System.ArgumentException>(() =>
                new ExpressionQueryContext(
                    schema,
                    new QueryPolicy(allowedOperators: new Dictionary<string, IEnumerable<ComparisonOperator>>
                    {
                        ["Price"] = new[] { ComparisonOperator.Equal }
                    })));
        }

        [Fact]
        public void PublicSchema_TreatsRegisteredExpressionsAsOpaquePolicyFields()
        {
            var schema = new QuerySchema<ComplexOrderingEntity>()
                .Field("city", entity => entity.Customer.Name)
                .Field("itemCount", entity => entity.Items.Count)
                .Build();
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(
                    maxNavigationDepth: 0,
                    allowCollectionAccess: false));

            Assert.True(context.IsNavigationAllowed("city", out var cityDepth));
            Assert.Equal(0, cityDepth);
            Assert.True(context.IsCollectionAccessAllowed("city"));
            Assert.True(context.IsNavigationAllowed("itemCount", out var countDepth));
            Assert.Equal(0, countDepth);
            Assert.True(context.IsCollectionAccessAllowed("itemCount"));
            Assert.False(context.IsNavigationAllowed("Customer.Name", out _));
            Assert.False(context.IsCollectionAccessAllowed("Items.Count"));
        }

        [Fact]
        public void Ordering_RejectsLengthOverrunAndReturnsNoSortedSource()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 2));
            var source = new[] { new OrderingEntity { Id = 2 }, new OrderingEntity { Id = 1 } }.AsQueryable();

            var result = context.TryOrderBy(source, "Id ");

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(result.Diagnostics).Code);
        }

        [Fact]
        public void StructuredOrderingChecksCumulativePropertyLengthAndSupportsEnumerableSources()
        {
            var context = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 4));
            var overLimit = context.TryBuildOrderByClause(new[]
            {
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "Id" },
                new OrderByInfo { Property = "Id" }
            });

            Assert.False(overLimit.Succeeded);
            Assert.Equal("query-policy-expression-length-exceeded", Assert.Single(overLimit.Diagnostics).Code);
            Assert.Equal("OrderBys[2].Property", overLimit.Diagnostics[0].InputPath);

            var allowed = new ExpressionQueryContext(
                ExpressionSchema.FromType<OrderingEntity>(),
                new QueryPolicy(maxExpressionLength: 4));
            var sorted = allowed.TryOrderBy(
                new[] { new OrderingEntity { Id = 2 }, new OrderingEntity { Id = 1 } },
                new[] { new OrderByInfo { Property = "Id" } });

            Assert.True(sorted.Succeeded);
            Assert.Equal(new[] { 1, 2 }, sorted.Result.Select(entity => entity.Id));
        }

        [Fact]
        public void Ordering_EnforcesNavigationAndCollectionRestrictionsThroughAliases()
        {
            var schema = ExpressionSchema.FromType<ComplexOrderingEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["city"] = "Customer.Name",
                    ["itemCount"] = "Items.Count"
                });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(
                    maxNavigationDepth: 0,
                    allowCollectionAccess: false));

            var navigation = context.TryBuildOrderByClause(
                new OrderByInfo { Property = "city" });
            var collectionContext = new ExpressionQueryContext(
                schema,
                new QueryPolicy(allowCollectionAccess: false));
            var collection = collectionContext.TryBuildOrderByClause(
                new OrderByInfo { Property = "itemCount" });

            Assert.False(navigation.Succeeded);
            Assert.Equal("query-policy-navigation-depth-exceeded", Assert.Single(navigation.Diagnostics).Code);
            Assert.Equal("OrderBys[0].Property", navigation.Diagnostics[0].InputPath);
            Assert.False(collection.Succeeded);
            Assert.Equal("query-policy-collection-access-denied", Assert.Single(collection.Diagnostics).Code);
        }

        private sealed class OrderingEntity
        {
            public static int SortKeyCalls { get; set; }

            public int Id { get; set; }

            public decimal Price { get; set; }

            public int GetSortKey()
            {
                SortKeyCalls++;
                return Id;
            }
        }

        private sealed class OtherOrderingEntity
        {
            public int Id { get; set; }
        }

        private sealed class ProbeVariableResolver : VariableResolver
        {
            public int ResolutionAttempts { get; private set; }

            protected override Task<VariableInfo> TryResolveCore(
                string name,
                CancellationToken cancellationToken)
            {
                ResolutionAttempts++;
                return Task.FromResult(new VariableInfo { Name = name });
            }
        }

        private sealed class ComplexOrderingEntity
        {
            public Customer Customer { get; set; }

            public List<Item> Items { get; set; }
        }

        private sealed class Customer
        {
            public string Name { get; set; }
        }

        private sealed class Item
        {
            public int Id { get; set; }
        }
    }
}
