using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>A display-oriented function call projected from thread events.</summary>
public sealed record HpdAgentThreadToolCall(
    string CallId,
    string Name,
    string MessageId,
    JsonElement? Arguments,
    string? ResultText,
    bool? InformationalOnly);

/// <summary>A display-oriented message projected from committed thread events.</summary>
public sealed record HpdAgentThreadMessage(
    string Id,
    string Role,
    string Text,
    IReadOnlyList<JsonElement> Contents,
    string? ReasoningText,
    string Timestamp,
    IReadOnlyList<HpdAgentThreadToolCall> ToolCalls,
    string? AuthorName,
    Dictionary<string, JsonElement>? AdditionalProperties,
    string? Source,
    string? Visibility,
    string? Persistence);

/// <summary>
/// AOT-safe projection of stable HPD thread message event fields. Unknown
/// content remains available verbatim in <see cref="HpdAgentThreadMessage.Contents"/>.
/// </summary>
public static class HpdAgentThreadMessageProjector
{
    public static IReadOnlyList<HpdAgentThreadMessage> Project(
        IEnumerable<JsonElement> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var messages = new Dictionary<string, MessageState>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var raw in events)
        {
            var item = Unwrap(raw);
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var type = ReadString(item, "type");
            if (type is null)
                continue;

            if (type == "THREAD_MESSAGE_REPLACED")
            {
                ApplyReplacement(item, messages, order);
                continue;
            }

            if (type is not ("TEXT_MESSAGE_START" or "TEXT_DELTA" or
                "USER_MESSAGE" or "REASONING_MESSAGE_START" or
                "REASONING_DELTA" or "CONTENT_ADDED"))
            {
                continue;
            }

            var fallbackRole = type == "USER_MESSAGE" ? "user" : "assistant";
            var state = EnsureMessage(item, messages, order, fallbackRole);
            if (state is null)
                continue;
            ApplyPolicy(state, item);

            if (type == "USER_MESSAGE")
            {
                state.Role = "user";
                if (ReadString(item, "text") is { Length: > 0 } userText)
                    state.Contents.Add(Content("text", userText));
            }
            else if (type == "TEXT_DELTA" && ReadString(item, "text") is { Length: > 0 } text)
            {
                state.Contents.Add(Content("text", text));
            }
            else if (type == "REASONING_DELTA" && ReadString(item, "text") is { Length: > 0 } reasoning)
            {
                state.Contents.Add(Content("reasoning", reasoning));
            }
            else if (type == "CONTENT_ADDED" &&
                     item.TryGetProperty("content", out var content) &&
                     content.ValueKind == JsonValueKind.Object &&
                     content.TryGetProperty("$type", out var contentType) &&
                     contentType.ValueKind == JsonValueKind.String)
            {
                state.Contents.Add(content.Clone());
            }
        }

        return order.Select(id => ToReadModel(messages[id])).ToArray();
    }

    public static IReadOnlyList<HpdAgentThreadMessage> ProjectTranscript(
        IEnumerable<JsonElement> events) =>
        Project(events)
            .Where(static message =>
                !string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(message.Visibility, "Hidden", StringComparison.Ordinal))
            .ToArray();

    private static JsonElement Unwrap(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object &&
        raw.TryGetProperty("event", out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : raw;

    private static MessageState? EnsureMessage(
        JsonElement item,
        Dictionary<string, MessageState> messages,
        List<string> order,
        string fallbackRole)
    {
        var id = ReadString(item, "messageId");
        if (string.IsNullOrWhiteSpace(id))
            return null;
        if (messages.TryGetValue(id, out var existing))
        {
            if (ReadString(item, "role") is { Length: > 0 } role)
                existing.Role = role;
            ApplyMessageMetadata(existing, item);
            return existing;
        }

        var state = new MessageState(
            id,
            ReadString(item, "role") ?? fallbackRole,
            ReadString(item, "createdAt") ??
                ReadString(item, "timestamp") ??
                DateTimeOffset.UtcNow.ToString("O"),
            ReadString(item, "authorName"));
        ApplyMessageMetadata(state, item);
        messages.Add(id, state);
        order.Add(id);
        return state;
    }

    private static void ApplyMessageMetadata(MessageState state, JsonElement item)
    {
        if (item.TryGetProperty("additionalProperties", out var additional) &&
            additional.ValueKind == JsonValueKind.Object)
        {
            state.AdditionalProperties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in additional.EnumerateObject())
                state.AdditionalProperties[property.Name] = property.Value.Clone();
        }
        ApplyPolicy(state, item);
    }

    private static void ApplyPolicy(MessageState state, JsonElement item)
    {
        state.Source = ReadString(item, "source") ??
            ReadAdditionalString(state, "hpd.message.source") ??
            state.Source;
        state.Visibility = ReadString(item, "visibility") ??
            ReadAdditionalString(state, "hpd.message.visibility") ??
            state.Visibility;
        state.Persistence = ReadString(item, "persistence") ??
            ReadAdditionalString(state, "hpd.message.persistence") ??
            state.Persistence;
    }

    private static string? ReadAdditionalString(MessageState state, string name) =>
        state.AdditionalProperties is not null &&
        state.AdditionalProperties.TryGetValue(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void ApplyReplacement(
        JsonElement item,
        Dictionary<string, MessageState> messages,
        List<string> order)
    {
        if (!item.TryGetProperty("replacement", out var replacement) ||
            replacement.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var messageId = ReadString(item, "messageId");
        if (string.IsNullOrWhiteSpace(messageId) ||
            !string.Equals(ReadString(replacement, "messageId"), messageId, StringComparison.Ordinal))
        {
            return;
        }

        messages.TryGetValue(messageId, out var previous);
        if (previous is null)
            order.Add(messageId);
        var state = new MessageState(
            messageId,
            ReadString(replacement, "role") ?? "assistant",
            ReadString(replacement, "createdAt") ??
                ReadString(item, "timestamp") ??
                DateTimeOffset.UtcNow.ToString("O"),
            ReadString(replacement, "authorName"));
        state.AdditionalProperties = previous?.AdditionalProperties;
        state.Source = previous?.Source;
        state.Visibility = previous?.Visibility;
        state.Persistence = previous?.Persistence;
        ApplyMessageMetadata(state, replacement);
        if (replacement.TryGetProperty("contents", out var contents) &&
            contents.ValueKind == JsonValueKind.Array)
        {
            foreach (var content in contents.EnumerateArray())
            {
                if (content.ValueKind == JsonValueKind.Object)
                    state.Contents.Add(content.Clone());
            }
        }
        messages[messageId] = state;
    }

    private static HpdAgentThreadMessage ToReadModel(MessageState state)
    {
        var text = new System.Text.StringBuilder();
        var reasoning = new System.Text.StringBuilder();
        var toolCalls = new List<MutableToolCall>();
        foreach (var content in state.Contents)
        {
            var type = ReadString(content, "$type");
            if (type == "text")
            {
                text.Append(ReadString(content, "text"));
            }
            else if (type == "reasoning")
            {
                reasoning.Append(ReadString(content, "text"));
            }
            else if (type == "functionCall")
            {
                var callId = ReadString(content, "callId");
                var name = ReadString(content, "name");
                if (callId is not null && name is not null)
                {
                    toolCalls.Add(new MutableToolCall(
                        callId,
                        name,
                        content.TryGetProperty("arguments", out var arguments)
                            ? arguments.Clone()
                            : null,
                        ReadBool(content, "informationalOnly")));
                }
            }
            else if (type == "functionResult" &&
                     ReadString(content, "callId") is { } resultCallId &&
                     content.TryGetProperty("result", out var result))
            {
                var call = toolCalls.FirstOrDefault(candidate =>
                    string.Equals(candidate.CallId, resultCallId, StringComparison.Ordinal));
                if (call is not null)
                    call.ResultText = FormatPayload(result);
            }
        }

        return new HpdAgentThreadMessage(
            state.Id,
            state.Role,
            text.ToString(),
            state.Contents.ToArray(),
            reasoning.Length == 0 ? null : reasoning.ToString(),
            state.Timestamp,
            toolCalls.Select(call => new HpdAgentThreadToolCall(
                call.CallId,
                call.Name,
                state.Id,
                call.Arguments,
                call.ResultText,
                call.InformationalOnly)).ToArray(),
            state.AuthorName,
            state.AdditionalProperties,
            state.Source,
            state.Visibility,
            state.Persistence);
    }

    private static string? FormatPayload(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.String)
            return result.GetString();
        if (result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }
            if (result.TryGetProperty("json", out var json))
                return json.GetRawText();
            if (result.TryGetProperty("content", out var content))
                return content.GetRawText();
        }
        return result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : result.GetRawText();
    }

    private static JsonElement Content(string type, string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", type);
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private sealed class MessageState(
        string id,
        string role,
        string timestamp,
        string? authorName)
    {
        public string Id { get; } = id;
        public string Role { get; set; } = role;
        public string Timestamp { get; } = timestamp;
        public string? AuthorName { get; } = authorName;
        public List<JsonElement> Contents { get; } = [];
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
        public string? Source { get; set; }
        public string? Visibility { get; set; }
        public string? Persistence { get; set; }
    }

    private sealed class MutableToolCall(
        string callId,
        string name,
        JsonElement? arguments,
        bool? informationalOnly)
    {
        public string CallId { get; } = callId;
        public string Name { get; } = name;
        public JsonElement? Arguments { get; } = arguments;
        public bool? InformationalOnly { get; } = informationalOnly;
        public string? ResultText { get; set; }
    }
}
