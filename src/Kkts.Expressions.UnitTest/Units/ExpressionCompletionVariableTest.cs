using System;
using System.Linq;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionVariableTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();

        [Fact]
        public void TemporalVariablesAreDeclaredAndTypeCompatible()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("utcnow", typeof(DateTime)),
                new ExpressionVariableDefinition("startOfMonth", typeof(DateTime)),
                new ExpressionVariableDefinition("minimum", typeof(decimal))
            });
            var result = Complete("CreatedAt > $|", variables);
            Assert.Equal(new[] { "$startOfMonth", "$utcnow" }, result.Items.Select(item => item.InsertionText));
            Assert.All(result.Items, item =>
            {
                Assert.Equal(12, item.Start);
                Assert.Equal(1, item.Length);
                Assert.Equal(typeof(DateTime), item.TypeInfo.ClrType);
                Assert.Contains("Declared, type-compatible", item.Description);
                Assert.Contains("deferred", item.Description);
            });
            Assert.Empty(Complete("CreatedAt > $|", null).Items);
        }

        [Fact]
        public void ExplicitChildrenCanCompleteAnIncompatibleParentWithoutInventingTrees()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("user", typeof(object), members: new[]
                {
                    new ExpressionVariableDefinition("Date", typeof(DateTime)),
                    new ExpressionVariableDefinition("Name", typeof(string))
                }),
                new ExpressionVariableDefinition("opaque", typeof(User)),
                new ExpressionVariableDefinition("direct.Date", typeof(DateTime))
            });
            Assert.Equal("$user.Date", Assert.Single(Complete("CreatedAt > $u|", variables).Items).InsertionText);
            Assert.Equal("$user.Date", Assert.Single(Complete("CreatedAt > $user.da|ZZ", variables).Items).InsertionText);
            Assert.Empty(Complete("CreatedAt > $opaque.|", variables).Items);
            Assert.Equal("$direct.Date", Assert.Single(Complete("CreatedAt > $direct.|", variables).Items).InsertionText);
        }

        [Fact]
        public void RootsAreCaseSensitiveMembersAreNotAndExactDottedDeclarationsWin()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType<User>("User"),
                new ExpressionVariableDefinition("User.Date", typeof(string))
            });
            Assert.Empty(Complete("CreatedAt > $user.|", variables).Items);
            Assert.Empty(Complete("CreatedAt > $User.Date|", variables).Items);
            Assert.Empty(Complete("CreatedAt > $User.da|", variables).Items);
            // The differently cased member path binds reflected metadata, not the exact dotted override.
            Assert.True(Interpreter.AnalyzeExpression("CreatedAt > $User.date", typeof(Product), Schema, variables).IsSemanticallyValid);
        }

        [Fact]
        public void MembershipScalarAndWholeCollectionsStayDistinct()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                new ExpressionVariableDefinition("ids", typeof(int[])),
                new ExpressionVariableDefinition("names", typeof(string[])),
                new ExpressionVariableDefinition("minimum", typeof(int)),
                new ExpressionVariableDefinition("text", typeof(string)),
                new ExpressionVariableDefinition("doubleThreshold", typeof(double))
            });
            Assert.Equal("$ids", Assert.Single(Complete("Id in $|", variables).Items).InsertionText);
            Assert.Equal(new[] { "$doubleThreshold", "$minimum", "$text" },
                Complete("Id in [$|]", variables).Items.Select(item => item.InsertionText));
            Assert.DoesNotContain(Complete("Id > $|", variables).Items, item => item.Label == "text");
            Assert.Contains(Complete("Price > $|", variables).Items, item => item.Label == "doubleThreshold");
        }

        [Fact]
        public void ReflectedIntermediateSegmentsUseBindingValidCanonicalCasing()
        {
            var variables = new ExpressionVariableSchema(new[]
            {
                ExpressionVariableDefinition.FromType<UserContainer>("user")
            });
            Assert.Equal("$user.Profile.Date",
                Assert.Single(Complete("CreatedAt > $user.profile.da|", variables).Items).InsertionText);
        }

        private static ExpressionCompletionResult Complete(string marked, ExpressionVariableSchema variables)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            var syntax = Interpreter.AnalyzeExpression(text);
            var slot = ExpressionCompletionContext.Recognize(text, position, Schema, variables, syntax);
            var candidates = new ExpressionCompletionCandidates(text, Schema, variables, syntax, slot);
            candidates.AddVariables();
            return candidates.Shape();
        }

        public class Product
        {
            public DateTime CreatedAt { get; set; }
            public int Id { get; set; }
            public decimal Price { get; set; }
        }

        public class User
        {
            public DateTime Date => throw new InvalidOperationException("Getter must not run.");
            public string Name => throw new InvalidOperationException("Getter must not run.");
        }

        public class UserContainer
        {
            public User Profile => throw new InvalidOperationException("Getter must not run.");
        }
    }
}
