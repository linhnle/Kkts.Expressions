using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kkts.Expressions.Internal
{
    internal sealed class LegacyFilterComposition
    {
        private readonly bool _isAnd;
        private readonly Func<Expression> _leaf;
        private readonly Func<CancellationToken, Task<Expression>> _asyncLeaf;
        private readonly IReadOnlyList<LegacyFilterComposition> _children;

        private LegacyFilterComposition(
            bool isAnd,
            Func<Expression> leaf,
            Func<CancellationToken, Task<Expression>> asyncLeaf,
            IReadOnlyList<LegacyFilterComposition> children)
        {
            _isAnd = isAnd;
            _leaf = leaf;
            _asyncLeaf = asyncLeaf;
            _children = children;
        }

        internal static LegacyFilterComposition Leaf(Func<Expression> evaluator)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            return new LegacyFilterComposition(false, evaluator, null, null);
        }

        internal static LegacyFilterComposition AsyncLeaf(
            Func<CancellationToken, Task<Expression>> evaluator)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            return new LegacyFilterComposition(false, null, evaluator, null);
        }

        internal static LegacyFilterComposition And(IEnumerable<LegacyFilterComposition> children) =>
            Group(true, children);

        internal static LegacyFilterComposition Or(IEnumerable<LegacyFilterComposition> children) =>
            Group(false, children);

        internal Expression Build()
        {
            if (_leaf != null) return _leaf();
            Expression body = null;
            foreach (var child in _children)
            {
                var next = child.Build();
                body = body == null
                    ? next
                    : _isAnd ? Expression.AndAlso(body, next) : Expression.OrElse(body, next);
            }
            return body;
        }

        internal async Task<Expression> BuildAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_leaf != null) return _leaf();
            if (_asyncLeaf != null)
                return await _asyncLeaf(cancellationToken).ConfigureAwait(false);
            Expression body = null;
            foreach (var child in _children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = await child.BuildAsync(cancellationToken).ConfigureAwait(false);
                body = body == null
                    ? next
                    : _isAnd ? Expression.AndAlso(body, next) : Expression.OrElse(body, next);
            }
            return body;
        }

        private static LegacyFilterComposition Group(
            bool isAnd,
            IEnumerable<LegacyFilterComposition> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));
            return new LegacyFilterComposition(
                isAnd,
                null,
                null,
                Array.AsReadOnly(new List<LegacyFilterComposition>(children).ToArray()));
        }
    }
}
