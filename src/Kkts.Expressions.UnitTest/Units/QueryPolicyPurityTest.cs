using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class QueryPolicyPurityTest
    {
        [Fact]
        public async Task AnalyzeExpression_DoesNotExecuteGettersResolversOrEnumerateDeclaredCollections()
        {
            ProbeEntity.GetterCalls = 0;
            ProbeNumber.OperatorCalls = 0;
            ThrowingSequence.EnumerationCalls = 0;
            var resolver = new CountingResolver();
            var schema = ExpressionSchema.FromType<ProbeEntity>(
                properties: new[]
                {
                    new ExpressionPropertyDefinition("Restricted", typeof(int), canQuery: false)
                });
            var context = new ExpressionQueryContext(
                schema,
                new QueryPolicy(maxParenthesisDepth: 2, maxInItems: 1));
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("items", typeof(ThrowingSequence)),
                new ExpressionVariableDefinition("number", typeof(ProbeNumber)),
                ExpressionVariableDefinition.FromType("entity", typeof(ProbeEntity))
            });

            Assert.True(context.AnalyzeExpression("Value = 1").IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Value in $items", variables).IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("$entity.Value = 1", variables).IsSemanticallyValid);
            Assert.True(context.AnalyzeExpression("Number = $number", variables).IsSemanticallyValid);
            Assert.False(context.AnalyzeExpression("Restricted = 1").IsSemanticallyValid);
            Assert.False(context.AnalyzeExpression("Value ==").IsComplete);
            Assert.False(context.AnalyzeExpression("Value =").IsComplete);
            Assert.True(context.AnalyzeExpression("(((Value = 1)))").IsTruncated);
            Assert.Equal(0, ProbeEntity.GetterCalls);
            Assert.Equal(0, ProbeNumber.OperatorCalls);
            Assert.Equal(0, ThrowingSequence.EnumerationCalls);
            Assert.Equal(0, resolver.InitializeCalls);
            Assert.Equal(0, resolver.ResolveCalls);

            var entity = new ProbeEntity();
            _ = entity.Value;
            Assert.Equal(1, ProbeEntity.GetterCalls);
            _ = default(ProbeNumber) == default(ProbeNumber);
            Assert.Equal(1, ProbeNumber.OperatorCalls);
            Assert.Throws<InvalidOperationException>(() => new ThrowingSequence().GetEnumerator());
            Assert.Equal(1, ThrowingSequence.EnumerationCalls);
            await resolver.InitializeVariablesAsync(null);
            Assert.True(resolver.TryResolve("external", out _));
            Assert.Equal(1, resolver.InitializeCalls);
            Assert.Equal(1, resolver.ResolveCalls);
        }

        private sealed class ProbeEntity
        {
            internal static int GetterCalls;

            public int Value
            {
                get
                {
                    ++GetterCalls;
                    return 1;
                }
            }

            public int Restricted
            {
                get
                {
                    ++GetterCalls;
                    return 2;
                }
            }

            public ProbeNumber Number
            {
                get
                {
                    ++GetterCalls;
                    return default;
                }
            }
        }

        private struct ProbeNumber
        {
            internal static int OperatorCalls;

            public static bool operator ==(ProbeNumber left, ProbeNumber right)
            {
                ++OperatorCalls;
                return true;
            }

            public static bool operator !=(ProbeNumber left, ProbeNumber right)
            {
                ++OperatorCalls;
                return false;
            }

            public override bool Equals(object obj) => obj is ProbeNumber;

            public override int GetHashCode() => 0;
        }

        private sealed class ThrowingSequence : IEnumerable<int>
        {
            internal static int EnumerationCalls;

            public IEnumerator<int> GetEnumerator()
            {
                ++EnumerationCalls;
                throw new InvalidOperationException("Metadata analysis must not enumerate values.");
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private sealed class CountingResolver : VariableResolver
        {
            internal int InitializeCalls;
            internal int ResolveCalls;

            public override Task InitializeVariablesAsync(object state)
            {
                ++InitializeCalls;
                return Task.CompletedTask;
            }

            protected override Task<VariableInfo> TryResolveCore(
                string name,
                CancellationToken cancellationToken)
            {
                ++ResolveCalls;
                return Task.FromResult(new VariableInfo
                {
                    Name = name,
                    Resolved = true,
                    Value = 1
                });
            }
        }
    }
}
