using System;
using System.Collections.Generic;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionSchemaTest
    {
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
        public void FromType_ReflectsNestedMembersWithoutReadingValues()
        {
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
        }

        public sealed class GuideCustomer
        {
            public string Name { get; set; }
        }

        public sealed class GuideUser
        {
            public string Department { get; set; }
        }
    }
}
