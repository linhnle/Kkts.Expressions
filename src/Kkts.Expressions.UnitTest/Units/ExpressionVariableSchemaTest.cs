using System;
using System.Collections.Generic;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionVariableSchemaTest
    {
        [Fact]
        public void FromType_ReflectsNestedMembersWithoutInvokingGetters()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(UserMetadata)),
                new ExpressionVariableDefinition("amount", typeof(decimal?)),
                new ExpressionVariableDefinition("prices", typeof(List<decimal>))
            });

            Assert.True(variables.TryGetVariable(
                "user.DEPARTMENT",
                out var departmentType,
                out var departmentNullability,
                out var departmentElementType,
                out var rootDeclared));
            Assert.Equal(typeof(string), departmentType);
            Assert.Equal(ExpressionNullability.Unknown, departmentNullability);
            Assert.Null(departmentElementType);
            Assert.True(rootDeclared);
            Assert.Equal(0, UserMetadata.GetterCalls);

            Assert.True(variables.TryGetVariable("amount", out var amountType, out var amountNullability, out _, out _));
            Assert.Equal(typeof(decimal?), amountType);
            Assert.Equal(ExpressionNullability.Nullable, amountNullability);

            Assert.True(variables.TryGetVariable("prices", out _, out _, out var elementType, out _));
            Assert.Equal(typeof(decimal), elementType);
        }

        [Fact]
        public void ExplicitAndDottedDeclarationsRemainDistinct()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition(
                    "account",
                    typeof(object),
                    members: new[]
                    {
                        new ExpressionVariableDefinition("department", typeof(string))
                    }),
                new ExpressionVariableDefinition("user.department", typeof(string))
            });

            Assert.True(variables.TryGetVariable("account.department", out var memberType, out _, out _, out var memberRoot));
            Assert.Equal(typeof(string), memberType);
            Assert.True(memberRoot);

            Assert.True(variables.TryGetVariable("user.department", out var directType, out _, out _, out var directRoot));
            Assert.Equal(typeof(string), directType);
            Assert.False(directRoot);
            Assert.False(variables.TryGetVariable("user.unknown", out _, out _, out _, out var unknownRoot));
            Assert.False(unknownRoot);
        }

        [Fact]
        public void UnknownMemberUnderDeclaredRootIsDistinguishedFromUnknownRoot()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType("user", typeof(UserMetadata))
            });

            Assert.False(variables.TryGetVariable("user.missing", out _, out _, out _, out var declaredRoot));
            Assert.True(declaredRoot);
            Assert.False(variables.TryGetVariable("missing.value", out _, out _, out _, out var missingRoot));
            Assert.False(missingRoot);
        }

        [Fact]
        public void DeclarationsRejectInvalidOrAmbiguousNames()
        {
            Assert.Throws<ArgumentException>(() => new ExpressionVariableDefinition("$user", typeof(UserMetadata)));
            Assert.Throws<ArgumentException>(() => new ExpressionVariableDefinition("user..department", typeof(string)));
            Assert.Throws<ArgumentException>(() => new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("user", typeof(UserMetadata)),
                new ExpressionVariableDefinition("user", typeof(UserMetadata))
            }));
            Assert.Throws<ArgumentException>(() => new ExpressionVariableDefinition(
                "user",
                typeof(UserMetadata),
                members: new[]
                {
                    new ExpressionVariableDefinition("department", typeof(string)),
                    new ExpressionVariableDefinition("DEPARTMENT", typeof(string))
                }));
        }

        public sealed class UserMetadata
        {
            public static int GetterCalls;

            public string Department
            {
                get
                {
                    GetterCalls++;
                    throw new InvalidOperationException("Variable metadata invoked a getter.");
                }
            }
        }
    }
}
