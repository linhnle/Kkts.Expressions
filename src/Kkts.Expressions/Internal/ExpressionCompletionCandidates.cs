using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kkts.Expressions.Internal
{
    internal sealed class ExpressionCompletionCandidates
    {
        private static readonly string[] CanonicalOperators = Interpreter.ComparisonOperators
            .Select(ExpressionGrammar.NormalizeOperator).Select(ExpressionLiteralCodec.CanonicalOperator).Distinct(StringComparer.Ordinal)
            .OrderBy(OperatorRank).ThenBy(op => op, StringComparer.Ordinal).ToArray();
        private readonly string _source;
        private readonly ExpressionSchema _schema;
        private readonly ExpressionQueryContext _queryContext;
        private readonly ExpressionVariableSchema _variables;
        private readonly ExpressionCompletionContext _context;
        private readonly ExpressionSemanticAnalyzer _binder;
        private readonly ExpressionCompletionPolicy _policy;
        private readonly List<ExpressionCompletionItem> _items = new List<ExpressionCompletionItem>();
        private readonly HashSet<string> _identities = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<ExpressionCompletionItem, string> _spellings =
            new Dictionary<ExpressionCompletionItem, string>();
        private readonly HashSet<ExpressionCompletionItem> _exactPrefixes = new HashSet<ExpressionCompletionItem>();

        internal ExpressionCompletionCandidates(
            string source, ExpressionSchema schema, ExpressionVariableSchema variables,
            ExpressionAnalysisResult syntax, ExpressionCompletionContext context,
            ExpressionQueryContext queryContext = null, ExpressionCompletionPolicy policy = null)
        {
            _source = source;
            _schema = schema;
            _queryContext = queryContext;
            _variables = variables;
            _context = context;
            _policy = policy;
            _binder = new ExpressionSemanticAnalyzer(source, schema, variables, syntax,
                queryContext, completionMode: true);
        }

        internal int DescriptorVisits { get; private set; }
        internal bool IsTruncated { get; private set; }
        internal IReadOnlyList<ExpressionCompletionItem> Items => _items;

        internal void AddConstructs()
        {
            if (!_context.IsAvailable || _context.Edit.IsQuoted ||
                _context.Edit.Kind == ExpressionTokenKind.Variable) return;
            switch (_context.Slot)
            {
                case ExpressionCompletionSlot.Condition:
                    AddConstruct(ExpressionCompletionKind.LogicalOperator, "not", "not(");
                    if (_context.CandidatePrefix.Length == 0)
                        AddConstruct(ExpressionCompletionKind.Delimiter, "(", "(");
                    break;
                case ExpressionCompletionSlot.Operand:
                    if (_context.CandidatePrefix.Length == 0)
                        AddConstruct(ExpressionCompletionKind.Delimiter, "(", "(");
                    break;
                case ExpressionCompletionSlot.MembershipOperand:
                    if (_context.CandidatePrefix.Length == 0)
                        foreach (var opening in new[] { "[", "(", "{" })
                            AddConstruct(ExpressionCompletionKind.Delimiter, opening, opening);
                    break;
                case ExpressionCompletionSlot.Operator:
                    if (_context.Receiver != null && _context.Receiver.IsBareBooleanPredicate &&
                        _binder.TryDescribeBinary("=", _context.Receiver,
                            ExpressionSemanticAnalyzer.SemanticNode.Literal(typeof(bool),
                                _context.Edit.Start, _context.Edit.Start, true), out _))
                    {
                        AddConstruct(ExpressionCompletionKind.LogicalOperator, "and", "and");
                        AddConstruct(ExpressionCompletionKind.LogicalOperator, "or", "or");
                        AddCloser();
                    }
                    break;
                case ExpressionCompletionSlot.Continuation:
                    AddConstruct(ExpressionCompletionKind.LogicalOperator, "and", "and");
                    AddConstruct(ExpressionCompletionKind.LogicalOperator, "or", "or");
                    AddCloser();
                    break;
                case ExpressionCompletionSlot.MembershipSeparator:
                    AddConstruct(ExpressionCompletionKind.Delimiter, ",", ",");
                    AddCloser();
                    break;
            }
        }

        private void AddCloser()
        {
            var closing = _context.Expectation.ClosingDelimiter;
            if (closing != '\0')
                AddConstruct(ExpressionCompletionKind.Delimiter, closing.ToString(), closing.ToString());
        }

        private void AddConstruct(ExpressionCompletionKind kind, string label, string insertion)
        {
            if (!Visit()) return;
            if (!label.StartsWith(_context.CandidatePrefix, StringComparison.OrdinalIgnoreCase)) return;
            var end = _context.Edit.Start + _context.Edit.Length;
            while (end < _source.Length && char.IsWhiteSpace(_source[end])) ++end;
            if (_context.Edit.Length == 0 && end < _source.Length &&
                (_source[end].ToString() == insertion ||
                    kind == ExpressionCompletionKind.LogicalOperator &&
                        (ExpressionGrammar.LogicalCharacters.Contains(_source[end]) ||
                            _source.Substring(end).StartsWith("and", StringComparison.OrdinalIgnoreCase) ||
                            _source.Substring(end).StartsWith("or", StringComparison.OrdinalIgnoreCase))))
                return;
            Add(kind, insertion, label, string.Empty, identity: label, matchSpelling: label);
        }

        internal void AddVariables()
        {
            if (_variables == null || !_context.IsAvailable || _context.Edit.IsQuoted ||
                !(_context.Slot == ExpressionCompletionSlot.Condition ||
                    _context.Slot == ExpressionCompletionSlot.Operand ||
                    _context.Slot == ExpressionCompletionSlot.MembershipOperand ||
                    _context.Slot == ExpressionCompletionSlot.MembershipItem)) return;
            var prefix = _context.Edit.Prefix.TrimStart('$');
            var suffix = _context.Edit.PathSuffix;
            foreach (var definition in _variables.Variables.Values)
            {
                if (!Visit()) return;
                if (definition.Name.StartsWith(prefix, StringComparison.Ordinal))
                    AddVariable(definition.Name + suffix);
                if (definition.Name.Contains(".")) continue;
                var dot = prefix.IndexOf('.');
                if (dot < 0)
                {
                    if (!definition.Name.StartsWith(prefix, StringComparison.Ordinal) ||
                        VariableCompatible(definition.Name)) continue;
                    AddVariableMembers(definition.Name, definition, definition.ClrType, definition.ReflectMembers, suffix);
                }
                else if (string.Equals(prefix.Substring(0, dot), definition.Name, StringComparison.Ordinal))
                {
                    var parentEnd = prefix.LastIndexOf('.');
                    var parent = prefix.Substring(0, parentEnd);
                    var current = definition;
                    var type = definition.ClrType;
                    var reflect = definition.ReflectMembers;
                    var canonical = definition.Name;
                    var found = true;
                    foreach (var segment in parent.Split('.').Skip(1))
                    {
                        if (!Visit()) return;
                        if (current != null && current.Members.TryGetValue(segment, out var member))
                        {
                            current = member;
                            type = member.ClrType;
                            reflect = member.ReflectMembers;
                            canonical += "." + member.Name;
                        }
                        else if (reflect)
                        {
                            if (!PropertyMetadata.GetMemberTypes(type).TryGetValue(segment, out var memberType) ||
                                !PropertyMetadata.GetPropertyNames(type).TryGetValue(segment, out var memberName))
                            {
                                found = false;
                                break;
                            }
                            current = null;
                            type = memberType;
                            canonical += "." + memberName;
                        }
                        else { found = false; break; }
                    }
                    if (found) AddVariableMembers(canonical, current, type, reflect, suffix);
                }
                if (IsTruncated) return;
            }
        }

        private void AddVariableMembers(string parent, ExpressionVariableDefinition definition,
            Type type, bool reflect, string suffix)
        {
            if (definition != null)
                foreach (var member in definition.Members.Values)
                {
                    if (!Visit()) return;
                    AddVariable(parent + "." + member.Name + suffix);
                }
            if (reflect)
                foreach (var member in PropertyMetadata.GetOrderedMemberTypes(type))
                {
                    if (!Visit()) return;
                    if (definition != null && definition.Members.ContainsKey(member.Key)) continue;
                    AddVariable(parent + "." + member.Key + suffix);
                }
        }

        private bool VariableCompatible(string name)
        {
            return _variables.TryGetVariable(name, out var type, out var nullability, out var elementType, out _) &&
                Compatible(ExpressionSemanticAnalyzer.SemanticNode.Value(type, nullability,
                    _context.Edit.Start, _context.Edit.Start + _context.Edit.Length,
                    elementType: elementType, isVariable: true));
        }

        private void AddVariable(string name)
        {
            var prefix = _context.Edit.Prefix.TrimStart('$');
            var rootLength = name.IndexOf('.');
            if (rootLength < 0) rootLength = name.Length;
            var prefixRootLength = prefix.IndexOf('.');
            if (prefixRootLength < 0) prefixRootLength = prefix.Length;
            if (prefixRootLength > rootLength ||
                !name.StartsWith(prefix.Substring(0, prefixRootLength), StringComparison.Ordinal) ||
                !MatchesPath(name, prefix, _context.Edit.PathSuffix) ||
                !_variables.TryGetVariable(name, out var type, out var nullability, out var elementType, out _))
                return;
            var operand = ExpressionSemanticAnalyzer.SemanticNode.Value(type, nullability,
                _context.Edit.Start, _context.Edit.Start + _context.Edit.Length, elementType: elementType, isVariable: true);
            if (!Compatible(operand)) return;
            Add(ExpressionCompletionKind.Variable, "$" + name, name,
                "Declared, type-compatible variable. Runtime availability and value conversion remain deferred.",
                ExpressionTypeInfo.ForClr(type, nullability, elementType));
        }

        internal void AddOperators()
        {
            if (!_context.IsAvailable || _context.Receiver == null ||
                !(_context.Slot == ExpressionCompletionSlot.Operator ||
                    _context.Slot == ExpressionCompletionSlot.FunctionOperator)) return;
            foreach (var op in CanonicalOperators)
            {
                if (!Visit()) return;
                if (_context.Slot == ExpressionCompletionSlot.FunctionOperator &&
                    !ExpressionGrammar.IsFunction(op) ||
                    !op.StartsWith(_context.CandidatePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var right = _context.SuffixOperand ?? ExpressionSemanticAnalyzer.SemanticNode.Value(
                    _context.Receiver.Type, _context.Receiver.Nullability,
                    _context.Edit.Start, _context.Edit.Start + _context.Edit.Length);
                if (_context.SuffixOperand == null &&
                    (op == Interpreter.ComparisonIn || op == Interpreter.ComparisonNotIn))
                    right = ExpressionSemanticAnalyzer.SemanticNode.List(right.Start, right.End,
                        Array.Empty<ExpressionSemanticAnalyzer.SemanticNode>(), right.Type);
                if (!_binder.TryDescribeBinary(op, _context.Receiver, right, out _)) continue;
                Add(ExpressionCompletionKind.Operator, _context.InsertionPrefix + op, op, string.Empty,
                    identity: op);
            }
        }

        internal void AddValues(ExpressionValueSuggestionSchema suggestions = null)
        {
            suggestions?.ValidateSchema(_schema);
            if (!_context.IsAvailable || _context.Receiver == null ||
                !(_context.Slot == ExpressionCompletionSlot.Operand ||
                    _context.Slot == ExpressionCompletionSlot.MembershipItem) ||
                _context.Edit.Kind == ExpressionTokenKind.Variable) return;
            var paths = _context.Receiver.EntityPaths;
            if (_context.Receiver.IsField && paths.Count == 1 && suggestions != null &&
                suggestions.Fields.TryGetValue(paths[0], out var hints))
            {
                foreach (var hint in hints)
                {
                    if (!Visit()) return;
                    AddValue(hint.Value, hint.Label, hint.Description);
                }
            }
            var type = Nullable.GetUnderlyingType(_context.Receiver.Type) ?? _context.Receiver.Type;
            if (type.IsEnum)
            {
                foreach (var name in PropertyMetadata.GetEnumNames(type))
                {
                    if (!Visit()) return;
                    AddValue(FilterValue.String(name), null, string.Empty);
                }
            }
            if (type == typeof(bool))
            {
                if (!Visit()) return;
                AddValue(FilterValue.Boolean(false), null, string.Empty);
                if (!Visit()) return;
                AddValue(FilterValue.Boolean(true), null, string.Empty);
            }
            if (!Visit()) return;
            AddValue(FilterValue.Null, null, string.Empty);
        }

        private void AddValue(FilterValue value, string label, string description)
        {
            var edit = _context.Edit;
            if (edit.IsQuoted && value.Kind != FilterValueKind.String) return;
            if (!ExpressionLiteralCodec.TryEncode(value, edit.IsQuoted ? edit.Quote : '\'',
                _context.Slot == ExpressionCompletionSlot.MembershipItem,
                out var text, out var literal, out var type)) return;
            var prefixText = value.Kind == FilterValueKind.String ? value.Text : text;
            if (!prefixText.StartsWith(edit.Prefix, StringComparison.OrdinalIgnoreCase)) return;
            var node = type == null ? ExpressionSemanticAnalyzer.SemanticNode.NullValue(edit.Start, edit.Start + edit.Length) :
                ExpressionSemanticAnalyzer.SemanticNode.Literal(type, edit.Start, edit.Start + edit.Length, literal);
            if (!Compatible(node)) return;
            var metadata = type == null ? ExpressionTypeInfo.Null() :
                ExpressionTypeInfo.ForClr(_context.Receiver.Type, node.Nullability);
            var identity = ExpressionLiteralCodec.TryEncode(value, '\'', false, out var canonical, out _, out _)
                ? canonical : text;
            if (value.Kind == FilterValueKind.Number &&
                decimal.TryParse(value.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number))
                identity = number.ToString("G29", CultureInfo.InvariantCulture);
            Add(ExpressionCompletionKind.Value, text, label ?? text, description, metadata,
                identity, matchSpelling: prefixText);
        }

        internal void AddFields()
        {
            if (!_context.IsAvailable || _context.Edit.IsQuoted ||
                _context.Edit.Kind == ExpressionTokenKind.Variable ||
                !(_context.Slot == ExpressionCompletionSlot.Condition ||
                    _context.Slot == ExpressionCompletionSlot.Operand ||
                    _context.Slot == ExpressionCompletionSlot.FunctionOperator && _context.AllowsPathFields))
                return;
            if (_schema.IsPublicSchema)
            {
                foreach (var field in _schema.Fields)
                {
                    if (!Visit()) return;
                    AddField(field.Name, field.DisplayName, field.Description);
                }
                return;
            }

            var allowlist = _queryContext?.CompletionValidProperties;
            if (allowlist == null || allowlist.Count == 0) allowlist = _schema.ValidProperties;
            if (allowlist.Count > 0)
            {
                foreach (var name in allowlist)
                {
                    if (!Visit()) return;
                    AddField(name, null, null);
                }
                return;
            }
            foreach (var mapping in _schema.PropertyMapping)
            {
                if (!Visit()) return;
                AddField(mapping.Key, null, null);
            }

            var prefix = _context.Edit.Prefix;
            var dot = prefix.LastIndexOf('.');
            var parent = dot < 0 ? string.Empty : prefix.Substring(0, dot);
            var parentType = _schema.EntityType;
            if (parent.Length > 0)
            {
                // Aliases bind exact paths, never a new traversable prefix.
                if (_schema.PropertyMapping.ContainsKey(parent) ||
                    !_schema.TryGetProperty(parent, out parentType, out _, out _))
                    return;
            }
            foreach (var member in PropertyMetadata.GetOrderedMemberTypes(parentType))
            {
                if (!Visit()) return;
                var path = parent.Length == 0 ? member.Key : parent + "." + member.Key;
                AddField(path + _context.Edit.PathSuffix, null, null);
            }
        }

        private void AddField(string name, string label, string description)
        {
            if (!MatchesPath(name, _context.Edit.Prefix, _context.Edit.PathSuffix) ||
                !(_queryContext?.IsPropertyQueryable(name) ?? _schema.IsPropertyQueryable(name)) ||
                !_schema.TryResolveQueryField(name, out var field) ||
                !QueryPolicyFieldMetadata.IsScalar(field.ClrType) ||
                _queryContext != null &&
                    (!_queryContext.IsNavigationAllowed(name, out _) || !_queryContext.IsCollectionAccessAllowed(name)))
                return;
            var permissions = _queryContext != null && _queryContext.TryGetAllowedOperators(name, out var operators)
                ? operators : field.AllowedOperators;
            if (permissions != null && permissions.Count == 0) return;
            var operand = ExpressionSemanticAnalyzer.SemanticNode.Value(
                field.ClrType, field.Nullability, _context.Edit.Start,
                _context.Edit.Start + _context.Edit.Length, entityPaths: new[] { name },
                isField: true, isBareBooleanPredicate: field.ClrType == typeof(bool));
            if (!Compatible(operand)) return;
            Add(ExpressionCompletionKind.Field, name, label ?? name, description ?? string.Empty,
                ExpressionTypeInfo.ForClr(field.ClrType, field.Nullability));
        }

        internal bool Visit()
        {
            if (DescriptorVisits >= ExpressionCompletionBudget.MaximumDescriptors)
            {
                IsTruncated = true;
                return false;
            }
            ++DescriptorVisits;
            return true;
        }

        private bool Compatible(ExpressionSemanticAnalyzer.SemanticNode operand)
        {
            if (_context.Receiver != null && _context.Operator != null)
            {
                if (_context.Slot == ExpressionCompletionSlot.MembershipItem)
                {
                    var list = ExpressionSemanticAnalyzer.SemanticNode.List(
                        operand.Start, operand.End, new[] { operand }, operand.Type);
                    if (!_binder.TryDescribeBinary(_context.Operator, _context.Receiver, list, out _)) return false;
                }
                else if (!_binder.TryDescribeBinary(_context.Operator, _context.Receiver, operand, out _)) return false;
            }
            return _context.SuffixOperator == null || _context.SuffixOperand == null ||
                _binder.TryDescribeBinary(_context.SuffixOperator, operand, _context.SuffixOperand, out _);
        }

        private void Add(ExpressionCompletionKind kind, string spelling, string label, string description,
            ExpressionTypeInfo typeInfo = null, string identity = null, string matchSpelling = null)
        {
            var edit = _context.Edit;
            var key = (int)kind + ":" + (identity ?? spelling) + ":" + edit.Start + ":" + edit.Length;
            if (!_identities.Add(key)) return;
            var insertion = spelling;
            if (NeedsSpaceBefore(spelling, edit.Start)) insertion = " " + insertion;
            if (NeedsSpaceAfter(spelling, edit.Start + edit.Length)) insertion += " ";
            if (_policy != null && !_policy.Allows(kind, insertion)) return;
            var item = new ExpressionCompletionItem(kind, label, insertion, edit.Start, edit.Length, description, typeInfo);
            _items.Add(item);
            _spellings.Add(item, spelling);
            var matching = matchSpelling ?? (kind == ExpressionCompletionKind.Variable ? spelling.TrimStart('$') :
                kind == ExpressionCompletionKind.Operator ? identity : spelling);
            var prefix = kind == ExpressionCompletionKind.Variable ? _context.CandidatePrefix.TrimStart('$') : _context.CandidatePrefix;
            if (string.Equals(matching, prefix, StringComparison.OrdinalIgnoreCase)) _exactPrefixes.Add(item);
        }

        private bool NeedsSpaceBefore(string text, int start) =>
            start > 0 && text.Length > 0 && !char.IsWhiteSpace(_source[start - 1]) &&
            (ExpressionGrammar.IsIdentifierPart(text[0]) || text[0] == '$') &&
            (ExpressionGrammar.IsIdentifierPart(_source[start - 1]) || ExpressionGrammar.IsQuote(_source[start - 1]) ||
                _source[start - 1] == ')' || _source[start - 1] == ']');

        private bool NeedsSpaceAfter(string text, int end) =>
            end < _source.Length && text.Length > 0 && !char.IsWhiteSpace(_source[end]) &&
            ExpressionGrammar.IsIdentifierPart(text[text.Length - 1]) &&
            ExpressionGrammar.IsIdentifierPart(_source[end]);

        private static bool MatchesPath(string name, string prefix, string suffix)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (suffix.Length == 0) return true;
            var dot = name.IndexOf('.', prefix.Length);
            return dot >= 0 && string.Equals(name.Substring(dot), suffix, StringComparison.OrdinalIgnoreCase);
        }

        internal ExpressionCompletionResult Shape(ExpressionCompletionOptions options = null)
        {
            if (!_context.IsAvailable)
                return new ExpressionCompletionResult(Array.Empty<ExpressionCompletionItem>(), new[]
                {
                    new ExpressionDiagnostic(ExpressionDiagnosticKind.Semantic, "completion-context-unavailable",
                        "The cursor context cannot be determined reliably.", _context.Edit.Position, 0)
                }, ExpressionCompletionStatus.ContextUnavailable);
            var maximum = options?.MaxResults ?? 50;
            var ordered = _items.OrderBy(item => item.Kind)
                .ThenBy(item => _exactPrefixes.Contains(item) ? 0 : 1)
                .ThenBy(item => item.Kind == ExpressionCompletionKind.Operator
                    ? OperatorRank(item.Label) : item.Kind == ExpressionCompletionKind.LogicalOperator
                        ? item.Label == "and" ? 0 : item.Label == "or" ? 1 : 2 : 0)
                .ThenBy(item => _spellings[item], StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => _spellings[item], StringComparer.Ordinal)
                .ToArray();
            return new ExpressionCompletionResult(ordered.Take(maximum), Array.Empty<ExpressionDiagnostic>(),
                ordered.Length == 0 ? ExpressionCompletionStatus.NoMatches : ExpressionCompletionStatus.Available,
                IsTruncated || ordered.Length > maximum);
        }

        private static int OperatorRank(string op)
        {
            switch (op)
            {
                case "=": return 0;
                case "!=": return 1;
                case ">": return 2;
                case ">=": return 3;
                case "<": return 4;
                case "<=": return 5;
                case "in": return 6;
                case "not in": return 7;
                default: return 8;
            }
        }
    }
}
