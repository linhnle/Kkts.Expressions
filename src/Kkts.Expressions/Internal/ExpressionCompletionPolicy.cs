using System;
using System.Collections.Generic;
using System.Linq;

namespace Kkts.Expressions.Internal
{
    internal sealed class ExpressionCompletionPolicy
    {
        private readonly string _source;
        private readonly ExpressionCompletionContext _context;
        private readonly QueryPolicy _policy;
        private readonly long _conditions;
        private readonly int _replacedConditions;
        private readonly int _implicitReceiverConditions;
        private readonly bool _conditionReserved;
        private readonly int _membershipItems;
        private readonly bool _replacesMembershipItem;
        private readonly int _suffixDepth;
        private readonly int _suffixScopes;

        internal ExpressionCompletionPolicy(string source, IReadOnlyList<ExpressionToken> tokens,
            ExpressionCompletionContext context, QueryPolicyExecution execution, IReadOnlyList<ExpressionToken> bareConditions)
        {
            _source = source;
            _context = context;
            _policy = execution.Policy;
            _conditions = execution.ConditionCount.Value;
            var edit = context.Edit;
            foreach (var token in bareConditions)
                if (token.Start < edit.Start + edit.Length && token.Start + token.Length > edit.Start)
                    ++_replacedConditions;
                else if (context.Slot == ExpressionCompletionSlot.Operator && context.Receiver != null &&
                    token.Start == context.Receiver.Start)
                    ++_implicitReceiverConditions;
            var depth = 0;
            var scopes = 0;
            var listStart = -1;
            var activeListItems = 0;
            var replacesListItem = false;
            for (var index = 0; index < tokens.Count; ++index)
            {
                var token = tokens[index];
                var text = source.Substring(token.Start, token.Length);
                if (token.Start >= edit.Start)
                {
                    _suffixDepth = Math.Max(_suffixDepth, depth);
                    _suffixScopes = Math.Max(_suffixScopes, scopes);
                }
                if (token.Kind == ExpressionTokenKind.Operator && ExpressionGrammar.IsComparison(text) &&
                    token.Start >= edit.Start && token.Start + token.Length <= edit.Start + edit.Length)
                    ++_replacedConditions;
                if (token.Kind == ExpressionTokenKind.Punctuation)
                {
                    if (text == "(") ++depth;
                    else if (text == ")") depth = Math.Max(0, depth - 1);
                    if (text == "(" || text == "[" || text == "{") ++scopes;
                    else if (text == ")" || text == "]" || text == "}") scopes = Math.Max(0, scopes - 1);
                    if (token.Start >= edit.Start)
                    {
                        _suffixDepth = Math.Max(_suffixDepth, depth);
                        _suffixScopes = Math.Max(_suffixScopes, scopes);
                    }
                    if (index > 0 && ExpressionGrammar.IsListStart(text[0]) &&
                        tokens[index - 1].Kind == ExpressionTokenKind.Operator &&
                        Interpreter.IsMembership(ExpressionGrammar.NormalizeOperator(
                            source.Substring(tokens[index - 1].Start, tokens[index - 1].Length))))
                    {
                        listStart = token.Start;
                        activeListItems = 0;
                        replacesListItem = false;
                    }
                    else if (listStart >= 0 && text != ",")
                    {
                        if (listStart < edit.Position && token.Start >= edit.Position)
                        {
                            _membershipItems = activeListItems;
                            _replacesMembershipItem = replacesListItem;
                        }
                        listStart = -1;
                    }
                    continue;
                }
                if (listStart >= 0)
                {
                    ++activeListItems;
                    if (token.Start < edit.Start + edit.Length && token.Start + token.Length > edit.Start)
                        replacesListItem = true;
                }
            }
            _suffixDepth = Math.Max(_suffixDepth, depth);
            _suffixScopes = Math.Max(_suffixScopes, scopes);
            if (listStart >= 0 && listStart < edit.Position)
            {
                _membershipItems = activeListItems;
                _replacesMembershipItem = replacesListItem;
            }
            _conditionReserved = context.Receiver != null || context.SuffixOperator != null;
        }

        internal bool Allows(ExpressionCompletionKind kind, string insertion)
        {
            var editedLength = (long)_source.Length - _context.Edit.Length + insertion.Length;
            if (editedLength > ExpressionCompletionBudget.MaximumSourceLength ||
                _policy.MaxExpressionLength.HasValue && editedLength > _policy.MaxExpressionLength.Value)
                return false;
            var opens = kind == ExpressionCompletionKind.Delimiter || kind == ExpressionCompletionKind.LogicalOperator
                ? insertion.Count(character => ExpressionGrammar.IsListStart(character)) : 0;
            if (opens > 0 &&
                (_suffixScopes + opens > ExpressionCompletionBudget.MaximumScopes ||
                    _policy.MaxParenthesisDepth.HasValue &&
                    _suffixDepth + insertion.Count(character => character == '(') > _policy.MaxParenthesisDepth.Value))
                return false;
            if (_policy.MaxAtomicConditions.HasValue)
            {
                var addsCondition = kind == ExpressionCompletionKind.Operator ? 1 :
                    kind == ExpressionCompletionKind.LogicalOperator && (insertion.Trim() == "and" || insertion.Trim() == "or") ? 1 :
                    !_conditionReserved && _context.Slot == ExpressionCompletionSlot.Condition ? 1 : 0;
                var replacedImplicit = kind == ExpressionCompletionKind.Operator ? _implicitReceiverConditions : 0;
                if (_conditions - _replacedConditions - replacedImplicit + addsCondition > _policy.MaxAtomicConditions.Value)
                    return false;
            }
            if (_policy.MaxInItems.HasValue)
            {
                var addsItem = _context.Slot == ExpressionCompletionSlot.MembershipItem ? 1 :
                    _context.Slot == ExpressionCompletionSlot.MembershipSeparator && insertion == "," ? 1 : 0;
                if (_membershipItems - (_replacesMembershipItem ? 1 : 0) + addsItem > _policy.MaxInItems.Value)
                    return false;
            }
            return true;
        }
    }
}
