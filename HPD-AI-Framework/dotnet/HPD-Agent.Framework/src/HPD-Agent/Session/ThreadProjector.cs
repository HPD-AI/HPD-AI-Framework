using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class ThreadProjector
{
    public static Thread Project(ThreadEventDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Project(document.SessionId, document.ThreadId, document.Events);
    }

    public static Thread Project(string sessionId, string threadId, IEnumerable<AgentEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var thread = new Thread(sessionId, threadId);
        Apply(thread, events);
        return thread;
    }

    public static Thread Apply(Thread thread, IEnumerable<AgentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(events);

        var messages = new Dictionary<string, MessageProjection>(StringComparer.Ordinal);
        var messageOrder = new List<string>();
        var toolCalls = new Dictionary<string, ToolCallProjection>(StringComparer.Ordinal);

        foreach (var message in thread.Messages)
        {
            EnsureMessageIdentity(message);
            var messageId = message.MessageId!;
            messages[messageId] = MessageProjection.FromChatMessage(message);
            messageOrder.Add(messageId);
        }

        foreach (var evt in events.OrderBy(e => e.SequenceNumber))
        {
            Apply(thread, evt, messages, messageOrder, toolCalls);
        }

        thread.Messages.Clear();
        foreach (var messageId in messageOrder)
        {
            if (messages.TryGetValue(messageId, out var projection))
                thread.Messages.Add(projection.ToChatMessage());
        }

        return thread;
    }

    private static void Apply(
        Thread thread,
        AgentEvent evt,
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder,
        Dictionary<string, ToolCallProjection> toolCalls)
    {
        switch (evt)
        {
            case ThreadCreatedEvent data:
            {
                thread.Name = data.Name;
                thread.Description = data.Description;
                thread.Tags = data.Tags;
                ReplaceMetadata(thread.Metadata, data.ThreadMetadata);
                ApplyHeader(thread, data.ThreadKind, data.Visibility, data.ParentSessionId, data.ParentThreadId,
                    data.SubAgentName, data.SubAgentRunId, data.SubAgentSourceKind, data.ParentToolCallId,
                    data.SessionPolicy, data.ThreadPolicy);
                thread.LastActivity = data.CreatedAt;
                break;
            }

            case ThreadForkedEvent data:
            {
                thread.SetForkMetadata(
                    data.SourceThreadId,
                    data.FromMessageId,
                    data.ResolvedMessageIndex,
                    data.Ancestors);
                break;
            }

            case ThreadMetadataUpdatedEvent data:
            {
                thread.Name = data.Name;
                thread.Description = data.Description;
                thread.Tags = data.Tags;
                ReplaceMetadata(thread.Metadata, data.ThreadMetadata);
                ApplyHeader(thread, data.ThreadKind, data.Visibility, data.ParentSessionId, data.ParentThreadId,
                    data.SubAgentName, data.SubAgentRunId, data.SubAgentSourceKind, data.ParentToolCallId,
                    data.SessionPolicy, data.ThreadPolicy);
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ThreadTreeUpdatedEvent data:
            {
                thread.SetTreeMetadata(
                    data.ForkedFrom,
                    data.ForkedAtMessageId,
                    data.ForkedAtMessageIndex,
                    data.SiblingIndex,
                    data.TotalSiblings,
                    data.IsOriginal,
                    data.OriginalThreadId,
                    data.PreviousSiblingId,
                    data.NextSiblingId,
                    data.ChildThreads.ToList());
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case MessageStartedEvent data:
            {
                if (string.IsNullOrWhiteSpace(data.MessageId))
                    return;

                var projection = new MessageProjection(
                    data.MessageId,
                    ParseRole(data.Role),
                    data.AuthorName,
                    data.CreatedAt);

                if (messages.TryGetValue(data.MessageId, out var existing))
                {
                    projection.Contents.AddRange(existing.Contents);
                    projection.AdditionalProperties = existing.AdditionalProperties?.Clone();
                }

                if (!messages.ContainsKey(data.MessageId))
                    messageOrder.Add(data.MessageId);

                messages[data.MessageId] = projection;
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ContentAddedEvent data:
            {
                if (data.Content is null)
                    return;
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .Contents.Add(data.Content);
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case TextMessageStartEvent data:
            {
                GetMessage(messages, messageOrder, data.MessageId, ParseRole(data.Role))
                    .SetMessageTurnId(evt.EventFlowId);
                break;
            }

            case TextDeltaEvent data:
            {
                AddTextContent(
                    GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant),
                    data.Text);
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ReasoningMessageStartEvent data:
            {
                GetMessage(messages, messageOrder, data.MessageId, ParseRole(data.Role))
                    .SetMessageTurnId(evt.EventFlowId);
                break;
            }

            case ReasoningDeltaEvent data:
            {
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .Contents.Add(new TextReasoningContent(data.Text)
                    {
                        ProtectedData = data.ProtectedData
                    });
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ToolCallStartEvent data:
            {
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .SetMessageTurnId(evt.EventFlowId);
                toolCalls[data.CallId] = new ToolCallProjection(data.MessageId, data.Name);
                break;
            }

            case ToolCallArgsEvent data:
            {
                if (toolCalls.TryGetValue(data.CallId, out var call))
                    call.ArgsJson = data.ArgsJson;
                break;
            }

            case ToolCallEndEvent data:
            {
                if (!toolCalls.TryGetValue(data.CallId, out var call))
                    return;

                var args = DeserializeArguments(call.ArgsJson);
                GetMessage(messages, messageOrder, call.MessageId, ChatRole.Assistant)
                    .Contents.Add(new FunctionCallContent(data.CallId, call.Name, args));
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ToolCallResultEvent data:
            {
                var messageId = string.IsNullOrWhiteSpace(data.MessageId)
                    ? $"tool-{data.CallId}"
                    : data.MessageId;
                GetMessage(messages, messageOrder, messageId, ChatRole.Tool)
                    .SetMessageTurnId(evt.EventFlowId)
                    .Contents.Add(new FunctionResultContent(data.CallId, ToResultObject(data.Result)));
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ThreadMiddlewareStateCommittedEvent data:
            {
                if (data.State is null)
                    return;
                thread.MiddlewareState.Clear();
                foreach (var (key, value) in data.State)
                    thread.MiddlewareState[key] = value;
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ThreadHistoryCompactedEvent data:
            {
                ApplyCompaction(data, messages, messageOrder);
                thread.LastActivity = data.CompactedAt.UtcDateTime;
                break;
            }
        }
    }

    private static void ApplyCompaction(
        ThreadHistoryCompactedEvent data,
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder)
    {
        var durableRemoved = data.DurableCompactedMessageIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        if (durableRemoved.Count == 0)
            return;

        var insertIndex = messageOrder.FindIndex(durableRemoved.Contains);
        if (insertIndex < 0)
            insertIndex = messageOrder.Count;

        messageOrder.RemoveAll(durableRemoved.Contains);
        foreach (var messageId in durableRemoved)
            messages.Remove(messageId);

        var replacementIds = new List<string>();
        foreach (var replacement in data.ReplacementMessages)
        {
            EnsureMessageIdentity(replacement);
            var replacementId = replacement.MessageId!;
            replacementIds.Add(replacementId);
            messages[replacementId] = MessageProjection.FromChatMessage(replacement);
        }

        messageOrder.InsertRange(insertIndex, replacementIds);
    }

    private static void ApplyHeader(
        Thread thread,
        ThreadKind kind,
        ThreadVisibility visibility,
        string? parentSessionId,
        string? parentThreadId,
        string? subAgentName,
        string? subAgentRunId,
        string? subAgentSourceKind,
        string? parentToolCallId,
        string? sessionPolicy,
        string? threadPolicy)
    {
        thread.Kind = kind;
        thread.Visibility = visibility;
        thread.ParentSessionId = parentSessionId;
        thread.ParentThreadId = parentThreadId;
        thread.SubAgentName = subAgentName;
        thread.SubAgentRunId = subAgentRunId;
        thread.SubAgentSourceKind = subAgentSourceKind;
        thread.ParentToolCallId = parentToolCallId;
        thread.SessionPolicy = sessionPolicy;
        thread.ThreadPolicy = threadPolicy;
    }

    private static void ReplaceMetadata(
        Dictionary<string, object> target,
        Dictionary<string, object>? source)
    {
        target.Clear();
        if (source == null)
            return;

        foreach (var (key, value) in source)
            target[key] = value;
    }

    private static MessageProjection GetMessage(
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder,
        string messageId,
        ChatRole role)
    {
        if (messages.TryGetValue(messageId, out var projection))
            return projection;

        projection = new MessageProjection(messageId, role, null, null);
        messages[messageId] = projection;
        messageOrder.Add(messageId);
        return projection;
    }

    private static ChatRole ParseRole(string? role)
    {
        if (string.Equals(role, ChatRole.System.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.System;
        if (string.Equals(role, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.User;
        if (string.Equals(role, ChatRole.Assistant.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.Assistant;
        if (string.Equals(role, ChatRole.Tool.Value, StringComparison.OrdinalIgnoreCase))
            return ChatRole.Tool;

        return new ChatRole(role ?? ChatRole.Assistant.Value);
    }

    private static IDictionary<string, object?>? DeserializeArguments(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            argsJson,
            SessionJsonContext.Combined.Options);
    }

    private static object? ToResultObject(ToolResultPayload payload)
    {
        if (payload.Json is { } json)
            return json.Clone();

        return payload.Text;
    }

    private sealed record ToolCallProjection(string MessageId, string Name)
    {
        public string? ArgsJson { get; set; }
    }

    private sealed record MessageProjection(
        string MessageId,
        ChatRole Role,
        string? AuthorName,
        DateTimeOffset? CreatedAt)
    {
        public List<AIContent> Contents { get; } = [];
        public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

        public MessageProjection SetMessageTurnId(string? messageTurnId)
        {
            if (string.IsNullOrWhiteSpace(messageTurnId))
                return this;

            AdditionalProperties ??= [];
            AdditionalProperties[ThreadHistoryCompactionMetadata.MessageTurnIdPropertyName] = messageTurnId;
            return this;
        }

        public ChatMessage ToChatMessage()
        {
            return new ChatMessage(Role, ThreadMessageEventConverter.CoalesceTextContents(Contents))
            {
                MessageId = MessageId,
                AuthorName = AuthorName,
                CreatedAt = CreatedAt,
                AdditionalProperties = AdditionalProperties?.Clone()
            };
        }

        public static MessageProjection FromChatMessage(ChatMessage message)
        {
            var projection = new MessageProjection(
                message.MessageId ?? Guid.NewGuid().ToString(),
                message.Role,
                message.AuthorName,
                message.CreatedAt);

            projection.Contents.AddRange(message.Contents);
            projection.AdditionalProperties = message.AdditionalProperties?.Clone();
            return projection;
        }
    }

    private static void AddTextContent(MessageProjection projection, string text)
    {
        var content = new TextContent(text);
        if (projection.Role == ChatRole.User &&
            projection.Contents.Count > 0 &&
            !projection.Contents.OfType<TextContent>().Any())
        {
            projection.Contents.Insert(0, content);
            return;
        }

        projection.Contents.Add(content);
    }

    private static void EnsureMessageIdentity(ChatMessage message)
    {
        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;
    }
}
