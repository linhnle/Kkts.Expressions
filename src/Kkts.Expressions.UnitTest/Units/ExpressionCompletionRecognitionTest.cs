using System;
using Kkts.Expressions.Internal;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class ExpressionCompletionRecognitionTest
    {
        private static readonly ExpressionSchema Schema = ExpressionSchema.FromType<Product>();
        private static readonly ExpressionVariableSchema Variables = new ExpressionVariableSchema(new[]
        {
            new ExpressionVariableDefinition("minimum", typeof(decimal)),
            new ExpressionVariableDefinition("utcnow", typeof(DateTime))
        });

        [Theory]
        [InlineData("|", "Condition", null, null)]
        [InlineData("Price |", "Operator", typeof(decimal), null)]
        [InlineData("Price|", "Operator", typeof(decimal), null)]
        [InlineData("Status = |", "Operand", typeof(string), "=")]
        [InlineData("CreatedAt > $|", "Operand", typeof(DateTime), ">")]
        [InlineData("Customer.|", "Condition", null, null)]
        [InlineData("Price > 1|", "Continuation", typeof(bool), null)]
        [InlineData("not (Price > 1|", "Continuation", typeof(bool), null)]
        [InlineData("Price > 1 and |", "Condition", null, null)]
        [InlineData("Price in |", "MembershipOperand", typeof(decimal), "in")]
        [InlineData("Price IN |", "MembershipOperand", typeof(decimal), "IN")]
        [InlineData("Price NOT   IN |", "MembershipOperand", typeof(decimal), "NOT   IN")]
        [InlineData("Status in ['Active', |]", "MembershipItem", typeof(string), "in")]
        [InlineData("Status in {'Active', |}", "MembershipItem", typeof(string), "in")]
        [InlineData("Status in ('Active', |)", "MembershipItem", typeof(string), "in")]
        [InlineData("Status in ['Active'|", "MembershipSeparator", typeof(string), "in")]
        [InlineData("Price + |", "Operand", typeof(decimal), "+")]
        [InlineData("Status.contains('Ac|ZZ')", "Operand", typeof(string), "contains")]
        [InlineData("Price > (|", "Operand", typeof(decimal), ">")]
        [InlineData("Price > ((|", "Operand", typeof(decimal), ">")]
        [InlineData("Price + (|", "Operand", typeof(decimal), "+")]
        [InlineData("Price NOT   IN [|", "MembershipItem", typeof(decimal), "NOT   IN")]
        public void RecognizesReliableLocalContexts(string marked, string slot, Type receiverType, string op)
        {
            var context = Context(marked);
            Assert.True(context.IsAvailable, marked);
            Assert.Equal(slot, context.Slot.ToString());
            Assert.Equal(receiverType, context.Receiver?.Type);
            Assert.Equal(op, context.Operator);
        }

        [Theory]
        [InlineData("Price > 1 and Unknown = )", 9, "Continuation")]
        [InlineData("Price = ) and Sta", 17, "Condition")]
        public void IndependentErrorsUseExistingRecoveryWithoutPoisoningLocalContext(
            string text, int position, string slot)
        {
            var context = ExpressionCompletionContext.Recognize(
                text, position, Schema, Variables, Interpreter.AnalyzeExpression(text));
            Assert.True(context.IsAvailable);
            Assert.Equal(slot, context.Slot.ToString());
        }

        [Theory]
        [InlineData("Unknown |")]
        [InlineData("Unknown = |")]
        [InlineData("Price = ) an|d Status")]
        [InlineData("'random| text'")]
        [InlineData("Status = 'a\\|'b'")]
        [InlineData("Price | > 1")]
        [InlineData("Price > | 1")]
        [InlineData("Sta|Tus 'adjacent operand'")]
        [InlineData("Price Bad|Field")]
        public void SuppressesUndecidableOrConflictingLocalContexts(string marked) =>
            Assert.False(Context(marked).IsAvailable, marked);

        [Fact]
        public void FieldEditKeepsTheSurvivingComparisonOperandAsATypeConstraint()
        {
            var context = Context("Sta|Tus = 'Active' and Price > 1");
            Assert.Equal(ExpressionCompletionSlot.Condition, context.Slot);
            Assert.Equal("=", context.SuffixOperator);
            Assert.Equal(typeof(string), context.SuffixOperand.Type);
            Assert.Equal(0, context.Edit.Start);
            Assert.Equal(6, context.Edit.Length);
        }

        [Fact]
        public void OperatorEditKeepsTheSurvivingRightHandOperand()
        {
            var context = Context("Price >|= 1 and Status = 'Active'");
            Assert.Equal(ExpressionCompletionSlot.Operator, context.Slot);
            Assert.Equal(typeof(int), context.SuffixOperand.Type);
            Assert.Equal(6, context.Edit.Start);
            Assert.Equal(2, context.Edit.Length);
        }

        [Fact]
        public void PartialCompoundOperatorDoesNotBecomeUnaryNot()
        {
            var context = Context("Price not i|");
            Assert.Equal(ExpressionCompletionSlot.Operator, context.Slot);
            Assert.Equal("not i", context.Edit.Prefix);
            Assert.Equal(6, context.Edit.Start);
            Assert.Equal(5, context.Edit.Length);
        }

        [Fact]
        public void PartialWordOperatorUsesParserExpectationsRatherThanAnIdentifierGuess()
        {
            var context = Context("Price i|");
            Assert.Equal(ExpressionCompletionSlot.Operator, context.Slot);
            Assert.Equal(typeof(decimal), context.Receiver.Type);
            Assert.Equal("i", context.CandidatePrefix);
        }

        [Theory]
        [InlineData("Price i|n [1, 2]")]
        [InlineData("Price i|n {1, 2}")]
        [InlineData("Price i|n (1, 2)")]
        public void MembershipOperatorEditBindsTheSurvivingList(string marked)
        {
            var context = Context(marked);
            Assert.Equal(ExpressionCompletionSlot.Operator, context.Slot);
            Assert.True(context.SuffixOperand.IsList);
            Assert.Equal(2, context.SuffixOperand.ElementNodes.Count);
        }

        [Fact]
        public void QuotedValueKeepsTheFullReplacementAndDecodedPrefix()
        {
            var context = Context("Status = 'Ac|ZZ' and Price > 1");
            Assert.Equal(ExpressionCompletionSlot.Operand, context.Slot);
            Assert.Equal("Ac", context.Edit.Prefix);
            Assert.Equal(9, context.Edit.Start);
            Assert.Equal(6, context.Edit.Length);
        }

        [Fact]
        public void PartialFunctionPathUsesADeclaredTypedReceiverAndExistingKeywords()
        {
            var context = Context("Status.con|");
            Assert.Equal(ExpressionCompletionSlot.FunctionOperator, context.Slot);
            Assert.Equal(typeof(string), context.Receiver.Type);
            Assert.Equal("con", context.CandidatePrefix);
            Assert.Equal("Status.", context.InsertionPrefix);
            Assert.Equal(0, context.Edit.Start);
            Assert.Equal(10, context.Edit.Length);
            Assert.False(context.AllowsPathFields);
        }

        [Fact]
        public void EmptyLegacyStringPathCanStillOfferExposedFieldsAlongsideFunctions()
        {
            var context = Context("Status.|");
            Assert.Equal(ExpressionCompletionSlot.FunctionOperator, context.Slot);
            Assert.True(context.AllowsPathFields);
            Assert.Equal(string.Empty, context.CandidatePrefix);
        }

        [Fact]
        public void ExistingFunctionOperatorPreservesItsReceiverAndSuffix()
        {
            var context = Context("Status.cont|ains('a')");
            Assert.Equal(ExpressionCompletionSlot.FunctionOperator, context.Slot);
            Assert.Equal(typeof(string), context.Receiver.Type);
            Assert.Equal(typeof(string), context.SuffixOperand.Type);
            Assert.Equal(string.Empty, context.InsertionPrefix);
            Assert.Equal(7, context.Edit.Start);
            Assert.Equal(8, context.Edit.Length);
        }

        [Fact]
        public void DeniedReceiversDoNotProduceTypedCompletionContexts()
        {
            var schema = ExpressionSchema.FromType<Product>(properties: new[]
            {
                new ExpressionPropertyDefinition("Price", typeof(decimal), canQuery: false)
            });
            const string text = "Price > ";
            var context = ExpressionCompletionContext.Recognize(
                text, text.Length, schema, Variables, Interpreter.AnalyzeExpression(text));
            Assert.False(context.IsAvailable);
            Assert.Null(context.Receiver);
            Assert.Null(context.SuffixOperand);
        }

        [Fact]
        public void PublicComputedReceiverUsesItsResultTypeWithoutExecutingItsSelector()
        {
            var schema = new QuerySchema<ThrowingProduct>()
                .Field("total", product => product.Hidden)
                .Build();
            const string text = "total > ";
            var context = ExpressionCompletionContext.Recognize(
                text, text.Length, schema, Variables, Interpreter.AnalyzeExpression(text));
            Assert.Equal(ExpressionCompletionSlot.Operand, context.Slot);
            Assert.Equal(typeof(decimal), context.Receiver.Type);
            Assert.Equal(new[] { "total" }, context.Receiver.EntityPaths);
            Assert.Equal(0, ThrowingProduct.Calls);
        }

        private static ExpressionCompletionContext Context(string marked)
        {
            var position = marked.IndexOf('|');
            var text = marked.Remove(position, 1);
            return ExpressionCompletionContext.Recognize(text, position, Schema, Variables,
                Interpreter.AnalyzeExpression(text));
        }

        public sealed class Product
        {
            public decimal Price { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public Customer Customer { get; set; }
        }

        public sealed class Customer
        {
            public string Name { get; set; }
        }

        public sealed class ThrowingProduct
        {
            public static int Calls;
            public decimal Hidden
            {
                get
                {
                    Calls++;
                    throw new InvalidOperationException("Completion executed a getter.");
                }
            }
        }
    }
}
