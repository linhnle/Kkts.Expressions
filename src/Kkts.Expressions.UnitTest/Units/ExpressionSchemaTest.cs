using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionSchemaTest
    {
        private static int _selectorMethodCalls;

        [Fact]
        public void MetadataConstructionGuideExample_UsesPublicTypes()
        {
            var schema = ExpressionSchema.FromType<GuideProduct>(
                validProperties: new[] { "Price", "Discount", "Name", "buyer" },
                propertyMapping: new Dictionary<string, string>
                {
                    ["buyer"] = "Customer.Name"
                },
                properties: new[]
                {
                    new ExpressionPropertyDefinition(
                        "InternalCost",
                        typeof(decimal),
                        canQuery: false),
                    new ExpressionPropertyDefinition(
                        "Name",
                        typeof(string),
                        ExpressionNullability.NonNullable)
                });

            Assert.Equal(typeof(GuideProduct), schema.EntityType);
            Assert.True(schema.IsPropertyQueryable("buyer"));
            Assert.False(schema.IsPropertyQueryable("InternalCost"));

            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(GuideUser)),
                new ExpressionVariableDefinition("minimum", typeof(decimal)),
                new ExpressionVariableDefinition("prices", typeof(decimal[]))
            });
            Assert.True(variables.TryGetVariable("user.Department", out var departmentType, out _, out _, out _));
            Assert.Equal(typeof(string), departmentType);
        }

        [Fact]
        public void QuerySchema_RegistersTypedFieldsAndCreatesExplicitPublicSnapshot()
        {
            SchemaEntity.GetterCalls = 0;
            var builder = new QuerySchema<GuideProduct>()
                .Field("buyer", product => product.Customer.Name)
                .Field(
                    "total",
                    product => product.Price * 2m,
                    canSort: false,
                    displayName: "Total",
                    description: "Order total")
                .Field("discount", product => product.Discount);
            var schema = builder.Build();

            Assert.True(schema.IsPublicSchema);
            Assert.Equal(typeof(GuideProduct), schema.EntityType);
            Assert.Collection(
                schema.Fields,
                buyer =>
                {
                    Assert.Equal("buyer", buyer.Name);
                    Assert.Equal(typeof(string), buyer.ClrType);
                    Assert.Equal(ExpressionNullability.Unknown, buyer.Nullability);
                    Assert.True(buyer.CanFilter);
                    Assert.True(buyer.CanSort);
                    Assert.Null(buyer.AllowedOperators);
                },
                total =>
                {
                    Assert.Equal(typeof(decimal), total.ClrType);
                    Assert.True(total.CanFilter);
                    Assert.False(total.CanSort);
                    Assert.Equal("Total", total.DisplayName);
                    Assert.Equal("Order total", total.Description);
                },
                discount =>
                {
                    Assert.Equal(typeof(decimal?), discount.ClrType);
                    Assert.Equal(ExpressionNullability.Nullable, discount.Nullability);
                });

            Assert.True(schema.TryGetExpressionField("TOTAL", out var registered));
            Assert.Equal(typeof(decimal), registered.Selector.ReturnType);
            Assert.Equal(ExpressionType.Multiply, registered.Selector.Body.NodeType);
            Assert.Equal(0, SchemaEntity.GetterCalls);
            Assert.False(ExpressionSchema.FromType<GuideProduct>().IsPublicSchema);
            Assert.Empty(ExpressionSchema.FromType<GuideProduct>().Fields);
            Assert.Empty(new QuerySchema<GuideProduct>().Build().Fields);
            Assert.True(new QuerySchema<GuideProduct>().Build().IsPublicSchema);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("buyer.name")]
        [InlineData(" buyer")]
        [InlineData("buyer ")]
        [InlineData("buyer name")]
        [InlineData("$buyer")]
        [InlineData("null")]
        [InlineData("contains")]
        [InlineData("and")]
        public void QuerySchema_RejectsUnsupportedFieldNames(string name)
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                new QuerySchema<GuideProduct>().Field(name, product => product.Price));

            Assert.Contains(name, exception.Message);
        }

        [Fact]
        public void QuerySchema_RejectsDuplicateNamesAndInvalidSelectorShapes()
        {
            var schema = new QuerySchema<GuideProduct>()
                .Field("buyer", product => product.Price);
            Assert.Throws<ArgumentException>(() =>
                schema.Field("BUYER", product => product.Price));

            var parameter = Expression.Parameter(typeof(GuideProduct), "product");
            var external = Expression.Parameter(typeof(decimal), "external");
            var invocation = Expression.Invoke(
                Expression.Constant((Func<GuideProduct, decimal>)(product => product.Price)),
                parameter);
            var nestedLambda = Expression.Call(
                typeof(ExpressionSchemaTest).GetMethod(
                    nameof(ConsumeSelector),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
                Expression.Lambda<Func<GuideProduct, decimal>>(
                    Expression.Property(parameter, nameof(GuideProduct.Price)),
                    parameter));
            var assignment = Expression.Assign(
                Expression.Property(parameter, nameof(GuideProduct.Price)),
                Expression.Constant(1m));
            var block = Expression.Block(Expression.Constant(1m));
            var constructed = Expression.New(
                typeof(decimal).GetConstructor(new[] { typeof(int) }),
                Expression.Constant(1));
            var freeParameter = Expression.Add(
                Expression.Property(parameter, nameof(GuideProduct.Price)),
                external);
            var dynamic = Expression.Dynamic(
                new ConstantDecimalBinder(),
                typeof(decimal),
                Expression.Constant(new object()));
            var extension = new UnsupportedExtensionExpression();

            AssertUnsupportedSelector<GuideProduct, decimal>(
                "invocation", parameter, invocation);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "nested", parameter, nestedLambda);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "assignment", parameter, assignment);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "block", parameter, block);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "construction", parameter, constructed);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "dynamic", parameter, dynamic);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "extension", parameter, extension);
            AssertUnsupportedSelector<GuideProduct, decimal>(
                "freeParameter", parameter, freeParameter);
            AssertUnsupportedSelector<IndexSelectorEntity, int>(
                "index", Expression.Parameter(typeof(IndexSelectorEntity), "entity"),
                Expression.MakeIndex(
                    Expression.Parameter(typeof(IndexSelectorEntity), "entity"),
                    typeof(IndexSelectorEntity).GetProperty("Item"),
                    new[] { Expression.Constant(0) }));
            Assert.Throws<ArgumentException>(() =>
                new QuerySchema<GuideProduct>().Field(
                    "buyer",
                    product => product.Customer));
            var unchanged = new QuerySchema<GuideProduct>()
                .Field("price", product => product.Price);
            Assert.Throws<ArgumentException>(() =>
                unchanged.Field("invalid", product => product.Customer));
            Assert.Single(unchanged.Build().Fields);
            Assert.Throws<ArgumentNullException>(() =>
                new QuerySchema<GuideProduct>().Field<decimal>("price", null));
            Assert.Throws<ArgumentException>(() =>
                QuerySelectorValidator.Validate(
                    typeof(GuideProduct),
                    "wrongEntity",
                    (Expression<Func<GuideCustomer, decimal>>)(customer => customer.Rank)));
        }

        [Fact]
        public void QuerySchema_AcceptsMethodCallsWithoutExecutingThem()
        {
            _selectorMethodCalls = 0;
            var schema = new QuerySchema<GuideProduct>()
                .Field("businessValue", product => CalculateBusinessValue(product.Price))
                .Build();

            Assert.Equal(0, _selectorMethodCalls);
            Assert.True(schema.TryGetExpressionField("businessValue", out var registered));
            Assert.Equal(ExpressionType.Call, registered.Selector.Body.NodeType);
        }

        [Fact]
        public void QuerySchema_InfersValueNullabilityAndSnapshotsOperators()
        {
            var operators = new List<ComparisonOperator>
            {
                ComparisonOperator.Equal,
                ComparisonOperator.In
            };
            var builder = new QuerySchema<GuideProduct>()
                .Field("price", product => product.Price, allowedOperators: operators)
                .Field("discount", product => product.Discount);
            var schema = builder.Build();
            operators.Clear();
            builder.Field("name", product => product.Name);

            Assert.Equal(ExpressionNullability.NonNullable, schema.Fields[0].Nullability);
            Assert.Equal(ExpressionNullability.Nullable, schema.Fields[1].Nullability);
            Assert.Equal(
                new[] { ComparisonOperator.Equal, ComparisonOperator.In },
                schema.Fields[0].AllowedOperators);
            Assert.Equal(2, schema.Fields.Count);
            Assert.Empty(new QuerySchema<GuideProduct>()
                .Field("price", product => product.Price, allowedOperators: Array.Empty<ComparisonOperator>())
                .Build()
                .Fields[0]
                .AllowedOperators);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ComparisonOperator>)schema.Fields[0].AllowedOperators).Add(
                    ComparisonOperator.NotEqual));
            Assert.Throws<ArgumentException>(() =>
                new QuerySchema<GuideProduct>().Field(
                    "price",
                    product => product.Price,
                    allowedOperators: new[] { (ComparisonOperator)2 }));
            Assert.Throws<ArgumentException>(() =>
                new QuerySchema<GuideProduct>().Field(
                    "price",
                    product => product.Price,
                    nullability: ExpressionNullability.Nullable));
            Assert.Throws<ArgumentException>(() =>
                new QuerySchema<GuideProduct>().Field(
                    "discount",
                    product => product.Discount,
                    nullability: ExpressionNullability.NonNullable));
        }

        [Fact]
        public void QuerySchema_RejectsMixedLegacyMappingsAndCanonicalOverrides()
        {
            var registered = new QuerySchema<GuideProduct>()
                .Field("total", product => product.Price * 2m)
                .Build();
            Assert.True(registered.TryGetExpressionField("total", out var field));

            Assert.Throws<ArgumentException>(() =>
                ExpressionSchema.FromPublicFields(
                    typeof(GuideProduct),
                    new[] { field },
                    null,
                    propertyMapping: new Dictionary<string, string>
                    {
                        ["cost"] = nameof(GuideProduct.Price)
                    }));
            Assert.Throws<ArgumentException>(() =>
                ExpressionSchema.FromPublicFields(
                    typeof(GuideProduct),
                    new[] { field },
                    null,
                    properties: new[]
                    {
                        new ExpressionPropertyDefinition(
                            nameof(GuideProduct.Price),
                            typeof(decimal),
                            canQuery: false)
                    }));

            Assert.True(ExpressionSchema.FromType<GuideProduct>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["cost"] = nameof(GuideProduct.Price)
                }).TryMapProperty("cost", out var path));
            Assert.Equal(nameof(GuideProduct.Price), path);
        }

        [Fact]
        public void SchemaFieldResolution_UsesExactPublicNamesAndLegacyCanonicalPaths()
        {
            var publicSchema = new QuerySchema<GuideProduct>()
                .Field("buyer", product => product.Customer.Name)
                .Field("total", product => product.Price * 2m)
                .Build();

            Assert.True(publicSchema.TryResolveQueryField("BUYER", out var buyer));
            Assert.True(buyer.IsExpressionMapped);
            Assert.Equal("buyer", buyer.Identity);
            Assert.Equal(typeof(string), buyer.ClrType);
            Assert.Equal(0, buyer.NavigationDepth);
            Assert.False(publicSchema.TryResolveQueryField("Customer.Name", out _));
            Assert.False(publicSchema.TryResolveQueryField("buyer.Name", out _));
            Assert.False(publicSchema.TryResolveQueryField("unknown", out _));
            Assert.False(publicSchema.TryResolveQueryField(nameof(GuideProduct.Price), out _));
            Assert.False(publicSchema.TryMapProperty("Customer.Name", out _));
            Assert.False(publicSchema.TryMapProperty("buyer.Name", out _));
            Assert.False(publicSchema.TryGetProperty("buyer.Name", out _, out _, out _));
            Assert.False(publicSchema.IsPropertyQueryable(nameof(GuideProduct.Price)));
            Assert.False(new QuerySchema<GuideProduct>()
                .Build()
                .TryResolveQueryField(nameof(GuideProduct.Price), out _));

            var legacySchema = ExpressionSchema.FromType<GuideProduct>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["buyer"] = "Customer.Name"
                });
            Assert.True(legacySchema.TryResolveQueryField("buyer", out var legacyBuyer));
            Assert.False(legacyBuyer.IsExpressionMapped);
            Assert.Equal("Customer.Name", legacyBuyer.Identity);
            Assert.Equal(typeof(string), legacyBuyer.ClrType);
        }

        [Fact]
        public void ResolvedFields_RebindParametersByIdentityAndPreserveSelectorNodes()
        {
            _selectorMethodCalls = 0;
            var schema = new QuerySchema<GuideProduct>()
                .Field("computedA", x => checked(x.Price * 2m))
                .Field("computedB", x => CalculateBusinessValue(x.Price))
                .Field("nullableTotal", product => product.Discount ?? 3m)
                .Field(
                    "conditional",
                    product => product.Customer == null ? 0 : product.Customer.Rank)
                .Field(
                    "nullableRank",
                    product => product.Customer == null
                        ? (int?)null
                        : product.Customer.Rank)
                .Field("checkedInteger", product => checked(product.Customer.Rank * 2))
                .Build();
            var operationParameter = Expression.Parameter(typeof(GuideProduct), "x");

            Assert.True(schema.TryResolveQueryField("computedA", out var first));
            Assert.True(schema.TryResolveQueryField("computedB", out var second));
            Assert.True(schema.TryResolveQueryField("nullableTotal", out var nullable));
            Assert.True(schema.TryResolveQueryField("conditional", out var conditional));
            Assert.True(schema.TryResolveQueryField("nullableRank", out var nullableRank));
            Assert.True(schema.TryResolveQueryField("checkedInteger", out var checkedInteger));
            var firstBody = first.Compose(operationParameter);
            var secondBody = second.Compose(operationParameter);
            var nullableBody = nullable.Compose(operationParameter);
            var conditionalBody = conditional.Compose(operationParameter);

            Assert.Equal(typeof(decimal), firstBody.Type);
            Assert.Equal(ExpressionType.Multiply, firstBody.NodeType);
            Assert.Equal(ExpressionType.Call, secondBody.NodeType);
            Assert.Equal(
                ((MethodCallExpression)secondBody).Method,
                typeof(ExpressionSchemaTest).GetMethod(
                    nameof(CalculateBusinessValue),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            Assert.Equal(typeof(decimal), nullableBody.Type);
            Assert.Equal(ExpressionType.Coalesce, nullableBody.NodeType);
            Assert.Equal(ExpressionType.Conditional, conditionalBody.NodeType);
            Assert.Equal(ExpressionType.MultiplyChecked, checkedInteger.Compose(operationParameter).NodeType);
            Assert.Equal(typeof(int?), nullableRank.Compose(operationParameter).Type);
            Assert.Equal(0, _selectorMethodCalls);

            var predicateBody = Expression.AndAlso(
                Expression.GreaterThan(firstBody, Expression.Constant(4m)),
                Expression.GreaterThan(secondBody, Expression.Constant(4m)));
            var freeParameters = new ParameterCollector();
            freeParameters.Visit(predicateBody);
            Assert.Equal(new[] { operationParameter }, freeParameters.Parameters);
            Assert.False(ContainsInvocation(predicateBody));

            var predicate = Expression.Lambda<Func<GuideProduct, bool>>(
                predicateBody,
                operationParameter).Compile();
            Assert.True(predicate(new GuideProduct { Price = 5m }));
            Assert.Equal(1, _selectorMethodCalls);

            var legacy = ExpressionSchema.FromType<GuideProduct>(
                propertyMapping: new Dictionary<string, string> { ["buyer"] = "Customer.Name" });
            Assert.True(legacy.TryResolveQueryField("buyer", out var legacyField));
            var legacyParameter = Expression.Parameter(typeof(GuideProduct), "legacy");
            var legacyBody = legacyField.Compose(legacyParameter);
            Assert.Equal(typeof(string), legacyBody.Type);
            Assert.False(ContainsInvocation(legacyBody));
        }

        [Fact]
        public void MappedNavigation_PreservesExplicitNullGuardsAndDoesNotRunDuringComposition()
        {
            SchemaEntity.GetterCalls = 0;
            var schema = new QuerySchema<SchemaEntity>()
                .Field("nextCount", entity => entity.Next.Count)
                .Build();
            Assert.Equal(0, SchemaEntity.GetterCalls);
            Assert.True(schema.TryResolveQueryField("nextCount", out var resolved));

            var parameter = Expression.Parameter(typeof(SchemaEntity), "entity");
            var body = resolved.Compose(parameter);
            Assert.Equal(typeof(int), body.Type);
            Assert.Equal(0, SchemaEntity.GetterCalls);
            var unguarded = Expression.Lambda<Func<SchemaEntity, int>>(body, parameter).Compile();
            Assert.Throws<InvalidOperationException>(() => unguarded(new SchemaEntity()));
            Assert.Equal(1, SchemaEntity.GetterCalls);

            var publicSchema = new QuerySchema<GuideProduct>()
                .Field(
                    "customerName",
                    product => product.Customer == null
                        ? null
                        : product.Customer.Name,
                    nullability: ExpressionNullability.Nullable)
                .Field("unguardedName", product => product.Customer.Name)
                .Field(
                    "customerRank",
                    product => product.Customer == null
                        ? (int?)null
                        : product.Customer.Rank)
                .Build();
            Assert.True(publicSchema.TryResolveQueryField("customerRank", out var rankField));
            var rankParameter = Expression.Parameter(typeof(GuideProduct), "order");
            var rankSelector = Expression.Lambda<Func<GuideProduct, int?>>(
                rankField.Compose(rankParameter),
                rankParameter).Compile();
            Assert.Null(rankSelector(new GuideProduct()));
            Assert.Equal(
                (int?)4,
                rankSelector(new GuideProduct { Customer = new GuideCustomer { Rank = 4 } }));

            Assert.True(publicSchema.TryResolveQueryField("unguardedName", out var nameField));
            var nameParameter = Expression.Parameter(typeof(GuideProduct), "order");
            var nameSelector = Expression.Lambda<Func<GuideProduct, string>>(
                nameField.Compose(nameParameter),
                nameParameter).Compile();
            Assert.Throws<NullReferenceException>(() => nameSelector(new GuideProduct()));
            SchemaEntity.GetterCalls = 0;
        }

        [Fact]
        public void ComputedFields_PreserveClrResultTypesAndSemantics()
        {
            var multiplier = 4m;
            var schema = new QuerySchema<GuideProduct>()
                .Field("total", product => product.Price * product.Quantity)
                .Field("nullableTotal", product => product.Discount * product.Quantity)
                .Field("quantity64", product => (long)product.Quantity)
                .Field("priced", product => product.Enabled && product.Price > 0m)
                .Field("capturedTotal", product => product.Price * multiplier)
                .Build();
            var parameter = Expression.Parameter(typeof(GuideProduct), "entity");
            var entity = new GuideProduct
            {
                Price = 2.5m,
                Quantity = 4,
                Discount = 1.5m,
                Enabled = true
            };

            Assert.True(schema.TryResolveQueryField("total", out var total));
            Assert.True(schema.TryResolveQueryField("nullableTotal", out var nullableTotal));
            Assert.True(schema.TryResolveQueryField("quantity64", out var quantity64));
            Assert.True(schema.TryResolveQueryField("priced", out var priced));
            Assert.True(schema.TryResolveQueryField("capturedTotal", out var capturedTotal));
            Assert.Equal(typeof(decimal), total.Compose(parameter).Type);
            Assert.Equal(typeof(decimal?), nullableTotal.Compose(parameter).Type);
            Assert.Equal(ExpressionNullability.Nullable, nullableTotal.Nullability);
            Assert.Equal(typeof(long), quantity64.Compose(parameter).Type);
            Assert.Equal(typeof(bool), priced.Compose(parameter).Type);
            Assert.Equal(typeof(decimal), capturedTotal.Compose(parameter).Type);

            var totalSelector = Expression.Lambda<Func<GuideProduct, decimal>>(
                total.Compose(parameter),
                parameter).Compile();
            var nullableSelector = Expression.Lambda<Func<GuideProduct, decimal?>>(
                nullableTotal.Compose(parameter),
                parameter).Compile();
            var quantity64Selector = Expression.Lambda<Func<GuideProduct, long>>(
                quantity64.Compose(parameter),
                parameter).Compile();
            var pricedSelector = Expression.Lambda<Func<GuideProduct, bool>>(
                priced.Compose(parameter),
                parameter).Compile();
            var capturedSelector = Expression.Lambda<Func<GuideProduct, decimal>>(
                capturedTotal.Compose(parameter),
                parameter).Compile();

            Assert.Equal(entity.Price * entity.Quantity, totalSelector(entity));
            Assert.Equal(entity.Discount * entity.Quantity, nullableSelector(entity));
            Assert.Equal((long)entity.Quantity, quantity64Selector(entity));
            Assert.Equal(entity.Enabled && entity.Price > 0m, pricedSelector(entity));
            Assert.Equal(entity.Price * multiplier, capturedSelector(entity));
            multiplier = 5m;
            Assert.Equal(entity.Price * multiplier, capturedSelector(entity));

            var withoutDiscount = new GuideProduct { Quantity = 2 };
            Assert.Null(nullableSelector(withoutDiscount));
        }

        [Fact]
        public void FromType_ReflectsNestedMembersWithoutReadingValues()
        {
            SchemaEntity.GetterCalls = 0;
            var schema = ExpressionSchema.FromType<SchemaEntity>();

            Assert.Equal(typeof(SchemaEntity), schema.EntityType);
            Assert.True(schema.TryGetProperty(nameof(SchemaEntity.Count), out var countType, out var countNullability, out var countCanQuery));
            Assert.Equal(typeof(int), countType);
            Assert.Equal(ExpressionNullability.NonNullable, countNullability);
            Assert.True(countCanQuery);

            Assert.True(schema.TryGetProperty("Child.Name", out var nameType, out var nameNullability, out var nameCanQuery));
            Assert.Equal(typeof(string), nameType);
            Assert.Equal(ExpressionNullability.Unknown, nameNullability);
            Assert.True(nameCanQuery);

            Assert.True(schema.TryGetProperty(nameof(SchemaEntity.Code), out var fieldType, out _, out _));
            Assert.Equal(typeof(Guid), fieldType);
            Assert.False(schema.TryGetProperty("Item", out _, out _, out _));
            Assert.True(schema.TryGetProperty("Next.Next.Count", out var nestedType, out _, out _));
            Assert.Equal(typeof(int), nestedType);
            Assert.Equal(0, SchemaEntity.GetterCalls);
        }

        [Fact]
        public void FromType_ValidatesOverridesAgainstReflectedClrTypes()
        {
            var schema = ExpressionSchema.FromType<SchemaEntity>(
                properties: new[]
            {
                new ExpressionPropertyDefinition(
                    nameof(SchemaEntity.OptionalCount),
                    typeof(int?),
                    ExpressionNullability.Nullable,
                    canQuery: false)
            });

            Assert.True(schema.TryGetProperty(
                nameof(SchemaEntity.OptionalCount),
                out var propertyType,
                out var nullability,
                out var canQuery));
            Assert.Equal(typeof(int?), propertyType);
            Assert.Equal(ExpressionNullability.Nullable, nullability);
            Assert.False(canQuery);
        }

        [Fact]
        public void FromType_RejectsInvalidMetadata()
        {
            Assert.Throws<ArgumentNullException>(() => ExpressionSchema.FromType((Type)null));
            Assert.Throws<ArgumentException>(() => ExpressionSchema.FromType<SchemaEntity>(properties: new[]
            {
                new ExpressionPropertyDefinition(nameof(SchemaEntity.Count), typeof(long))
            }));
            Assert.Throws<ArgumentException>(() => ExpressionSchema.FromType<SchemaEntity>(properties: new[]
            {
                new ExpressionPropertyDefinition(nameof(SchemaEntity.Count), typeof(int), ExpressionNullability.Nullable)
            }));
            Assert.Throws<ArgumentException>(() => ExpressionSchema.FromType<SchemaEntity>(properties: new[]
            {
                new ExpressionPropertyDefinition(nameof(SchemaEntity.OptionalCount), typeof(int?), ExpressionNullability.NonNullable)
            }));
        }

        [Fact]
        public void FromType_CopiesExactMappingAndAllowlistAndPropagatesDenials()
        {
            var allowed = new[] { "cost", "buyer" };
            var mapping = new Dictionary<string, string>
            {
                ["cost"] = "Count",
                ["buyer"] = "Child.Name"
            };
            var schema = ExpressionSchema.FromType<SchemaEntity>(
                validProperties: allowed,
                propertyMapping: mapping,
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Child", typeof(ChildEntity), canQuery: false)
                });
            allowed[0] = "Code";
            mapping["cost"] = "Code";

            Assert.True(schema.TryMapProperty("COST", out var pricePath));
            Assert.Equal("Count", pricePath);
            Assert.True(schema.IsPropertyQueryable("cost"));
            Assert.False(schema.IsPropertyQueryable("Count"));
            Assert.False(schema.IsPropertyQueryable("buyer"));
            Assert.False(schema.IsPropertyQueryable("buyer.Name"));
            Assert.True(schema.TryMapProperty("buyer.Name", out var nestedPath));
            Assert.Equal("buyer.Name", nestedPath);
        }

        [Fact]
        public void FromType_RejectsDuplicateAndInvalidMappings()
        {
            Assert.Throws<ArgumentException>(() => ExpressionSchema.FromType<SchemaEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["price"] = "Count",
                    ["Price"] = "Code"
                }));
            Assert.Throws<ArgumentException>(() => ExpressionSchema.FromType<SchemaEntity>(
                propertyMapping: new Dictionary<string, string> { ["external"] = "Missing" }));
        }

        public sealed class SchemaEntity
        {
            public static int GetterCalls;
            public Guid Code;
            public int Count { get; set; }
            public int? OptionalCount { get; set; }
            public int this[int index] => index;
            public SchemaEntity Next => RecordRead();
            public ChildEntity Child => RecordReadChild();

            private SchemaEntity RecordRead()
            {
                GetterCalls++;
                throw new InvalidOperationException("Metadata inspection invoked an entity getter.");
            }

            private ChildEntity RecordReadChild()
            {
                GetterCalls++;
                throw new InvalidOperationException("Metadata inspection invoked an entity getter.");
            }
        }

        public sealed class ChildEntity
        {
            public string Name { get; set; }
        }

        public sealed class GuideProduct
        {
            public decimal Price { get; set; }
            public decimal? Discount { get; set; }
            public string Name { get; set; }
            public decimal InternalCost { get; set; }
            public GuideCustomer Customer { get; set; }
            public int Quantity { get; set; }
            public bool Enabled { get; set; }
        }

        public sealed class GuideCustomer
        {
            public string Name { get; set; }
            public int Rank { get; set; }
        }

        public sealed class GuideUser
        {
            public string Department { get; set; }
        }

        public sealed class IndexSelectorEntity
        {
            public int this[int index] => index;
        }

        private static decimal ConsumeSelector(
            Expression<Func<GuideProduct, decimal>> selector) => 0m;

        private static decimal CalculateBusinessValue(decimal price)
        {
            _selectorMethodCalls++;
            return price;
        }

        private static void AssertUnsupportedSelector<TEntity, TResult>(
            string name,
            ParameterExpression parameter,
            Expression body)
        {
            var selector = Expression.Lambda<Func<TEntity, TResult>>(body, parameter);
            var exception = Assert.Throws<ArgumentException>(() =>
                new QuerySchema<TEntity>().Field(name, selector));
            Assert.Contains(name, exception.Message);
        }

        private static bool ContainsInvocation(Expression expression)
        {
            var visitor = new InvocationFinder();
            visitor.Visit(expression);
            return visitor.Found;
        }

        private sealed class InvocationFinder : ExpressionVisitor
        {
            internal bool Found { get; private set; }

            protected override Expression VisitInvocation(InvocationExpression node)
            {
                Found = true;
                return base.VisitInvocation(node);
            }
        }

        private sealed class ParameterCollector : ExpressionVisitor
        {
            internal HashSet<ParameterExpression> Parameters { get; } =
                new HashSet<ParameterExpression>();

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Parameters.Add(node);
                return node;
            }
        }

        private sealed class ConstantDecimalBinder : CallSiteBinder
        {
            public override Expression Bind(
                object[] args,
                ReadOnlyCollection<ParameterExpression> parameters,
                LabelTarget returnLabel) =>
                Expression.Return(returnLabel, Expression.Constant(1m));
        }

        private sealed class UnsupportedExtensionExpression : Expression
        {
            public override ExpressionType NodeType => ExpressionType.Extension;

            public override Type Type => typeof(decimal);

            public override bool CanReduce => false;
        }
    }
}
