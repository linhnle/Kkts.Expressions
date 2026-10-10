using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class FilterTreeMetadataPurityTest
    {
        [Fact]
        public async Task ValidMalformedAndDeniedTreeValidationExecutesNoApplicationCode()
        {
            var sequence = new CountingEnumerable();
            var resolver = new InstrumentedResolver(sequence);
            resolver.TryAdd("ids", sequence);
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("request", typeof(InstrumentedResolver)),
                new ExpressionVariableDefinition("ids", typeof(IEnumerable<int>))
            });
            var schema = ExpressionSchema.FromType<InstrumentedEntity>(
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Denied", typeof(int), canQuery: false)
                });
            var valid = FilterNode.And(new[]
            {
                FilterNode.Condition("Id", "=", FilterValue.Number("1")),
                FilterNode.Condition("Id", "=", FilterValue.Variable("request.Id")),
                FilterNode.Condition("Id", "in", FilterValue.Variable("ids")),
                FilterNode.Condition("Id", "in", FilterValue.Variable("request.Values"))
            });

            var validResult = FilterTreeSemanticValidator.Validate(valid, schema, variables);
            var malformedJson = FilterTreeJson.TryDeserialize("{\"and\":[]}");
            var denied = FilterTreeSemanticValidator.Validate(
                FilterNode.Condition("Denied", "=", FilterValue.Number("1")),
                schema,
                variables);
            var unsupportedConversion = FilterTreeSemanticValidator.Validate(
                FilterNode.Condition("Custom", "=", FilterValue.String("value")),
                schema,
                variables);
            Assert.Throws<NotSupportedException>(() => FilterValue.FromObject(new OperatorProbe()));

            Assert.True(validResult.Succeeded);
            Assert.False(malformedJson.Succeeded);
            Assert.False(denied.Succeeded);
            Assert.False(unsupportedConversion.Succeeded);
            Assert.Equal(0, resolver.InitializationCalls);
            Assert.Equal(0, resolver.ResolutionCalls);
            Assert.Equal(0, InstrumentedResolver.GetterCalls);
            Assert.Equal(0, InstrumentedEntity.GetterCalls);
            Assert.Equal(0, sequence.EnumerationCalls);
            Assert.Equal(0, OperatorProbe.OperatorCalls);
            Assert.Equal(0, OperatorProbe.ToStringCalls);

            await resolver.InitializeVariablesAsync(null);
            Assert.Equal(1, resolver.InitializationCalls);
            resolver.TryResolve("not-cached", out _);
            Assert.Equal(1, resolver.ResolutionCalls);
            _ = resolver.Id;
            Assert.Equal(1, InstrumentedResolver.GetterCalls);
            _ = new InstrumentedEntity().Id;
            Assert.Equal(1, InstrumentedEntity.GetterCalls);
            foreach (var unused in sequence) break;
            Assert.Equal(1, sequence.EnumerationCalls);
            _ = new OperatorProbe() == new OperatorProbe();
            Assert.Equal(1, OperatorProbe.OperatorCalls);
            _ = new OperatorProbe().ToString();
            Assert.Equal(1, OperatorProbe.ToStringCalls);
        }

        private sealed class InstrumentedEntity
        {
            public static int GetterCalls;

            public int Id
            {
                get
                {
                    GetterCalls++;
                    return 1;
                }
            }

            public int Denied { get; set; }
            public OperatorProbe Custom { get; set; }
        }

        private sealed class InstrumentedResolver : VariableResolver
        {
            internal InstrumentedResolver(CountingEnumerable values)
            {
                _values = values;
            }

            public static int GetterCalls;
            public int InitializationCalls { get; private set; }
            public int ResolutionCalls { get; private set; }

            public int Id
            {
                get
                {
                    GetterCalls++;
                    return 1;
                }
            }

            public IEnumerable<int> Values
            {
                get
                {
                    GetterCalls++;
                    return (IEnumerable<int>)_values;
                }
            }

            private readonly CountingEnumerable _values;

            public override Task InitializeVariablesAsync(object state)
            {
                InitializationCalls++;
                return Task.CompletedTask;
            }

            protected override Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
            {
                ResolutionCalls++;
                return Task.FromResult(new VariableInfo { Name = name });
            }
        }

        private sealed class CountingEnumerable : IEnumerable<int>
        {
            public int EnumerationCalls { get; private set; }

            public IEnumerator<int> GetEnumerator()
            {
                EnumerationCalls++;
                return new List<int> { 1, 2 }.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class OperatorProbe
        {
            public static int OperatorCalls;
            public static int ToStringCalls;

            public static bool operator ==(OperatorProbe left, OperatorProbe right)
            {
                OperatorCalls++;
                return ReferenceEquals(left, right);
            }

            public static bool operator !=(OperatorProbe left, OperatorProbe right) => !(left == right);
            public override bool Equals(object value) => ReferenceEquals(this, value);
            public override int GetHashCode() => 0;

            public override string ToString()
            {
                ToStringCalls++;
                return "probe";
            }
        }
    }
}
