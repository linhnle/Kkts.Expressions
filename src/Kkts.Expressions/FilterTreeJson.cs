using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kkts.Expressions.Internal;

namespace Kkts.Expressions
{
    /// <summary>Strict JSON serialization and deserialization for nested filter trees.</summary>
    public static class FilterTreeJson
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Decodes a filter tree, returning structured diagnostics for invalid input.</summary>
        public static FilterOperationResult<FilterNode> TryDeserialize(string json)
        {
            return TryDeserializeCore(json, null);
        }

        /// <summary>Decodes and validates a filter tree against a server-owned query context.</summary>
        public static FilterOperationResult<FilterNode> TryDeserialize(
            string json,
            ExpressionQueryContext queryContext,
            ExpressionVariableSchema variables = null)
        {
            if (queryContext == null) throw new ArgumentNullException(nameof(queryContext));
            var decoded = TryDeserializeCore(json, queryContext);
            if (!decoded.Succeeded) return decoded;
            return queryContext.ValidateFilterTree(decoded.Result, variables);
        }

        private static FilterOperationResult<FilterNode> TryDeserializeCore(
            string json,
            ExpressionQueryContext queryContext)
        {
            if (json == null)
                return FilterTreeJsonCodec.Failure(
                    "filter-tree-invalid-json",
                    "The JSON input cannot be null.",
                    string.Empty);

            try
            {
                var bytes = StrictUtf8.GetBytes(json);
                var reader = new Utf8JsonReader(
                    bytes,
                    new JsonReaderOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = int.MaxValue
                    });
                if (!reader.Read())
                    return FilterTreeJsonCodec.Failure(
                        "filter-tree-invalid-json",
                        "The JSON input is empty.",
                        string.Empty);

                var result = FilterTreeJsonCodec.Read(ref reader, queryContext);
                if (!result.Succeeded) return result;
                if (reader.Read())
                    return FilterTreeJsonCodec.Failure(
                        "filter-tree-invalid-json",
                        "The JSON input contains more than one root value.",
                        string.Empty);
                return result;
            }
            catch (EncoderFallbackException exception)
            {
                return FilterTreeJsonCodec.Failure(
                    "filter-tree-invalid-json",
                    "The JSON input contains invalid Unicode.",
                    string.Empty,
                    exception);
            }
            catch (JsonException exception)
            {
                return FilterTreeJsonCodec.Failure(
                    "filter-tree-invalid-json",
                    "The JSON input is malformed.",
                    string.Empty,
                    exception);
            }
        }

        /// <summary>Decodes a filter tree or throws <see cref="JsonException"/> when it is invalid.</summary>
        public static FilterNode Deserialize(string json)
        {
            var result = TryDeserialize(json);
            if (result.Succeeded) return result.Result;
            var diagnostic = result.Diagnostics[0];
            throw new JsonException(diagnostic.Code + " at " + diagnostic.InputPath + ": " + diagnostic.Message);
        }

        /// <summary>Serializes a filter tree using deterministic contract key ordering.</summary>
        public static string Serialize(FilterNode tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions { MaxDepth = int.MaxValue }))
                {
                    FilterTreeJsonCodec.Write(writer, tree);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    /// <summary>
    /// System.Text.Json converter for <see cref="FilterNode"/>. When used directly with
    /// <see cref="JsonSerializer"/>, its reader/writer depth is controlled by the serializer options.
    /// </summary>
    public sealed class FilterTreeJsonConverter : JsonConverter<FilterNode>
    {
        /// <inheritdoc />
        public override FilterNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = FilterTreeJsonCodec.Read(ref reader);
            if (result.Succeeded) return result.Result;
            var diagnostic = result.Diagnostics[0];
            throw new JsonException(diagnostic.Code + " at " + diagnostic.InputPath + ": " + diagnostic.Message);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, FilterNode value, JsonSerializerOptions options)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (value == null) throw new ArgumentNullException(nameof(value));
            FilterTreeJsonCodec.Write(writer, value);
        }
    }

    internal static class FilterTreeJsonCodec
    {
        internal static FilterOperationResult<FilterNode> Read(
            ref Utf8JsonReader reader,
            ExpressionQueryContext queryContext = null)
        {
            try
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw Error("filter-tree-invalid-shape", "A filter node must be a JSON object.", string.Empty);

                var policyExecution = queryContext == null
                    ? null
                    : new QueryPolicyExecution(queryContext.Policy);
                var frames = new Stack<ReadFrame>();
                frames.Push(new NodeFrame(string.Empty, 0));
                FilterNode root = null;
                while (frames.Count != 0)
                {
                    var frame = frames.Peek();
                    if (!frame.Started)
                    {
                        if (reader.TokenType != frame.StartToken)
                            throw Error("filter-tree-invalid-shape", "A filter node has the wrong JSON shape.", frame.Path);
                        frame.Started = true;
                    }
                    if (!reader.Read())
                        throw new JsonException("Unexpected end of JSON input.");

                    var nodeFrame = frame as NodeFrame;
                    if (nodeFrame != null)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            var name = reader.GetString();
                            if (!nodeFrame.SeenMembers.Add(name))
                                throw Error(
                                    "filter-tree-duplicate-member",
                                    "A filter node contains a duplicate member.",
                                    Pointer(nodeFrame.Path, name));
                            if (!IsNodeMember(name))
                                throw Error(
                                    "filter-tree-unknown-member",
                                    "A filter node contains an unknown member.",
                                    Pointer(nodeFrame.Path, name));
                            nodeFrame.CurrentMember = name;
                            nodeFrame.RecordMember(name);
                            continue;
                        }

                        if (reader.TokenType == JsonTokenType.EndObject)
                        {
                            frames.Pop();
                            var completed = nodeFrame.Build();
                            if (queryContext != null &&
                                completed.Kind == FilterNodeKind.Condition &&
                                !FilterTreePolicyScanner.TryScan(
                                    completed,
                                    queryContext,
                                    policyExecution,
                                    nodeFrame.Path,
                                    countMembershipItems: false))
                                throw new FilterTreeJsonPolicyException(policyExecution.Diagnostics.ToReadOnlyList());
                            if (frames.Count == 0)
                            {
                                root = completed;
                                continue;
                            }
                            Attach(completed, frames.Peek());
                            continue;
                        }

                        if (nodeFrame.CurrentMember == null)
                            throw Error("filter-tree-invalid-shape", "Unexpected JSON content in a filter node.", nodeFrame.Path);

                        var member = nodeFrame.CurrentMember;
                        nodeFrame.CurrentMember = null;
                        if (member == "and" || member == "or")
                        {
                            if (reader.TokenType != JsonTokenType.StartArray)
                                throw Error(
                                    "filter-tree-invalid-shape",
                                    "An AND or OR member must contain an array of filter nodes.",
                                    Pointer(nodeFrame.Path, member));
                            var logicalDepth = nodeFrame.LogicalDepth + 1;
                            var logicalPath = Pointer(nodeFrame.Path, member);
                            ValidateDecodeDepth(logicalDepth, logicalPath, queryContext, policyExecution);
                            frames.Push(new LogicalArrayFrame(
                                nodeFrame,
                                member,
                                logicalPath,
                                logicalDepth));
                            continue;
                        }
                        if (member == "not")
                        {
                            if (reader.TokenType != JsonTokenType.StartObject)
                                throw Error(
                                    "filter-tree-invalid-shape",
                                    "A NOT member must contain exactly one filter node object.",
                                    Pointer(nodeFrame.Path, member));
                            nodeFrame.HasNotChild = true;
                            var notDepth = nodeFrame.LogicalDepth + 1;
                            var notPath = Pointer(nodeFrame.Path, member);
                            ValidateDecodeDepth(notDepth, notPath, queryContext, policyExecution);
                            frames.Push(new NodeFrame(notPath, notDepth));
                            continue;
                        }
                        if (member == "field" || member == "op")
                        {
                            if (reader.TokenType != JsonTokenType.String)
                                throw Error(
                                    "filter-tree-invalid-shape",
                                    "Condition fields and operators must be strings.",
                                    Pointer(nodeFrame.Path, member));
                            var text = reader.GetString();
                            if (member == "field") nodeFrame.Field = text;
                            else nodeFrame.Operator = text;
                            continue;
                        }

                        nodeFrame.Value = ReadValue(
                            ref reader,
                            Pointer(nodeFrame.Path, "value"),
                            policyExecution);
                        continue;
                    }

                    var arrayFrame = (LogicalArrayFrame)frame;
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        frames.Push(new NodeFrame(
                            Pointer(arrayFrame.Path, arrayFrame.Children.Count.ToString()),
                            arrayFrame.LogicalDepth));
                    }
                    else if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        frames.Pop();
                        arrayFrame.Owner.Children = arrayFrame.Children;
                    }
                    else if (reader.TokenType == JsonTokenType.Null)
                    {
                        throw Error(
                            "filter-tree-null-child",
                            "Logical groups cannot contain null children.",
                            Pointer(arrayFrame.Path, arrayFrame.Children.Count.ToString()));
                    }
                    else
                    {
                        throw Error(
                            "filter-tree-invalid-shape",
                            "Logical groups can contain only filter node objects.",
                            Pointer(arrayFrame.Path, arrayFrame.Children.Count.ToString()));
                    }
                }

                if (root == null)
                    throw Error("filter-tree-invalid-shape", "The JSON input does not contain a filter node.", string.Empty);
                return FilterOperationResult<FilterNode>.Success(root);
            }
            catch (FilterTreeJsonDecodeException exception)
            {
                return Failure(exception.Code, exception.Message, exception.Path, exception);
            }
            catch (FilterTreeJsonPolicyException exception)
            {
                return Failure(exception.Diagnostics);
            }
            catch (JsonException exception)
            {
                return Failure(
                    "filter-tree-invalid-json",
                    "The JSON input is malformed.",
                    string.Empty,
                    exception);
            }
        }

        internal static void Write(Utf8JsonWriter writer, FilterNode root)
        {
            var actions = new Stack<WriteFrame>();
            actions.Push(WriteFrame.ForNode(root));
            while (actions.Count > 0)
            {
                var action = actions.Pop();
                if (action.EndArray)
                {
                    writer.WriteEndArray();
                    continue;
                }
                if (action.EndObject)
                {
                    writer.WriteEndObject();
                    continue;
                }

                var node = action.Node;
                switch (node.Kind)
                {
                    case FilterNodeKind.And:
                    case FilterNodeKind.Or:
                        var member = node.Kind == FilterNodeKind.And ? "and" : "or";
                        writer.WriteStartObject();
                        writer.WritePropertyName(member);
                        writer.WriteStartArray();
                        actions.Push(WriteFrame.ForEndObject());
                        actions.Push(WriteFrame.ForEndArray());
                        for (var index = node.Children.Count - 1; index >= 0; index--)
                            actions.Push(WriteFrame.ForNode(node.Children[index]));
                        break;
                    case FilterNodeKind.Not:
                        writer.WriteStartObject();
                        writer.WritePropertyName("not");
                        actions.Push(WriteFrame.ForEndObject());
                        actions.Push(WriteFrame.ForNode(node.Child));
                        break;
                    case FilterNodeKind.Condition:
                        writer.WriteStartObject();
                        writer.WriteString("field", node.Field);
                        writer.WriteString("op", node.Operator);
                        writer.WritePropertyName("value");
                        WriteValue(writer, node.Value);
                        writer.WriteEndObject();
                        break;
                    default:
                        throw new InvalidOperationException("Unknown filter node kind.");
                }
            }
        }

        internal static FilterOperationResult<FilterNode> Failure(
            string code,
            string message,
            string path,
            Exception exception = null)
        {
            var diagnostic = new ExpressionDiagnostic(
                ExpressionDiagnosticKind.Semantic,
                code,
                message,
                0,
                0,
                inputPath: path);
            return FilterOperationResult<FilterNode>.Failure(new[] { diagnostic });
        }

        internal static FilterOperationResult<FilterNode> Failure(
            IEnumerable<ExpressionDiagnostic> diagnostics)
        {
            return FilterOperationResult<FilterNode>.Failure(diagnostics);
        }

        private static void ValidateDecodeDepth(
            int depth,
            string path,
            ExpressionQueryContext queryContext,
            QueryPolicyExecution policyExecution)
        {
            if (queryContext == null ||
                !queryContext.Policy.MaxFilterTreeDepth.HasValue ||
                depth <= queryContext.Policy.MaxFilterTreeDepth.Value)
                return;

            policyExecution.Diagnostics.Add(
                "query-policy-filter-tree-depth-exceeded",
                "The filter tree exceeds the configured logical depth limit.",
                0,
                0,
                queryContext.Policy.MaxFilterTreeDepth.Value,
                depth,
                inputPath: path);
            throw new FilterTreeJsonPolicyException(policyExecution.Diagnostics.ToReadOnlyList());
        }

        private static FilterValue ReadValue(
            ref Utf8JsonReader reader,
            string path,
            QueryPolicyExecution policyExecution)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return FilterValue.Null;
                case JsonTokenType.True:
                    return FilterValue.Boolean(true);
                case JsonTokenType.False:
                    return FilterValue.Boolean(false);
                case JsonTokenType.String:
                    return FilterValue.String(reader.GetString());
                case JsonTokenType.Number:
                    return FilterValue.Number(ReadRawNumber(ref reader));
                case JsonTokenType.StartArray:
                    return ReadCollection(ref reader, path, policyExecution);
                case JsonTokenType.StartObject:
                    return ReadVariable(ref reader, path);
                default:
                    throw Error("filter-tree-invalid-value", "A filter value has an unsupported JSON shape.", path);
            }
        }

        private static FilterValue ReadCollection(
            ref Utf8JsonReader reader,
            string path,
            QueryPolicyExecution policyExecution)
        {
            var items = new List<FilterValue>();
            var membershipCounter = policyExecution?.CreateMembershipCounter();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return FilterValue.Collection(items);
                if (reader.TokenType == JsonTokenType.StartArray)
                    throw Error(
                        "filter-tree-invalid-value",
                        "Membership arrays cannot contain nested arrays.",
                        Pointer(path, items.Count.ToString()));
                var itemPath = Pointer(path, items.Count.ToString());
                if (policyExecution != null &&
                    policyExecution.Policy.MaxInItems.HasValue &&
                    !policyExecution.TryCountMembershipItem(membershipCounter, 0, 0, itemPath))
                    throw new FilterTreeJsonPolicyException(policyExecution.Diagnostics.ToReadOnlyList());
                if (reader.TokenType == JsonTokenType.StartObject)
                    items.Add(ReadVariable(ref reader, itemPath));
                else
                    items.Add(ReadScalar(ref reader, itemPath));
            }
            throw new JsonException("Unexpected end of membership array.");
        }

        private static FilterValue ReadScalar(ref Utf8JsonReader reader, string path)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return FilterValue.Null;
                case JsonTokenType.True:
                    return FilterValue.Boolean(true);
                case JsonTokenType.False:
                    return FilterValue.Boolean(false);
                case JsonTokenType.String:
                    return FilterValue.String(reader.GetString());
                case JsonTokenType.Number:
                    return FilterValue.Number(ReadRawNumber(ref reader));
                default:
                    throw Error(
                        "filter-tree-invalid-value",
                        "Membership arrays can contain only scalar values or variable references.",
                        path);
            }
        }

        private static FilterValue ReadVariable(ref Utf8JsonReader reader, string path)
        {
            var seenVariable = false;
            var seenMembers = new HashSet<string>(StringComparer.Ordinal);
            string name = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    if (!seenVariable)
                        throw Error(
                            "filter-tree-invalid-value",
                            "A value object must contain exactly one variable member.",
                            path);
                    try
                    {
                        return FilterValue.Variable(name);
                    }
                    catch (ArgumentException)
                    {
                        throw Error(
                            "filter-tree-invalid-value",
                            "A variable reference must use a non-empty unprefixed dotted path.",
                            Pointer(path, "variable"));
                    }
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw Error("filter-tree-invalid-value", "A value object must contain a variable member.", path);
                var member = reader.GetString();
                if (!seenMembers.Add(member))
                    throw Error(
                        "filter-tree-duplicate-member",
                        "A value object contains a duplicate member.",
                        Pointer(path, member));
                if (member != "variable")
                    throw Error(
                        "filter-tree-invalid-value",
                        "A value object may contain only the variable member.",
                        Pointer(path, member));
                seenVariable = true;
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                    throw Error(
                        "filter-tree-invalid-value",
                        "A variable reference name must be a string.",
                        Pointer(path, "variable"));
                name = reader.GetString();
            }
            throw new JsonException("Unexpected end of variable reference object.");
        }

        private static string ReadRawNumber(ref Utf8JsonReader reader)
        {
            return reader.HasValueSequence
                ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                : Encoding.UTF8.GetString(reader.ValueSpan.ToArray());
        }

        private static void WriteValue(Utf8JsonWriter writer, FilterValue value)
        {
            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    writer.WriteNullValue();
                    break;
                case FilterValueKind.Boolean:
                    writer.WriteBooleanValue(value.BooleanValue);
                    break;
                case FilterValueKind.Number:
                    writer.WriteRawValue(value.Text, skipInputValidation: false);
                    break;
                case FilterValueKind.String:
                    writer.WriteStringValue(value.Text);
                    break;
                case FilterValueKind.Variable:
                    writer.WriteStartObject();
                    writer.WriteString("variable", value.Text);
                    writer.WriteEndObject();
                    break;
                case FilterValueKind.Collection:
                    writer.WriteStartArray();
                    foreach (var item in value.Items) WriteValue(writer, item);
                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidOperationException("Unknown filter value kind.");
            }
        }

        private static void Attach(FilterNode child, ReadFrame parent)
        {
            var array = parent as LogicalArrayFrame;
            if (array != null)
            {
                array.Children.Add(child);
                return;
            }
            var node = parent as NodeFrame;
            if (node != null && node.HasNotChild && node.NotChild == null)
            {
                node.NotChild = child;
                return;
            }
            throw Error("filter-tree-invalid-shape", "A filter node is not in a valid parent position.", child == null ? string.Empty : node.Path);
        }

        private static bool IsNodeMember(string member) =>
            member == "and" || member == "or" || member == "not" ||
            member == "field" || member == "op" || member == "value";

        private static FilterTreeJsonDecodeException Error(string code, string message, string path) =>
            new FilterTreeJsonDecodeException(code, message, path);

        private static string Pointer(string parent, string member)
            => FilterTreeDiagnosticProjection.AppendPointer(parent, member);

        private abstract class ReadFrame
        {
            protected ReadFrame(string path, JsonTokenType startToken)
            {
                Path = path;
                StartToken = startToken;
            }

            internal string Path { get; }
            internal JsonTokenType StartToken { get; }
            internal bool Started { get; set; }
        }

        private sealed class NodeFrame : ReadFrame
        {
            internal NodeFrame(string path, int logicalDepth) : base(path, JsonTokenType.StartObject)
            {
                LogicalDepth = logicalDepth;
            }

            internal int LogicalDepth { get; }
            internal HashSet<string> SeenMembers { get; } = new HashSet<string>(StringComparer.Ordinal);
            internal string CurrentMember { get; set; }
            internal bool HasAnd { get; private set; }
            internal bool HasOr { get; private set; }
            internal bool HasNot { get; private set; }
            internal bool HasField { get; private set; }
            internal bool HasOperator { get; private set; }
            internal bool HasValue { get; private set; }
            internal bool HasNotChild { get; set; }
            internal string Field { get; set; }
            internal string Operator { get; set; }
            internal FilterValue Value { get; set; }
            internal FilterNode NotChild { get; set; }
            internal List<FilterNode> Children { get; set; }

            internal void RecordMember(string name)
            {
                if (name == "and") HasAnd = true;
                else if (name == "or") HasOr = true;
                else if (name == "not") HasNot = true;
                else if (name == "field") HasField = true;
                else if (name == "op") HasOperator = true;
                else if (name == "value") HasValue = true;
            }

            internal FilterNode Build()
            {
                var logicalCount = (HasAnd ? 1 : 0) + (HasOr ? 1 : 0) + (HasNot ? 1 : 0);
                var conditionMemberCount = (HasField ? 1 : 0) + (HasOperator ? 1 : 0) + (HasValue ? 1 : 0);
                if (logicalCount > 1 || logicalCount > 0 && conditionMemberCount > 0)
                    throw Error(
                        "filter-tree-invalid-shape",
                        "A filter node must use exactly one logical or condition shape.",
                        Path);
                if (logicalCount == 1)
                {
                    if (HasAnd || HasOr)
                    {
                        if (Children == null)
                            throw Error("filter-tree-invalid-shape", "A logical child list is required.", Path);
                        if (Children.Count == 0)
                            throw Error("filter-tree-empty-group", "AND and OR groups cannot be empty.", Pointer(Path, HasAnd ? "and" : "or"));
                        return HasAnd ? FilterNode.And(Children) : FilterNode.Or(Children);
                    }
                    if (!HasNotChild || NotChild == null)
                        throw Error("filter-tree-invalid-shape", "A NOT node must contain exactly one child.", Pointer(Path, "not"));
                    return FilterNode.Not(NotChild);
                }
                if (conditionMemberCount == 0)
                    throw Error("filter-tree-invalid-shape", "A filter node must contain one valid node shape.", Path);
                if (!HasField)
                    throw Error("filter-tree-invalid-shape", "A condition requires a field member.", Pointer(Path, "field"));
                if (!HasOperator)
                    throw Error("filter-tree-invalid-shape", "A condition requires an op member.", Pointer(Path, "op"));
                if (!HasValue)
                    throw Error("filter-tree-invalid-shape", "A condition requires a value member.", Pointer(Path, "value"));
                if (string.IsNullOrWhiteSpace(Field))
                    throw Error("filter-tree-invalid-shape", "A condition field must be nonblank.", Pointer(Path, "field"));
                if (string.IsNullOrWhiteSpace(Operator))
                    throw Error("filter-tree-invalid-shape", "A condition operator must be nonblank.", Pointer(Path, "op"));
                var normalized = Interpreter.NormalizeComparisonOperator(Operator.Trim());
                if (!Interpreter.ComparisonOperators.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    throw Error("filter-tree-unknown-operator", "The condition operator is not supported.", Pointer(Path, "op"));
                var membership = Interpreter.IsMembership(normalized);
                if (Value.Kind == FilterValueKind.Collection && !membership)
                    throw Error("filter-tree-invalid-value", "Arrays are valid only for IN and NOT IN conditions.", Pointer(Path, "value"));
                return FilterNode.Condition(Field, normalized, Value);
            }
        }

        private sealed class LogicalArrayFrame : ReadFrame
        {
            internal LogicalArrayFrame(NodeFrame owner, string member, string path, int logicalDepth)
                : base(path, JsonTokenType.StartArray)
            {
                Owner = owner;
                Member = member;
                LogicalDepth = logicalDepth;
            }

            internal NodeFrame Owner { get; }
            internal string Member { get; }
            internal int LogicalDepth { get; }
            internal List<FilterNode> Children { get; } = new List<FilterNode>();
        }

        private sealed class WriteFrame
        {
            private WriteFrame(FilterNode node, bool endObject, bool endArray)
            {
                Node = node;
                EndObject = endObject;
                EndArray = endArray;
            }

            internal FilterNode Node { get; }
            internal bool EndObject { get; }
            internal bool EndArray { get; }

            internal static WriteFrame ForNode(FilterNode node) => new WriteFrame(node, false, false);
            internal static WriteFrame ForEndObject() => new WriteFrame(null, true, false);
            internal static WriteFrame ForEndArray() => new WriteFrame(null, false, true);
        }

        private sealed class FilterTreeJsonDecodeException : Exception
        {
            internal FilterTreeJsonDecodeException(string code, string message, string path) : base(message)
            {
                Code = code;
                Path = path;
            }

            internal string Code { get; }
            internal string Path { get; }
        }

        private sealed class FilterTreeJsonPolicyException : Exception
        {
            internal FilterTreeJsonPolicyException(IEnumerable<ExpressionDiagnostic> diagnostics)
                : base("The filter tree violates the configured query policy.")
            {
                Diagnostics = Array.AsReadOnly(new List<ExpressionDiagnostic>(diagnostics).ToArray());
            }

            internal IReadOnlyList<ExpressionDiagnostic> Diagnostics { get; }
        }
    }
}
