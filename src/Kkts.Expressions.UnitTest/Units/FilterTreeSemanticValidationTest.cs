using System;
using System.Collections.Generic;
using Kkts.Expressions;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreeSemanticValidationTest
    {
        [Fact]
        public void ValidatesTypedScalarFamiliesAndMappedFields()
        {
            var schema = CreateSchema();
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "==", FilterValue.Number("1")),
                FilterNode.Condition("NullableId", "=", FilterValue.Null),
                FilterNode.Condition("Active", "=", FilterValue.Boolean(true)),
                FilterNode.Condition("Active", "=", FilterValue.String("true")),
                FilterNode.Condition("Name", "=", FilterValue.String("Alice")),
                FilterNode.Condition("Grade", "=", FilterValue.String("A")),
                FilterNode.Condition("Cost", ">=", FilterValue.Number("1234567890.1234567890123456789")),
                FilterNode.Condition("Big", "=", FilterValue.Number("18446744073709551615")),
                FilterNode.Condition("SignedByte", "=", FilterValue.Number("-128")),
                FilterNode.Condition("Byte", "=", FilterValue.Number("255")),
                FilterNode.Condition("Short", "=", FilterValue.Number("-32768")),
                FilterNode.Condition("UShort", "=", FilterValue.Number("65535")),
                FilterNode.Condition("UInt", "=", FilterValue.Number("4294967295")),
                FilterNode.Condition("Long", "=", FilterValue.Number("-9223372036854775808")),
                FilterNode.Condition("ULong", "=", FilterValue.Number("18446744073709551615")),
                FilterNode.Condition("Single", "=", FilterValue.Number("1.25")),
                FilterNode.Condition("Double", "=", FilterValue.Number("1e200")),
                FilterNode.Condition("Created", "=", FilterValue.String("2026-10-09T00:00:00Z")),
                FilterNode.Condition("CreatedOffset", "=", FilterValue.String("2026-10-09T00:00:00+00:00")),
                FilterNode.Condition("Duration", "=", FilterValue.String("-1.02:03:04.1234567")),
                FilterNode.Condition("Key", "=", FilterValue.String("00000000-0000-0000-0000-000000000001")),
                FilterNode.Condition("publicStatus", "=", FilterValue.String("Ready")),
                FilterNode.Condition("publicStatus", "=", FilterValue.Number("0")),
                FilterNode.Condition("Name", "@", FilterValue.String("li")),
                FilterNode.Condition("NullableId", "in", FilterValue.Collection(new[]
                {
                    FilterValue.Null,
                    FilterValue.Number("7"),
                    FilterValue.Number("7")
                }))
            });

            var result = FilterTreeSemanticValidator.Validate(tree, schema);

            Assert.True(result.Succeeded);
            Assert.Same(tree, result.Result);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void RejectsIncompatibleTypedValuesAndContextDependentTemporalLiterals()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Null),
                FilterNode.Condition("Id", "=", FilterValue.Number("2147483648")),
                FilterNode.Condition("Name", "=", FilterValue.Boolean(true)),
                FilterNode.Condition("Name", "=", FilterValue.Null),
                FilterNode.Condition("Cost", "=", FilterValue.Number("0.00000000000000000000000000001")),
                FilterNode.Condition("Single", "=", FilterValue.Number("1e100")),
                FilterNode.Condition("Double", "=", FilterValue.Number("1e309")),
                FilterNode.Condition("Grade", "=", FilterValue.String("AB")),
                FilterNode.Condition("CreatedOffset", "=", FilterValue.String("2026-10-09T00:00:00"))
            });

            var result = FilterTreeSemanticValidator.Validate(tree, CreateSchema());

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal(
                new[]
                {
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "incompatible-operand",
                    "context-dependent-conversion"
                },
                System.Linq.Enumerable.Select(result.Diagnostics, diagnostic => diagnostic.Code));
            Assert.Equal(
                new[]
                {
                    "/and/0/value", "/and/1/value", "/and/2/value", "/and/3/value",
                    "/and/4/value", "/and/5/value", "/and/6/value", "/and/7/value", "/and/8/value"
                },
                System.Linq.Enumerable.Select(result.Diagnostics, diagnostic => diagnostic.InputPath));
            Assert.All(result.Diagnostics, diagnostic =>
            {
                Assert.Equal(0, diagnostic.Start);
                Assert.Equal(0, diagnostic.Length);
            });
        }

        [Fact]
        public void ResolvesDeclaredScalarAndCollectionMetadataWithoutExecutingGetters()
        {
            Probe.GetterCalls = 0;
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("request", typeof(Probe)),
                new ExpressionVariableDefinition("rawId", typeof(string))
            });
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Variable("request.Id")),
                FilterNode.Condition("Id", "in", FilterValue.Variable("request.Ids")),
                FilterNode.Condition("Id", "=", FilterValue.Variable("rawId")),
                FilterNode.Condition("Id", "in", FilterValue.Collection(new[]
                {
                    FilterValue.Variable("request.Id")
                }))
            });

            var result = FilterTreeSemanticValidator.Validate(tree, CreateSchema(), variables);

            Assert.True(result.Succeeded);
            Assert.Equal(0, Probe.GetterCalls);
        }

        [Fact]
        public void ReportsUndeclaredVariableAndRejectsCollectionExpansionSlots()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("ids", typeof(int[]))
            });
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Variable("missing")),
                FilterNode.Condition("Id", "in", FilterValue.Collection(new[]
                {
                    FilterValue.Variable("ids")
                }))
            });

            var result = FilterTreeSemanticValidator.Validate(tree, CreateSchema(), variables);

            Assert.False(result.Succeeded);
            Assert.Null(result.Result);
            Assert.Equal(
                new[] { "undeclared-variable", "incompatible-operand" },
                System.Linq.Enumerable.Select(result.Diagnostics, diagnostic => diagnostic.Code));
            Assert.Equal(new[] { "/and/0/value", "/and/1/value/0" },
                System.Linq.Enumerable.Select(result.Diagnostics, diagnostic => diagnostic.InputPath));
        }

        [Fact]
        public void RestrictedAndUnknownFieldsUseExternalFieldPointersWithoutCanonicalDetails()
        {
            var tree = FilterNode.And(new[]
            {
                FilterNode.Condition("SecretAlias", "=", FilterValue.Number("1")),
                FilterNode.Condition("Missing", "=", FilterValue.Number("1"))
            });
            var schema = ExpressionSchema.FromType<ValidationEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["SecretAlias"] = "Secret"
                },
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Secret", typeof(int), canQuery: false)
                });

            var result = FilterTreeSemanticValidator.Validate(tree, schema);

            Assert.False(result.Succeeded);
            Assert.Equal("property-not-queryable", result.Diagnostics[0].Code);
            Assert.Equal("/and/0/field", result.Diagnostics[0].InputPath);
            Assert.DoesNotContain("Secret", result.Diagnostics[0].Message);
            Assert.Equal("unknown-property", result.Diagnostics[1].Code);
            Assert.Equal("/and/1/field", result.Diagnostics[1].InputPath);
        }

        private static ExpressionSchema CreateSchema() =>
            ExpressionSchema.FromType<ValidationEntity>(
                propertyMapping: new Dictionary<string, string>
                {
                    ["publicStatus"] = "Status"
                },
                properties: new[]
                {
                    new ExpressionPropertyDefinition(
                        "Name",
                        typeof(string),
                        nullability: ExpressionNullability.NonNullable)
                });

        private sealed class ValidationEntity
        {
            public int Id { get; set; }
            public int? NullableId { get; set; }
            public bool Active { get; set; }
            public string Name { get; set; }
            public char Grade { get; set; }
            public decimal Cost { get; set; }
            public ulong Big { get; set; }
            public sbyte SignedByte { get; set; }
            public byte Byte { get; set; }
            public short Short { get; set; }
            public ushort UShort { get; set; }
            public uint UInt { get; set; }
            public long Long { get; set; }
            public ulong ULong { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
            public DateTime Created { get; set; }
            public DateTimeOffset CreatedOffset { get; set; }
            public TimeSpan Duration { get; set; }
            public Guid Key { get; set; }
            public TestStatus Status { get; set; }
            public int Secret { get; set; }
        }

        private enum TestStatus
        {
            Ready
        }

        private sealed class Probe
        {
            public static int GetterCalls;

            public int Id
            {
                get
                {
                    GetterCalls++;
                    throw new InvalidOperationException("Metadata validation must not read values.");
                }
            }

            public IEnumerable<int> Ids
            {
                get
                {
                    GetterCalls++;
                    throw new InvalidOperationException("Metadata validation must not read values.");
                }
            }
        }
    }
}
