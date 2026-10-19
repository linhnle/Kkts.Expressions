using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Kkts.Expressions.OpenSearch.Internal
{
    /// <summary>
    /// Translates one Boolean lambda into the clause model. Diagnostics accumulate across the whole tree; any diagnostic
    /// means the caller must discard the output.
    /// </summary>
    internal sealed class PredicateTranslator
    {
        private readonly OpenSearchMapping _mapping;
        private readonly List<OpenSearchTranslationDiagnostic> _diagnostics;
        private Scope _current;

        public PredicateTranslator(OpenSearchMapping mapping, List<OpenSearchTranslationDiagnostic> diagnostics)
        {
            _mapping = mapping;
            _diagnostics = diagnostics;
        }

        public Clause Translate(LambdaExpression predicate)
        {
            var root = new Scope(predicate.Parameters[0], MemberChain.Empty, new HashSet<string>(), false, null);
            return Visit(predicate.Body, root);
        }

        private Clause Visit(Expression expression, Scope scope)
        {
            var previous = _current;
            _current = scope;
            try
            {
                return VisitCore(expression, scope);
            }
            finally
            {
                _current = previous;
            }
        }

        private Clause VisitCore(Expression expression, Scope scope)
        {
            if (expression.NodeType == ExpressionType.Quote) return Visit(((UnaryExpression)expression).Operand, scope);

            if (ExpressionEvaluator.IsParameterFree(expression))
            {
                if (!TryEvaluate(expression, out var value)) return null;
                if (value is bool constant) return constant ? (Clause)MatchAllClause.Instance : MatchNoneClause.Instance;
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue, "A constant predicate must be true or false.", null, expression);
            }

            if (expression.Type != typeof(bool))
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Only Boolean expressions can be translated to query clauses.", null, expression);

            switch (expression.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.And:
                {
                    var binary = (BinaryExpression)expression;
                    var left = Visit(binary.Left, scope);
                    var right = Visit(binary.Right, scope);
                    return left == null || right == null ? null : Clauses.And(new[] { left, right });
                }
                case ExpressionType.OrElse:
                case ExpressionType.Or:
                {
                    var binary = (BinaryExpression)expression;
                    var left = Visit(binary.Left, scope);
                    var right = Visit(binary.Right, scope);
                    return left == null || right == null ? null : Clauses.Or(new[] { left, right });
                }
                case ExpressionType.Not:
                {
                    var operand = Visit(((UnaryExpression)expression).Operand, scope);
                    return operand == null ? null : Clauses.Not(operand);
                }
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    return VisitComparison((BinaryExpression)expression, scope);
                case ExpressionType.MemberAccess:
                case ExpressionType.Parameter:
                    return VisitBooleanMember(expression, scope);
                case ExpressionType.Call:
                    return VisitCall((MethodCallExpression)expression, scope);
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    return Report(OpenSearchDiagnosticCodes.UnsupportedConversion,
                        "Converting a value to bool, such as unwrapping a nullable Boolean, cannot be translated.", null, expression);
                case ExpressionType.Conditional:
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Conditional expressions cannot be translated.", null, expression);
                case ExpressionType.TypeIs:
                case ExpressionType.TypeEqual:
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Type tests cannot be translated.", null, expression);
                default:
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                        $"Expressions of kind {expression.NodeType} cannot be translated.", null, expression);
            }
        }

        private Clause VisitBooleanMember(Expression expression, Scope scope)
        {
            var operand = Classify(expression, scope);
            if (operand == null) return null;
            if (!(operand is FieldOperand field))
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Only Boolean fields can be used as standalone predicates.", null, expression);
            if (!CheckScalarField(field)) return null;
            return Wrap(new TermClause(field.Member.Path, JsonValue.Create(true)), field.Member, scope);
        }

        #region Comparisons

        private Clause VisitComparison(BinaryExpression binary, Scope scope)
        {
            var left = Classify(binary.Left, scope);
            var right = Classify(binary.Right, scope);
            if (left == null || right == null) return null;

            if (left is FieldOperand && right is FieldOperand)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Comparisons between two fields cannot be translated.", null, binary);
            if (left is ConstantOperand && right is ConstantOperand)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "A comparison must involve a field.", null, binary);

            var op = binary.NodeType;
            FieldOperand field;
            ConstantOperand constant;
            if (left is FieldOperand leftField)
            {
                field = leftField;
                constant = (ConstantOperand)right;
            }
            else
            {
                field = (FieldOperand)right;
                constant = (ConstantOperand)left;
                op = Mirror(op);
            }

            if (binary.Method != null && !IsBuiltInOperator(binary.Method, field))
                return Report(OpenSearchDiagnosticCodes.UnsupportedMethod,
                    $"The user-defined operator {binary.Method.Name} cannot be translated.", field.Member.MemberPath, binary);

            return EmitComparison(field, op, constant.Value, binary, scope);
        }

        private static bool IsBuiltInOperator(MethodInfo method, FieldOperand field)
        {
            var declaring = method.DeclaringType;
            return declaring == typeof(string) || declaring == typeof(decimal) || declaring == typeof(DateTime) ||
                declaring == typeof(DateTimeOffset) || declaring == typeof(TimeSpan) || declaring == typeof(Guid) ||
                declaring == field.Member.ValueType;
        }

        private static ExpressionType Mirror(ExpressionType op)
        {
            switch (op)
            {
                case ExpressionType.LessThan: return ExpressionType.GreaterThan;
                case ExpressionType.LessThanOrEqual: return ExpressionType.GreaterThanOrEqual;
                case ExpressionType.GreaterThan: return ExpressionType.LessThan;
                case ExpressionType.GreaterThanOrEqual: return ExpressionType.LessThanOrEqual;
                default: return op;
            }
        }

        private Clause EmitComparison(FieldOperand field, ExpressionType op, object value, Expression source, Scope scope)
        {
            var member = field.Member;
            if (!field.IsElement && member.IsCollection)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Collections cannot be compared directly; use Any(...) or Contains(...).", member.MemberPath, source);

            if (!field.IsElement && member.IsOrdinaryObject)
            {
                if (value == null && (op == ExpressionType.Equal || op == ExpressionType.NotEqual))
                    return EmitObjectNullCheck(field, op == ExpressionType.Equal, source, scope);
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Objects can only be compared with null.", member.MemberPath, source);
            }

            if (field.IsElement && !member.IsScalarValue)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Collection elements that are objects can only be filtered through their members.", member.MemberPath, source);

            if (value == null)
            {
                switch (op)
                {
                    case ExpressionType.Equal:
                        return Clauses.Not(Wrap(new ExistsClause(member.Path), member, scope));
                    case ExpressionType.NotEqual:
                        return Wrap(new ExistsClause(member.Path), member, scope);
                    default:
                        // A lifted ordered comparison with null is always false.
                        return MatchNoneClause.Instance;
                }
            }

            if (ValueFormatter.IsNonFinite(value))
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue, "Non-finite floating-point values (NaN or infinity) cannot be translated.", member.MemberPath, source);

            var conversion = CreateConversion(field, source);
            if (conversion == null) return null;
            if (!conversion.IsIdentity) return EmitConvertedComparison(field, conversion, op, value, source, scope);

            var isRange = op != ExpressionType.Equal && op != ExpressionType.NotEqual;
            if (member.IsStringLike)
            {
                if (isRange)
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Ordered comparisons of strings cannot be translated.", member.MemberPath, source);
                if (!RequireExactPath(member, source)) return null;
            }

            if (isRange && member.ValueType.IsEnum && member.EnumFormat == EnumFormat.Name && member.Converter == null)
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue,
                    "Ordered comparisons of enums require the numeric enum format.", member.MemberPath, source);

            if (!TryFormat(value, member, source, out var json)) return null;
            var path = member.IsStringLike ? member.ExactPath : member.Path;
            var caseInsensitive = member.IsStringLike && member.CaseInsensitive;
            switch (op)
            {
                case ExpressionType.Equal:
                    return Wrap(new TermClause(path, json, caseInsensitive), member, scope);
                case ExpressionType.NotEqual:
                    return Clauses.Not(Wrap(new TermClause(path, json, caseInsensitive), member, scope));
                case ExpressionType.LessThan:
                    return Wrap(new RangeClause(path, lt: json), member, scope);
                case ExpressionType.LessThanOrEqual:
                    return Wrap(new RangeClause(path, lte: json), member, scope);
                case ExpressionType.GreaterThan:
                    return Wrap(new RangeClause(path, gt: json), member, scope);
                default:
                    return Wrap(new RangeClause(path, gte: json), member, scope);
            }
        }

        private Clause EmitObjectNullCheck(FieldOperand field, bool isNull, Expression source, Scope scope)
        {
            var member = field.Member;
            Clause present;
            if (member.IsNested)
            {
                if (scope.Opened.Contains(member.Path))
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                        "A nested object cannot be null-checked inside its own nested query.", member.MemberPath, source);
                present = Wrap(new NestedClause(member.Path, MatchAllClause.Instance), member, scope);
            }
            else if (member.PresencePath != null)
            {
                return Wrap(new TermClause(member.PresencePath, JsonValue.Create(!isNull)), member, scope);
            }
            else
            {
                return Report(OpenSearchDiagnosticCodes.PresenceMarkerRequired,
                    "Null checks on objects require a registered Boolean presence marker (Presence(...)) or a nested mapping; " +
                    "object presence cannot be inferred from child fields.", member.MemberPath, source);
            }

            return isNull ? Clauses.Not(present) : present;
        }

        private Clause EmitConvertedComparison(FieldOperand field, NumericConversion conversion, ExpressionType op, object value, Expression source, Scope scope)
        {
            var member = field.Member;
            if (!TryCoerceTarget(value, conversion, field, source, out var target)) return null;
            if (op == ExpressionType.NotEqual)
            {
                var equal = EmitIntervals(field, conversion, ToList(conversion.Equal(target)), source, scope);
                return equal == null ? null : Clauses.Not(equal);
            }

            return EmitIntervals(field, conversion, ToList(conversion.Compare(op, target)), source, scope);
        }

        private bool TryCoerceTarget(object value, NumericConversion conversion, FieldOperand field, Expression source, out object target)
        {
            if (ValueConversions.TryConvert(value, conversion.TargetType, true, out target) && target != null) return true;
            Report(OpenSearchDiagnosticCodes.UnsupportedConversion,
                $"The compared value cannot be represented as {conversion.TargetType.Name}.", field.Member.MemberPath, source);
            return false;
        }

        private static IReadOnlyList<SourceInterval> ToList(SourceInterval? interval) =>
            interval == null ? Array.Empty<SourceInterval>() : new[] { interval.Value };

        /// <summary>Emits clauses matching source values in the given ascending, non-overlapping intervals.</summary>
        private Clause EmitIntervals(FieldOperand field, NumericConversion conversion, IReadOnlyList<SourceInterval> intervals, Expression source, Scope scope)
        {
            var member = field.Member;
            if (intervals.Count == 0) return MatchNoneClause.Instance;
            if (intervals.Count == 1 && intervals[0].Lower == conversion.Min && intervals[0].Upper == conversion.Max)
                return Wrap(new ExistsClause(member.Path), member, scope);

            if (member.Converter != null)
                return Report(OpenSearchDiagnosticCodes.UnsupportedConversion,
                    "Converted comparisons cannot be translated for fields with a value converter, because numeric equivalence cannot be established.",
                    member.MemberPath, source);

            var singletons = new List<JsonNode>();
            var clauses = new List<Clause>();
            var path = member.IsStringLike ? member.ExactPath : member.Path;
            if (member.IsStringLike && !RequireExactPath(member, source)) return null;
            foreach (var interval in intervals)
            {
                if (interval.Lower == interval.Upper)
                {
                    if (!TryFormat(conversion.ToSourceValue(interval.Lower), member, source, out var json)) return null;
                    singletons.Add(json);
                    continue;
                }

                if (member.IsStringLike)
                    return Report(OpenSearchDiagnosticCodes.UnsupportedConversion,
                        "Ordered comparisons of characters cannot be translated.", member.MemberPath, source);
                if (member.ValueType.IsEnum && member.EnumFormat == EnumFormat.Name)
                    return Report(OpenSearchDiagnosticCodes.UnsupportedValue,
                        "Ordered comparisons of enums require the numeric enum format.", member.MemberPath, source);

                JsonNode lower = null, upper = null;
                if (interval.Lower != conversion.Min && !TryFormat(conversion.ToSourceValue(interval.Lower), member, source, out lower)) return null;
                if (interval.Upper != conversion.Max && !TryFormat(conversion.ToSourceValue(interval.Upper), member, source, out upper)) return null;
                clauses.Add(new RangeClause(path, gte: lower, lte: upper));
            }

            var caseInsensitive = member.IsStringLike && member.CaseInsensitive;
            if (singletons.Count == 1) clauses.Insert(0, new TermClause(path, singletons[0], caseInsensitive));
            else if (singletons.Count > 1 && caseInsensitive) clauses.InsertRange(0, singletons.Select(s => new TermClause(path, s, true)));
            else if (singletons.Count > 1) clauses.Insert(0, new TermsClause(path, singletons));
            return Wrap(Clauses.Or(clauses), member, scope);
        }

        private NumericConversion CreateConversion(FieldOperand field, Expression source)
        {
            if (NumericConversion.TryCreate(field.Member.ValueType, field.Conversions, out var conversion, out var error)) return conversion;
            Report(OpenSearchDiagnosticCodes.UnsupportedConversion, "The field conversion cannot be translated: " + error + ".", field.Member.MemberPath, source);
            return null;
        }

        #endregion

        #region Method calls

        private Clause VisitCall(MethodCallExpression call, Scope scope)
        {
            var method = call.Method;
            if (method.DeclaringType == typeof(string) && !method.IsStatic &&
                (method.Name == nameof(string.Contains) || method.Name == nameof(string.StartsWith) || method.Name == nameof(string.EndsWith)))
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && (parameters[0].ParameterType == typeof(string) || parameters[0].ParameterType == typeof(char)))
                    return VisitStringFunction(call, scope);
                return Report(OpenSearchDiagnosticCodes.UnsupportedMethod,
                    $"string.{method.Name} overloads with comparison or culture options cannot be translated; configure the field as case-insensitive instead.",
                    null, call);
            }

            if (TryGetContains(call, out var collection, out var item)) return VisitContains(collection, item, call, scope);

            if ((method.DeclaringType == typeof(Enumerable) || method.DeclaringType == typeof(Queryable)) && method.Name == nameof(Enumerable.Any))
                return VisitAny(call, scope);

            return Report(OpenSearchDiagnosticCodes.UnsupportedMethod,
                $"The method {method.DeclaringType?.Name}.{method.Name} cannot be translated.", null, call);
        }

        private Clause VisitStringFunction(MethodCallExpression call, Scope scope)
        {
            var target = Classify(call.Object, scope);
            var argument = Classify(call.Arguments[0], scope);
            if (target == null || argument == null) return null;
            if (!(target is FieldOperand field) || !(argument is ConstantOperand constant))
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    $"string.{call.Method.Name} must be called on a field with a constant argument.", null, call);

            var member = field.Member;
            if (field.Conversions.Count > 0 || (field.IsElement ? !member.IsStringLike : member.IsCollection || !member.IsStringLike))
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "String functions require a string field.", member.MemberPath, call);
            if (constant.Value == null)
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue, $"string.{call.Method.Name} with a null argument cannot be translated.", member.MemberPath, call);
            if (!RequireExactPath(member, call)) return null;

            var text = constant.Value is char c ? c.ToString() : (string)constant.Value;
            if (text.Length == 0) return Wrap(new ExistsClause(member.Path), member, scope);

            Clause clause;
            switch (call.Method.Name)
            {
                case nameof(string.StartsWith):
                    clause = new PrefixClause(member.ExactPath, text, member.CaseInsensitive);
                    break;
                case nameof(string.EndsWith):
                    clause = new WildcardClause(member.ExactPath, "*" + EscapeWildcard(text), member.CaseInsensitive);
                    break;
                default:
                    clause = new WildcardClause(member.ExactPath, "*" + EscapeWildcard(text) + "*", member.CaseInsensitive);
                    break;
            }

            return Wrap(clause, member, scope);
        }

        internal static string EscapeWildcard(string text)
        {
            var builder = new StringBuilder(text.Length + 4);
            foreach (var c in text)
            {
                if (c == '\\' || c == '*' || c == '?') builder.Append('\\');
                builder.Append(c);
            }

            return builder.ToString();
        }

        private static bool TryGetContains(MethodCallExpression call, out Expression collection, out Expression item)
        {
            collection = item = null;
            var method = call.Method;
            if (method.Name != "Contains") return false;

            if (method.DeclaringType == typeof(Enumerable) && call.Arguments.Count == 2)
            {
                collection = call.Arguments[0];
                item = call.Arguments[1];
                return true;
            }

            // C# 14 binds array.Contains(x) in expression trees to MemoryExtensions.Contains(ReadOnlySpan<T>, T[, comparer]).
            if (method.DeclaringType?.FullName == "System.MemoryExtensions" &&
                (call.Arguments.Count == 2 || (call.Arguments.Count == 3 && call.Arguments[2] is ConstantExpression comparer && comparer.Value == null)) &&
                call.Arguments[0] is MethodCallExpression implicitSpan && implicitSpan.Method.Name == "op_Implicit" &&
                implicitSpan.Arguments.Count == 1 && implicitSpan.Arguments[0].Type.IsArray)
            {
                collection = implicitSpan.Arguments[0];
                item = call.Arguments[1];
                return true;
            }

            if (!method.IsStatic && call.Object != null && call.Arguments.Count == 1)
            {
                var element = TypeClassifier.GetCollectionElementType(call.Object.Type);
                if (element != null && method.GetParameters()[0].ParameterType == element)
                {
                    collection = call.Object;
                    item = call.Arguments[0];
                    return true;
                }
            }

            return false;
        }

        private Clause VisitContains(Expression collectionExpression, Expression itemExpression, Expression source, Scope scope)
        {
            var collection = Classify(collectionExpression, scope);
            var item = Classify(itemExpression, scope);
            if (collection == null || item == null) return null;

            if (collection is ConstantOperand list && item is FieldOperand field)
                return EmitMembership(field, list.Value, source, scope);
            if (collection is FieldOperand collectionField && item is ConstantOperand value)
                return EmitCollectionContains(collectionField, value.Value, source, scope);

            return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                "Contains must test a field against a constant list, or a collection field for a constant value.", null, source);
        }

        private Clause EmitMembership(FieldOperand field, object listValue, Expression source, Scope scope)
        {
            var member = field.Member;
            if (!ExpressionEvaluator.TryReadList(listValue, out var values))
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue,
                    "Membership lists must be non-null arrays or materialized collections; deferred sequences are not evaluated.", member.MemberPath, source);
            if ((!field.IsElement && (member.IsCollection || member.IsOrdinaryObject)) || (field.IsElement && !member.IsScalarValue))
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Membership requires a scalar field.", member.MemberPath, source);

            var hasNull = values.Any(v => v == null);
            var distinct = new List<object>();
            foreach (var value in values.Where(v => v != null))
            {
                if (!distinct.Contains(value)) distinct.Add(value);
            }

            if (distinct.Any(ValueFormatter.IsNonFinite))
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue, "Non-finite floating-point values (NaN or infinity) cannot be translated.", member.MemberPath, source);

            var conversion = CreateConversion(field, source);
            if (conversion == null) return null;

            Clause positive;
            if (!conversion.IsIdentity)
            {
                var intervals = new List<SourceInterval>();
                foreach (var value in distinct)
                {
                    if (!TryCoerceTarget(value, conversion, field, source, out var target)) return null;
                    var interval = conversion.Equal(target);
                    if (interval != null) intervals.Add(interval.Value);
                }

                positive = EmitIntervals(field, conversion, NumericConversion.Union(intervals), source, scope);
                if (positive == null) return null;
            }
            else if (distinct.Count == 0)
            {
                positive = MatchNoneClause.Instance;
            }
            else
            {
                if (member.IsStringLike && !RequireExactPath(member, source)) return null;
                var json = new List<JsonNode>();
                foreach (var value in distinct)
                {
                    if (!TryFormat(value, member, source, out var node)) return null;
                    json.Add(node);
                }

                var path = member.IsStringLike ? member.ExactPath : member.Path;
                positive = member.IsStringLike && member.CaseInsensitive
                    ? Clauses.Or(json.Select(j => (Clause)new TermClause(path, j, true)))
                    : new TermsClause(path, json);
                positive = Wrap(positive, member, scope);
            }

            if (!hasNull) return positive;
            return Clauses.Or(new[] { positive, Clauses.Not(Wrap(new ExistsClause(member.Path), member, scope)) });
        }

        private Clause EmitCollectionContains(FieldOperand field, object value, Expression source, Scope scope)
        {
            var member = field.Member;
            if (field.IsElement || !member.IsCollection || field.Conversions.Count > 0)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Contains requires a collection field.", member.MemberPath, source);
            if (!member.IsScalarValue || member.IsNested)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Contains on collections of objects cannot be translated; use Any(...) with conditions on element members.", member.MemberPath, source);
            if (value == null)
                return Report(OpenSearchDiagnosticCodes.FlattenedCollection,
                    "Contains(null) cannot be translated on a non-nested collection, because indexed arrays do not retain null elements; " +
                    "use a nested mapping to represent null elements.", member.MemberPath, source);
            if (ValueFormatter.IsNonFinite(value))
                return Report(OpenSearchDiagnosticCodes.UnsupportedValue, "Non-finite floating-point values (NaN or infinity) cannot be translated.", member.MemberPath, source);
            if (member.IsStringLike && !RequireExactPath(member, source)) return null;
            if (!TryFormat(value, member, source, out var json)) return null;

            var path = member.IsStringLike ? member.ExactPath : member.Path;
            return Wrap(new TermClause(path, json, member.IsStringLike && member.CaseInsensitive), member, scope);
        }

        private Clause VisitAny(MethodCallExpression call, Scope scope)
        {
            var source = Classify(call.Arguments[0], scope);
            if (source == null) return null;
            if (!(source is FieldOperand collection) || collection.IsElement || !collection.Member.IsCollection || collection.Conversions.Count > 0)
                return Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Any must be called on a collection field.", null, call);

            var member = collection.Member;
            LambdaExpression lambda = null;
            if (call.Arguments.Count == 2)
            {
                var argument = call.Arguments[1];
                if (argument.NodeType == ExpressionType.Quote) argument = ((UnaryExpression)argument).Operand;
                lambda = argument as LambdaExpression;
                if (lambda == null)
                    return Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                        "Any requires an inline lambda predicate; delegates cannot be inspected.", member.MemberPath, call);
            }

            if (member.IsNested)
            {
                var opened = new HashSet<string>(scope.Opened);
                foreach (var enclosing in member.EnclosingNested) opened.Add(enclosing.Path);
                opened.Add(member.Path);

                var inner = lambda == null
                    ? MatchAllClause.Instance
                    : Visit(lambda.Body, new Scope(lambda.Parameters[0], member.Chain, opened, false, scope));
                if (inner == null) return null;
                if (inner is MatchNoneClause) return MatchNoneClause.Instance;
                return Wrap(new NestedClause(member.Path, inner), member, scope);
            }

            if (lambda == null)
                return Report(OpenSearchDiagnosticCodes.FlattenedCollection,
                    "Any() on a non-nested collection cannot be translated, because indexed arrays do not retain null or empty elements; " +
                    "map the collection as nested.", member.MemberPath, call);

            if (!IsFlattenedLeaf(lambda.Body, lambda.Parameters[0], out var nullMembership))
            {
                return Report(OpenSearchDiagnosticCodes.FlattenedCollection, nullMembership
                    ? "Membership with null candidates inside Any on a non-nested collection cannot be translated, because indexed arrays do not retain null elements; map the collection as nested."
                    : "Any on a non-nested collection supports only a single positive condition, because indexed arrays lose per-element grouping; map the collection as nested.",
                    member.MemberPath, call);
            }

            return Visit(lambda.Body, new Scope(lambda.Parameters[0], member.Chain, scope.Opened, true, scope));
        }

        /// <summary>
        /// Whether a predicate over a flattened element is a single positive condition whose flattened evaluation is exact.
        /// </summary>
        private static bool IsFlattenedLeaf(Expression body, ParameterExpression element, out bool nullMembership)
        {
            nullMembership = false;
            switch (body.NodeType)
            {
                case ExpressionType.Equal:
                {
                    var binary = (BinaryExpression)body;
                    return IsElementOperand(binary.Left, element) && IsNonNullConstant(binary.Right) ||
                        IsElementOperand(binary.Right, element) && IsNonNullConstant(binary.Left);
                }
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                {
                    var binary = (BinaryExpression)body;
                    return IsElementOperand(binary.Left, element) && ExpressionEvaluator.IsParameterFree(binary.Right) ||
                        IsElementOperand(binary.Right, element) && ExpressionEvaluator.IsParameterFree(binary.Left);
                }
                case ExpressionType.MemberAccess:
                case ExpressionType.Parameter:
                    return body.Type == typeof(bool) && IsElementOperand(body, element);
                case ExpressionType.Call:
                {
                    var call = (MethodCallExpression)body;
                    if (call.Method.DeclaringType == typeof(string) && !call.Method.IsStatic)
                        return IsElementOperand(call.Object, element) && call.Arguments.All(ExpressionEvaluator.IsParameterFree);
                    if (TryGetContains(call, out var collection, out var item) &&
                        ExpressionEvaluator.IsParameterFree(collection) && IsElementOperand(item, element))
                    {
                        if (ExpressionEvaluator.TryEvaluate(collection, out var list, out _) &&
                            ExpressionEvaluator.TryReadList(list, out var values) && values.Any(v => v == null))
                        {
                            nullMembership = true;
                            return false;
                        }

                        return true;
                    }

                    return false;
                }
                default:
                    return false;
            }
        }

        private static bool IsElementOperand(Expression expression, ParameterExpression element)
        {
            while (expression.NodeType == ExpressionType.Convert || expression.NodeType == ExpressionType.ConvertChecked)
                expression = ((UnaryExpression)expression).Operand;
            while (expression is MemberExpression member) expression = member.Expression;
            return expression == element;
        }

        private static bool IsNonNullConstant(Expression expression)
        {
            return ExpressionEvaluator.IsParameterFree(expression) &&
                ExpressionEvaluator.TryEvaluate(expression, out var value, out _) && value != null;
        }

        #endregion

        #region Operands

        private Operand Classify(Expression expression, Scope scope)
        {
            if (ExpressionEvaluator.IsParameterFree(expression))
                return TryEvaluate(expression, out var value) ? new ConstantOperand(value) : null;

            var conversions = new List<UnaryExpression>();
            var current = expression;
            while (current.NodeType == ExpressionType.Convert || current.NodeType == ExpressionType.ConvertChecked)
            {
                conversions.Insert(0, (UnaryExpression)current);
                current = ((UnaryExpression)current).Operand;
            }

            if (current is MemberExpression || current is ParameterExpression) return ResolveField(current, expression, conversions, scope);

            switch (current.NodeType)
            {
                case ExpressionType.Call:
                    var method = ((MethodCallExpression)current).Method;
                    if (method.DeclaringType == typeof(string) && method.Name == nameof(string.Concat))
                    {
                        Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Arithmetic and concatenation on fields cannot be translated.", null, expression);
                        return null;
                    }

                    Report(OpenSearchDiagnosticCodes.UnsupportedMethod,
                        $"The method {method.DeclaringType?.Name}.{method.Name} cannot be translated as an operand.", null, expression);
                    return null;
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Power:
                    Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Arithmetic and concatenation on fields cannot be translated.", null, expression);
                    return null;
                case ExpressionType.Conditional:
                case ExpressionType.Coalesce:
                    Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Conditional and coalescing expressions cannot be translated.", null, expression);
                    return null;
                case ExpressionType.TypeAs:
                case ExpressionType.TypeIs:
                    Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Type tests and casts cannot be translated.", null, expression);
                    return null;
                default:
                    Report(OpenSearchDiagnosticCodes.UnsupportedExpression, $"Operands of kind {current.NodeType} cannot be translated.", null, expression);
                    return null;
            }
        }

        private Operand ResolveField(Expression access, Expression source, List<UnaryExpression> conversions, Scope scope)
        {
            var members = new List<MemberInfo>();
            var current = access;
            while (current is MemberExpression member)
            {
                if (member.Expression == null || !(member.Member is PropertyInfo || member.Member is FieldInfo))
                {
                    Report(OpenSearchDiagnosticCodes.UnmappedField, "Only instance properties and fields can be mapped to OpenSearch fields.", null, source);
                    return null;
                }

                members.Insert(0, member.Member);
                current = member.Expression;
            }

            if (!(current is ParameterExpression parameter) || !scope.Knows(parameter))
            {
                Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Fields must be accessed through the predicate parameter or a supported collection element.", null, source);
                return null;
            }

            if (parameter != scope.Parameter)
            {
                Report(OpenSearchDiagnosticCodes.UnsupportedExpression,
                    "Conditions inside Any(...) can only reference the current collection element.", null, source);
                return null;
            }

            var chain = scope.Prefix;
            var currentType = parameter.Type;
            foreach (var member in members)
            {
                var memberText = chain.Count == 0 ? member.Name : chain + "." + member.Name;
                if (Nullable.GetUnderlyingType(currentType) != null)
                {
                    Report(OpenSearchDiagnosticCodes.UnmappedField,
                        "Nullable members such as Value and HasValue cannot be mapped; compare the field with null instead.", memberText, source);
                    return null;
                }

                if (TypeClassifier.IsCollection(currentType))
                {
                    Report(OpenSearchDiagnosticCodes.UnmappedField,
                        "Collection members such as Count cannot be mapped; use Any(...) instead.", memberText, source);
                    return null;
                }

                if (chain.Count > 0 && TypeClassifier.IsScalar(currentType))
                {
                    Report(OpenSearchDiagnosticCodes.UnmappedField,
                        "Members of scalar values, such as string.Length or DateTime.Year, cannot be mapped.", memberText, source);
                    return null;
                }

                chain = chain.Append(member);
                currentType = MemberChain.GetMemberType(member);
            }

            var isElement = members.Count == 0;
            if (isElement && chain.Count == 0)
            {
                Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "The predicate parameter itself cannot be compared.", null, source);
                return null;
            }

            return new FieldOperand(_mapping.Resolve(chain), isElement, conversions);
        }

        private bool CheckScalarField(FieldOperand field)
        {
            var member = field.Member;
            if (field.Conversions.Count > 0 || member.ValueType != typeof(bool) || (!field.IsElement && member.IsCollection) ||
                TypeClassifier.IsNullable(field.IsElement ? TypeClassifier.GetCollectionElementType(member.MemberType) : member.MemberType))
            {
                Report(OpenSearchDiagnosticCodes.UnsupportedExpression, "Only non-nullable Boolean fields can be used as standalone predicates.", member.MemberPath, null);
                return false;
            }

            return true;
        }

        #endregion

        #region Helpers

        private Clause Wrap(Clause clause, ResolvedMember member, Scope scope)
        {
            return Clauses.WrapNested(clause, member.EnclosingNested.Where(n => !scope.Opened.Contains(n.Path)).Select(n => n.Path));
        }

        private bool RequireExactPath(ResolvedMember member, Expression source)
        {
            if (member.ExactPath != null) return true;
            Report(OpenSearchDiagnosticCodes.TextFieldExactMatch,
                "Exact string matching, string functions, and sorting require a keyword field or keyword subfield; the field is mapped as text only.",
                member.MemberPath, source);
            return false;
        }

        private bool TryFormat(object value, ResolvedMember member, Expression source, out JsonNode json)
        {
            if (ValueFormatter.TryFormat(value, member, out json, out var error)) return true;
            Report(OpenSearchDiagnosticCodes.UnsupportedValue, $"A value for {member.ValueType.Name} field cannot be formatted: {error}.", member.MemberPath, source);
            return false;
        }

        private bool TryEvaluate(Expression expression, out object value)
        {
            if (ExpressionEvaluator.TryEvaluate(expression, out value, out var failing)) return true;
            Report(OpenSearchDiagnosticCodes.UnsupportedValue,
                "Only constants, captured variables, built-in conversions, and array initializers can be evaluated; other computations are not executed during translation.",
                null, failing ?? expression);
            return false;
        }

        private Clause Report(string code, string message, string memberPath, Expression expression)
        {
            _diagnostics.Add(new OpenSearchTranslationDiagnostic(code, message, memberPath ?? InferMemberPath(expression), ExpressionText.Render(expression)));
            return null;
        }

        /// <summary>Finds the CLR path of the first field referenced by an offending expression, for diagnostics.</summary>
        private string InferMemberPath(Expression expression)
        {
            if (expression == null) return null;
            var finder = new FieldFinder();
            finder.Visit(expression);
            if (finder.Parameter == null) return null;

            var prefix = MemberChain.Empty;
            for (var scope = _current; scope != null; scope = scope.Parent)
            {
                if (scope.Parameter != finder.Parameter) continue;
                prefix = scope.Prefix;
                break;
            }

            var names = prefix.Members.Select(m => m.Name).Concat(finder.Members.Select(m => m.Name)).ToArray();
            return names.Length == 0 ? null : string.Join(".", names);
        }

        private sealed class FieldFinder : ExpressionVisitor
        {
            public ParameterExpression Parameter { get; private set; }

            public List<MemberInfo> Members { get; } = new List<MemberInfo>();

            public override Expression Visit(Expression node) => Parameter != null ? node : base.Visit(node);

            protected override Expression VisitMember(MemberExpression node)
            {
                var members = new List<MemberInfo>();
                Expression current = node;
                while (current is MemberExpression member)
                {
                    members.Insert(0, member.Member);
                    current = member.Expression;
                }

                if (current is ParameterExpression parameter)
                {
                    Parameter = parameter;
                    Members.AddRange(members);
                    return node;
                }

                return base.VisitMember(node);
            }

            protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node) => node;
        }

        #endregion

        private sealed class Scope
        {
            public Scope(ParameterExpression parameter, MemberChain prefix, HashSet<string> opened, bool flattened, Scope parent)
            {
                Parameter = parameter;
                Prefix = prefix;
                Opened = opened;
                Flattened = flattened;
                Parent = parent;
            }

            public ParameterExpression Parameter { get; }

            public MemberChain Prefix { get; }

            public HashSet<string> Opened { get; }

            public bool Flattened { get; }

            public Scope Parent { get; }

            public bool Knows(ParameterExpression parameter)
            {
                for (var scope = this; scope != null; scope = scope.Parent)
                    if (scope.Parameter == parameter) return true;
                return false;
            }
        }

        private abstract class Operand
        {
        }

        private sealed class ConstantOperand : Operand
        {
            public ConstantOperand(object value)
            {
                Value = value;
            }

            public object Value { get; }
        }

        private sealed class FieldOperand : Operand
        {
            public FieldOperand(ResolvedMember member, bool isElement, IReadOnlyList<UnaryExpression> conversions)
            {
                Member = member;
                IsElement = isElement;
                Conversions = conversions;
            }

            public ResolvedMember Member { get; }

            /// <summary>The operand is a collection element itself, such as <c>t</c> in <c>x.Tags.Any(t => t == "a")</c>.</summary>
            public bool IsElement { get; }

            public IReadOnlyList<UnaryExpression> Conversions { get; }
        }
    }
}
