using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>
    /// Evaluates parameter-free constant, captured member, conversion, and array-initialization subtrees by reflection.
    /// Any other computation is refused rather than executed; nothing is compiled.
    /// </summary>
    internal static class ExpressionEvaluator
    {
        public static bool IsParameterFree(Expression expression)
        {
            var finder = new ParameterFinder();
            finder.Visit(expression);
            return !finder.Found;
        }

        public static bool TryEvaluate(Expression expression, out object value, out Expression failing)
        {
            value = null;
            failing = null;
            switch (expression)
            {
                case ConstantExpression constant:
                    value = constant.Value;
                    return true;
                case MemberExpression member:
                    object instance = null;
                    if (member.Expression != null)
                    {
                        if (!TryEvaluate(member.Expression, out instance, out failing)) return false;
                        if (instance == null)
                        {
                            failing = member;
                            return false;
                        }
                    }

                    if (member.Member is FieldInfo field)
                    {
                        value = field.GetValue(instance);
                        return true;
                    }

                    if (member.Member is PropertyInfo property && property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(instance);
                        return true;
                    }

                    failing = member;
                    return false;
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked:
                    if (unary.Method != null)
                    {
                        failing = unary;
                        return false;
                    }

                    if (!TryEvaluate(unary.Operand, out var operand, out failing)) return false;
                    if (ValueConversions.TryConvert(operand, unary.Type, unary.NodeType == ExpressionType.ConvertChecked, out value)) return true;
                    failing = unary;
                    return false;
                case UnaryExpression quote when quote.NodeType == ExpressionType.Quote:
                    return TryEvaluate(quote.Operand, out value, out failing);
                case NewArrayExpression array when array.NodeType == ExpressionType.NewArrayInit:
                    var items = Array.CreateInstance(array.Type.GetElementType(), array.Expressions.Count);
                    for (var i = 0; i < array.Expressions.Count; i++)
                    {
                        if (!TryEvaluate(array.Expressions[i], out var item, out failing)) return false;
                        items.SetValue(item, i);
                    }

                    value = items;
                    return true;
                default:
                    failing = expression;
                    return false;
            }
        }

        /// <summary>Reads a membership list without running deferred queries or other user code.</summary>
        public static bool TryReadList(object value, out List<object> items)
        {
            items = null;
            if (value == null) return false;
            var type = value.GetType();
            var isMaterialized = value is Array || value is ICollection || ImplementsGeneric(type, typeof(ICollection<>)) ||
                ImplementsGeneric(type, typeof(IReadOnlyCollection<>));
            if (!isMaterialized || !(value is IEnumerable enumerable)) return false;
            items = new List<object>();
            foreach (var item in enumerable) items.Add(item);
            return true;
        }

        private static bool ImplementsGeneric(Type type, Type definition)
        {
            foreach (var candidate in type.GetInterfaces())
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition) return true;
            return false;
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }

            public override Expression Visit(Expression node)
            {
                if (Found || node == null) return node;
                return base.Visit(node);
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found = true;
                return node;
            }
        }
    }

    /// <summary>Renders sub-expressions for diagnostics with constant values replaced by a placeholder.</summary>
    internal static class ExpressionText
    {
        public static string Render(Expression expression)
        {
            if (expression == null) return null;
            try
            {
                return new Redactor().Visit(expression).ToString();
            }
            catch (Exception)
            {
                return expression.NodeType.ToString();
            }
        }

        private sealed class Redactor : ExpressionVisitor
        {
            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value == null) return node;
                return Expression.Parameter(node.Type.IsByRef ? typeof(object) : node.Type, "<value>");
            }
        }
    }
}
