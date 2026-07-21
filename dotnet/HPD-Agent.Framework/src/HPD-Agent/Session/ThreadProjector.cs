using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class ThreadProjector
{
    public static Thread Project(
        string sessionId,
        string threadId,
        IEnumerable<AgentEvent> events,
        ThreadProjectionPurpose purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        var orderedEvents = events.OrderBy(evt => evt.ThreadSequenceNumber).ToArray();
        var defaultAgentId = orderedEvents.OfType<ThreadCreatedEvent>().FirstOrDefault()?.DefaultAgentId
            ?? throw new InvalidOperationException($"Thread '{threadId}' has no creation event with a default agent.");
        var thread = new Thread(sessionId, threadId, defaultAgentId);
        Apply(thread, orderedEvents, purpose);
        return thread;
    }

    public static Thread Apply(
        Thread thread,
        IEnumerable<AgentEvent> events,
        ThreadProjectionPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(events);

        var messages = new Dictionary<string, MessageProjection>(StringComparer.Ordinal);
        var messageOrder = new List<string>();

        foreach (var message in thread.Messages)
        {
            EnsureMessageIdentity(message);
            var messageId = message.MessageId!;
            messages[messageId] = MessageProjection.FromChatMessage(message);
            messageOrder.Add(messageId);
        }

        foreach (var evt in events.OrderBy(e => e.ThreadSequenceNumber))
        {
            Apply(thread, evt, messages, messageOrder, purpose);
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
        ThreadProjectionPurpose purpose)
    {
        switch (evt)
        {
            case ThreadCreatedEvent data:
            {
                ApplyHeader(thread, data);
                thread.LastActivity = data.CreatedAt;
                break;
            }

            case ThreadUpdatedEvent data:
            {
                ApplyHeader(thread, data);
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ThreadExecutionStartedEvent:
            {
                if (thread.Kind == ThreadKind.SubAgent)
                    thread.SubAgentStatus = ThreadExecutionStatus.Active;
                break;
            }

            case ThreadExecutionFinishedEvent completed:
            {
                if (thread.Kind == ThreadKind.SubAgent)
                {
                    thread.SubAgentStatus = completed.Outcome switch
                    {
                        ThreadExecutionOutcome.Failed => ThreadExecutionStatus.Failed,
                        ThreadExecutionOutcome.Cancelled => ThreadExecutionStatus.Cancelled,
                        _ => ThreadExecutionStatus.Succeeded
                    };
                }
                break;
            }

            case ContentAddedEvent data:
            {
                if (data.Content is null)
                    return;
                GetOrStartMessage(
                        messages,
                        messageOrder,
                        data.MessageId,
                        ParseRole(data.Role),
                        data.AuthorName,
                        data.CreatedAt,
                        data.AdditionalProperties)
                    .SetPolicy(data.Source, data.Visibility, data.Persistence)
                    .SetMessageTurnId(evt.EventFlowId)
                    .Contents.Add(data.Content);
                thread.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case TextMessageStartEvent data:
            {
                GetOrStartMessage(
                        messages,
                        messageOrder,
                        data.MessageId,
                        ParseRole(data.Role),
                        data.AuthorName,
                        data.CreatedAt,
                        data.AdditionalProperties)
                    .SetPolicy(data.Source, data.Visibility, data.Persistence)
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
                break;
            }

            case ToolCallArgsEvent data:
            {
                break;
            }

            case ToolCallEndEvent data:
            {
                var args = DeserializeArguments(data.ArgsJson);
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .Contents.Add(new FunctionCallContent(data.CallId, data.Name, args));
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

            case ThreadHistoryCompactionCheckpointEvent data:
            {
                ApplyCompaction(data, messages, messageOrder, purpose);
                thread.LastActivity = data.CompactedAt.UtcDateTime;
                break;
            }
        }
    }

    private static void ApplyCompaction(
        ThreadHistoryCompactionCheckpointEvent data,
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder,
        ThreadProjectionPurpose purpose)
    {
        if (data.CommitMode == CompactionCommitMode.Hard ||
            purpose is ThreadProjectionPurpose.ThreadHistory or
                ThreadProjectionPurpose.ForkConstruction or
                ThreadProjectionPurpose.CompleteSemanticExport)
            return;

        var removed = data.CompactedMessageIds;

        var removedIds = removed
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        if (removedIds.Count == 0)
            return;

        var insertIndex = messageOrder.FindIndex(removedIds.Contains);
        if (insertIndex < 0)
            insertIndex = messageOrder.Count;

        messageOrder.RemoveAll(removedIds.Contains);
        foreach (var messageId in removedIds)
            messages.Remove(messageId);

        var replacementIds = new List<string>();
        foreach (var replacement in data.ReplacementMessages)
        {
            EnsureMessageIdentity(replacement);
            var replacementId = replacement.MessageId!;
            messageOrder.Remove(replacementId);
            replacementIds.Add(replacementId);
            messages[replacementId] = MessageProjection.FromChatMessage(replacement);
        }

        messageOrder.InsertRange(insertIndex, replacementIds);
    }

    private static void ApplyHeader(Thread thread, ThreadCreatedEvent data)
    {
        ApplyHeader(
            thread,
            data.DefaultAgentId,
            data.Name,
            data.Description,
            data.Tags,
            data.ThreadMetadata,
            data.ThreadKind,
            data.Visibility,
            data.ParentSessionId,
            data.ParentThreadId,
            data.SubAgentName,
            data.SubAgentTaskName,
            data.InvocationId,
            data.SubAgentSourceKind,
            data.ParentToolCallId,
            data.ContextPolicy,
            data.ForkedFrom,
            data.ForkedAtMessageId,
            data.ForkedAtMessageIndex,
            data.ChildThreads ?? [],
            data.Ancestors);
    }

    private static void ApplyHeader(Thread thread, ThreadUpdatedEvent data)
    {
        ApplyHeader(
            thread,
            data.DefaultAgentId,
            data.Name,
            data.Description,
            data.Tags,
            data.ThreadMetadata,
            data.ThreadKind,
            data.Visibility,
            data.ParentSessionId,
            data.ParentThreadId,
            data.SubAgentName,
            data.SubAgentTaskName,
            data.InvocationId,
            data.SubAgentSourceKind,
            data.ParentToolCallId,
            data.ContextPolicy,
            data.ForkedFrom,
            data.ForkedAtMessageId,
            data.ForkedAtMessageIndex,
            data.ChildThreads ?? [],
            data.Ancestors);
    }

    private static void ApplyHeader(
        Thread thread,
        string defaultAgentId,
        string? name,
        string? description,
        List<string>? tags,
        Dictionary<string, object>? threadMetadata,
        ThreadKind kind,
        ThreadVisibility visibility,
        string? parentSessionId,
        string? parentThreadId,
        string? subAgentName,
        string? subAgentTaskName,
        string? invocationId,
        string? subAgentSourceKind,
        string? parentToolCallId,
        string? contextPolicy,
        string? forkedFrom,
        string? forkedAtMessageId,
        int? forkedAtMessageIndex,
        List<string> childThreads,
        Dictionary<string, string>? ancestors)
    {
        thread.DefaultAgentId = defaultAgentId;
        thread.Name = name;
        thread.Description = description;
        thread.Tags = tags;
        ReplaceMetadata(thread.Metadata, threadMetadata);
        thread.Kind = kind;
        thread.Visibility = visibility;
        thread.ParentSessionId = parentSessionId;
        thread.ParentThreadId = parentThreadId;
        thread.SubAgentName = subAgentName;
        thread.SubAgentTaskName = subAgentTaskName;
        thread.InvocationId = invocationId;
        thread.SubAgentSourceKind = subAgentSourceKind;
        thread.ParentToolCallId = parentToolCallId;
        thread.ContextPolicy = contextPolicy;
        thread.SetForkMetadata(forkedFrom, forkedAtMessageId, forkedAtMessageIndex, ancestors);
        thread.SetTreeMetadata(forkedFrom, forkedAtMessageId, forkedAtMessageIndex, childThreads);
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

    private static MessageProjection GetOrStartMessage(
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder,
        string messageId,
        ChatRole role,
        string? authorName,
        DateTimeOffset? createdAt,
        AdditionalPropertiesDictionary? additionalProperties)
    {
        if (!messages.TryGetValue(messageId, out var projection))
        {
            projection = new MessageProjection(messageId, role, authorName, createdAt);
            messages[messageId] = projection;
            messageOrder.Add(messageId);
        }
        else
        {
            projection.AuthorName ??= authorName;
            projection.CreatedAt ??= createdAt;
        }

        projection.MergeAdditionalProperties(additionalProperties);
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

    private sealed class MessageProjection
    {
        public MessageProjection(
            string messageId,
            ChatRole role,
            string? authorName,
            DateTimeOffset? createdAt)
        {
            MessageId = messageId;
            Role = role;
            AuthorName = authorName;
            CreatedAt = createdAt;
        }

        public string MessageId { get; }
        public ChatRole Role { get; }
        public string? AuthorName { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public List<AIContent> Contents { get; } = [];
        public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

        public MessageProjection MergeAdditionalProperties(AdditionalPropertiesDictionary? additionalProperties)
        {
            if (additionalProperties is null)
                return this;

            AdditionalProperties ??= [];
            foreach (var (key, value) in additionalProperties)
                AdditionalProperties.TryAdd(key, value);

            return this;
        }

        public MessageProjection SetMessageTurnId(string? messageTurnId)
        {
            if (string.IsNullOrWhiteSpace(messageTurnId))
                return this;

            AdditionalProperties ??= [];
            AdditionalProperties["hpd.messageTurnId"] = messageTurnId;
            return this;
        }

        public MessageProjection SetPolicy(
            AgentMessageSource source,
            AgentMessageVisibility visibility,
            AgentMessagePersistence persistence)
        {
            AdditionalProperties ??= [];
            AdditionalProperties[AgentMessagePolicy.SourcePropertyName] = source.ToString();
            AdditionalProperties[AgentMessagePolicy.VisibilityPropertyName] = visibility.ToString();
            AdditionalProperties[AgentMessagePolicy.PersistencePropertyName] = persistence.ToString();
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
