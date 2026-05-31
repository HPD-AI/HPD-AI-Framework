using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class BranchProjector
{
    public static Branch Project(BranchEventDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Project(document.SessionId, document.BranchId, document.Events);
    }

    public static Branch Project(string sessionId, string branchId, IEnumerable<AgentEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);

        var branch = new Branch(sessionId, branchId);
        var messages = new Dictionary<string, MessageProjection>(StringComparer.Ordinal);
        var messageOrder = new List<string>();
        var toolCalls = new Dictionary<string, ToolCallProjection>(StringComparer.Ordinal);

        foreach (var evt in events.OrderBy(e => e.SequenceNumber))
        {
            Apply(branch, evt, messages, messageOrder, toolCalls);
        }

        branch.Messages.Clear();
        foreach (var messageId in messageOrder)
        {
            if (messages.TryGetValue(messageId, out var projection))
                branch.Messages.Add(projection.ToChatMessage());
        }

        return branch;
    }

    private static void Apply(
        Branch branch,
        AgentEvent evt,
        Dictionary<string, MessageProjection> messages,
        List<string> messageOrder,
        Dictionary<string, ToolCallProjection> toolCalls)
    {
        switch (evt)
        {
            case BranchCreatedEvent data:
            {
                branch.Name = data.Name;
                branch.Description = data.Description;
                branch.Tags = data.Tags;
                ReplaceMetadata(branch.Metadata, data.BranchMetadata);
                branch.LastActivity = data.CreatedAt;
                break;
            }

            case BranchForkedEvent data:
            {
                branch.SetForkMetadata(
                    data.SourceBranchId,
                    data.FromMessageId,
                    data.ResolvedMessageIndex,
                    data.Ancestors);
                break;
            }

            case BranchMetadataUpdatedEvent data:
            {
                branch.Name = data.Name;
                branch.Description = data.Description;
                branch.Tags = data.Tags;
                ReplaceMetadata(branch.Metadata, data.BranchMetadata);
                branch.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case BranchTreeUpdatedEvent data:
            {
                branch.SetTreeMetadata(
                    data.ForkedFrom,
                    data.ForkedAtMessageId,
                    data.ForkedAtMessageIndex,
                    data.SiblingIndex,
                    data.TotalSiblings,
                    data.IsOriginal,
                    data.OriginalBranchId,
                    data.PreviousSiblingId,
                    data.NextSiblingId,
                    data.ChildBranches.ToList());
                branch.LastActivity = evt.Timestamp.UtcDateTime;
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

                if (!messages.ContainsKey(data.MessageId))
                    messageOrder.Add(data.MessageId);

                messages[data.MessageId] = projection;
                branch.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case ContentAddedEvent data:
            {
                if (data.Content is null)
                    return;
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .Contents.Add(data.Content);
                branch.LastActivity = evt.Timestamp.UtcDateTime;
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
                GetMessage(messages, messageOrder, data.MessageId, ChatRole.Assistant)
                    .Contents.Add(new TextContent(data.Text));
                branch.LastActivity = evt.Timestamp.UtcDateTime;
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
                branch.LastActivity = evt.Timestamp.UtcDateTime;
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
                branch.LastActivity = evt.Timestamp.UtcDateTime;
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
                branch.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case BranchMiddlewareStateCommittedEvent data:
            {
                if (data.State is null)
                    return;
                branch.MiddlewareState.Clear();
                foreach (var (key, value) in data.State)
                    branch.MiddlewareState[key] = value;
                branch.LastActivity = evt.Timestamp.UtcDateTime;
                break;
            }

            case BranchHistoryCompactedEvent data:
            {
                ApplyCompaction(data, messages, messageOrder);
                branch.LastActivity = data.CompactedAt.UtcDateTime;
                break;
            }
        }
    }

    private static void ApplyCompaction(
        BranchHistoryCompactedEvent data,
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
        public AdditionalPropertiesDictionary? AdditionalProperties { get; private set; }

        public MessageProjection SetMessageTurnId(string? messageTurnId)
        {
            if (string.IsNullOrWhiteSpace(messageTurnId))
                return this;

            AdditionalProperties ??= [];
            AdditionalProperties[BranchHistoryCompactionMetadata.MessageTurnIdPropertyName] = messageTurnId;
            return this;
        }

        public ChatMessage ToChatMessage()
        {
            return new ChatMessage(Role, BranchMessageEventConverter.CoalesceTextContents(Contents))
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

    private static void EnsureMessageIdentity(ChatMessage message)
    {
        message.MessageId ??= Guid.NewGuid().ToString();
        message.CreatedAt ??= DateTimeOffset.UtcNow;
    }
}
