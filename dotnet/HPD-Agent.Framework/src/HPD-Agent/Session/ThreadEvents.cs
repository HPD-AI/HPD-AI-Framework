using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class ThreadEventTypes
{
    public const string ThreadCreated = "THREAD_CREATED";
    public const string ThreadForked = "THREAD_FORKED";
    public const string ThreadMetadataUpdated = "THREAD_METADATA_UPDATED";
    public const string ThreadTreeUpdated = "THREAD_TREE_UPDATED";
    public const string MessageStarted = "MESSAGE_STARTED";
    public const string MessageCompleted = "MESSAGE_COMPLETED";
    public const string ContentAdded = "CONTENT_ADDED";
    public const string ThreadMiddlewareStateCommitted = "THREAD_MIDDLEWARE_STATE_COMMITTED";
    public const string ThreadHistoryCompacted = "THREAD_HISTORY_COMPACTED";
}

public sealed record ThreadEventDocument
{
    public string Schema { get; init; } = "hpd.agent.thread.events";
    public int Version { get; init; } = 2;
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public long NextSequenceNumber { get; init; } = 1;
    public List<AgentEvent> Events { get; init; } = [];
}

public sealed record ThreadEventStreamMetadata
{
    public string Schema { get; init; } = "hpd.agent.thread.meta";
    public int Version { get; init; } = 1;
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public long NextSequenceNumber { get; init; } = 1;
    public string? Name { get; init; }
    public string? Description { get; init; }
    public List<string>? Tags { get; init; }
    public ThreadKind Kind { get; init; } = ThreadKind.MainAgent;
    public ThreadVisibility Visibility { get; init; } = ThreadVisibility.Visible;
    public string? ParentSessionId { get; init; }
    public string? ParentThreadId { get; init; }
    public string? SubAgentName { get; init; }
    public string? SubAgentRunId { get; init; }
    public string? SubAgentSourceKind { get; init; }
    public string? ParentToolCallId { get; init; }
    public string? SessionPolicy { get; init; }
    public string? ThreadPolicy { get; init; }
    public int MessageCount { get; init; }
}

public sealed record ThreadProjectionCache
{
    public const int CurrentVersion = 2;

    public string Schema { get; init; } = "hpd.agent.thread.projection-cache";
    public int Version { get; init; } = CurrentVersion;
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required long LastSequenceNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required Thread Thread { get; init; }
}

public sealed record ThreadCreatedEvent(
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? ThreadMetadata,
    DateTime CreatedAt,
    ThreadKind ThreadKind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? SubAgentRunId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? SessionPolicy = null,
    string? ThreadPolicy = null) : AgentEvent;

public sealed record ThreadForkedEvent(
    string SourceThreadId,
    string? FromMessageId,
    int? ResolvedMessageIndex,
    Dictionary<string, string>? Ancestors) : AgentEvent;

public sealed record ThreadMetadataUpdatedEvent(
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? ThreadMetadata,
    ThreadKind ThreadKind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? SubAgentRunId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? SessionPolicy = null,
    string? ThreadPolicy = null) : AgentEvent;

public sealed record ThreadTreeUpdatedEvent(
    string? ForkedFrom,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    List<string> ChildThreads) : AgentEvent;

public sealed record MessageStartedEvent(
    string MessageId,
    string Role,
    string? AuthorName,
    DateTimeOffset? CreatedAt,
    string? ClientInputId = null,
    AdditionalPropertiesDictionary? AdditionalProperties = null) : AgentEvent;

public sealed record MessageCompletedEvent(string MessageId) : AgentEvent;

public sealed record ContentAddedEvent(
    string MessageId,
    AIContent Content) : AgentEvent;

public sealed record ThreadMiddlewareStateCommittedEvent(
    IReadOnlyDictionary<string, string> State) : AgentEvent;

public sealed record ThreadHistoryCompactedEvent(
    string CompactionId,
    IReadOnlyList<string> ModelCompactedMessageIds,
    IReadOnlyList<string> DurableCompactedMessageIds,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    string StrategyKind,
    string RetentionKind,
    string BoundaryKind,
    string? SummaryContent,
    DateTimeOffset CompactedAt) : AgentEvent;

public static class ThreadEventFactory
{
    public static AgentEvent ThreadCreated(Thread thread) =>
        Scope(thread.SessionId, thread.Id, new ThreadCreatedEvent(
            thread.Name,
            thread.Description,
            thread.Tags,
            thread.Metadata.Count > 0 ? thread.Metadata : null,
            thread.CreatedAt,
            thread.Kind,
            thread.Visibility,
            thread.ParentSessionId,
            thread.ParentThreadId,
            thread.SubAgentName,
            thread.SubAgentRunId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.SessionPolicy,
            thread.ThreadPolicy));

    public static AgentEvent ThreadForked(Thread thread) =>
        thread.ForkedFrom is null
            ? ThreadCreated(thread)
            : Scope(thread.SessionId, thread.Id, new ThreadForkedEvent(
                thread.ForkedFrom,
                thread.ForkedAtMessageId,
                thread.ForkedAtMessageIndex,
                thread.Ancestors));

    public static AgentEvent ThreadMetadataUpdated(Thread thread) =>
        Scope(thread.SessionId, thread.Id, new ThreadMetadataUpdatedEvent(
            thread.Name,
            thread.Description,
            thread.Tags,
            thread.Metadata.Count > 0 ? thread.Metadata : null,
            thread.Kind,
            thread.Visibility,
            thread.ParentSessionId,
            thread.ParentThreadId,
            thread.SubAgentName,
            thread.SubAgentRunId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.SessionPolicy,
            thread.ThreadPolicy));

    public static AgentEvent ThreadTreeUpdated(Thread thread) =>
        Scope(thread.SessionId, thread.Id, new ThreadTreeUpdatedEvent(
            thread.ForkedFrom,
            thread.ForkedAtMessageId,
            thread.ForkedAtMessageIndex,
            thread.ChildThreads.ToList()));

    public static AgentEvent MessageStarted(string sessionId, string threadId, ChatMessage message, string? clientInputId = null) =>
        Scope(sessionId, threadId, new MessageStartedEvent(
            message.MessageId ?? string.Empty,
            message.Role.Value,
            message.AuthorName,
            message.CreatedAt,
            clientInputId,
            message.AdditionalProperties is null
                ? null
                : SanitizeAdditionalProperties(message.AdditionalProperties)));

    public static AgentEvent MessageCompleted(string sessionId, string threadId, string messageId) =>
        Scope(sessionId, threadId, new MessageCompletedEvent(messageId));

    public static AgentEvent ContentAdded(string sessionId, string threadId, string messageId, AIContent content) =>
        Scope(sessionId, threadId, new ContentAddedEvent(messageId, content));

    private static AdditionalPropertiesDictionary SanitizeAdditionalProperties(
        AdditionalPropertiesDictionary properties)
    {
        var sanitized = new AdditionalPropertiesDictionary();
        foreach (var (key, value) in properties)
        {
            sanitized[key] = value switch
            {
                null => null,
                string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
                JsonElement => value,
                _ => JsonSerializer.SerializeToElement(value, value.GetType())
            };
        }

        return sanitized;
    }

    public static AgentEvent TextMessageStarted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        string role,
        int iteration) =>
        Scope(sessionId, threadId, new TextMessageStartEvent(messageId, role)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent TextDelta(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        string text,
        int iteration) =>
        Scope(sessionId, threadId, new TextDeltaEvent(text, messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent TextMessageCompleted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        int iteration) =>
        Scope(sessionId, threadId, new TextMessageEndEvent(messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ThreadMiddlewareStateCommitted(
        string sessionId,
        string threadId,
        IReadOnlyDictionary<string, string> state) =>
        Scope(sessionId, threadId, new ThreadMiddlewareStateCommittedEvent(state));

    public static AgentEvent ThreadHistoryCompacted(
        string sessionId,
        string threadId,
        ThreadHistoryCompactedEvent evt) =>
        Scope(sessionId, threadId, evt);

    public static AgentEvent TurnStarted(
        string sessionId,
        string threadId,
        string messageTurnId,
        string conversationId,
        string agentId,
        string agentName,
        int inputMessageCount,
        bool isResume) =>
        Scope(sessionId, threadId, new MessageTurnStartedEvent(
            messageTurnId,
            conversationId,
            agentId,
            agentName)
        {
            InputMessageCount = inputMessageCount,
            IsResume = isResume
        });

    public static AgentEvent TurnCompleted(
        string sessionId,
        string threadId,
        string messageTurnId,
        string conversationId,
        string agentId,
        string agentName,
        int iteration,
        string? terminationReason,
        TimeSpan duration,
        int turnMessageCount) =>
        Scope(sessionId, threadId, new MessageTurnFinishedEvent(
            messageTurnId,
            conversationId,
            agentId,
            agentName,
            duration)
        {
            Iteration = iteration,
            TerminationReason = terminationReason,
            TurnMessageCount = turnMessageCount
        });

    public static AgentEvent TurnFailed(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string? conversationId,
        string agentId,
        string agentName,
        Exception exception) =>
        Scope(sessionId, threadId, new MessageTurnErrorEvent(exception.Message, exception)
        {
            MessageTurnId = messageTurnId,
            ConversationId = conversationId,
            AgentId = agentId,
            AgentName = agentName,
            ErrorType = exception.GetType().Name
        });

    public static AgentEvent ReasoningStarted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        string role,
        int iteration) =>
        Scope(sessionId, threadId, new ReasoningMessageStartEvent(messageId, role)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ReasoningDelta(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        string text,
        string? protectedData,
        int iteration) =>
        Scope(sessionId, threadId, new ReasoningDeltaEvent(text, messageId, protectedData)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ReasoningCompleted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        int iteration) =>
        Scope(sessionId, threadId, new ReasoningMessageEndEvent(messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallStarted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string callId,
        string name,
        string messageId,
        string? toolharnessName,
        ToolCallType? callType,
        int iteration) =>
        Scope(sessionId, threadId, new ToolCallStartEvent(callId, name, messageId, toolharnessName, callType)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallArgs(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string callId,
        string argsJson,
        int iteration) =>
        Scope(sessionId, threadId, new ToolCallArgsEvent(callId, argsJson)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallResult(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string callId,
        string? messageId,
        ToolResultPayload result,
        string? toolharnessName,
        ToolCallType? callType,
        int iteration,
        string? name = null) =>
        Scope(sessionId, threadId, new ToolCallResultEvent(callId, result, toolharnessName, callType, name)
        {
            EventFlowId = messageTurnId,
            MessageId = messageId
        });

    public static AgentEvent ToolCallCompleted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string callId,
        int iteration,
        string messageId,
        string name,
        string argsJson) =>
        Scope(sessionId, threadId, new ToolCallEndEvent(callId, messageId, name, argsJson)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent? FromAgentEvent(
        string sessionId,
        string threadId,
        AgentEvent evt,
        string? messageTurnId,
        string? conversationId,
        int iteration,
        int inputMessageCount,
        bool isResume,
        string? terminationReason,
        int turnMessageCount)
    {
        return evt switch
        {
            MessageTurnStartedEvent started => Scope(sessionId, threadId, started with
            {
                InputMessageCount = inputMessageCount,
                IsResume = isResume
            }),

            MessageTurnFinishedEvent finished => Scope(sessionId, threadId, finished with
            {
                Iteration = iteration,
                TerminationReason = terminationReason,
                TurnMessageCount = turnMessageCount
            }),

            TextMessageStartEvent textStarted when messageTurnId != null =>
                Scope(sessionId, threadId, textStarted with { EventFlowId = messageTurnId }),

            TextDeltaEvent textDelta when messageTurnId != null =>
                Scope(sessionId, threadId, textDelta with { EventFlowId = messageTurnId }),

            TextMessageEndEvent textCompleted when messageTurnId != null =>
                Scope(sessionId, threadId, textCompleted with { EventFlowId = messageTurnId }),

            ReasoningMessageStartEvent reasoningStarted when messageTurnId != null =>
                Scope(sessionId, threadId, reasoningStarted with { EventFlowId = messageTurnId }),

            ReasoningDeltaEvent reasoningDelta when messageTurnId != null =>
                Scope(sessionId, threadId, reasoningDelta with { EventFlowId = messageTurnId }),

            ReasoningMessageEndEvent reasoningCompleted when messageTurnId != null =>
                Scope(sessionId, threadId, reasoningCompleted with { EventFlowId = messageTurnId }),

            ToolCallStartEvent toolStarted =>
                Scope(sessionId, threadId, toolStarted with { EventFlowId = messageTurnId }),

            ToolCallArgsEvent toolArgs =>
                Scope(sessionId, threadId, toolArgs with { EventFlowId = messageTurnId }),

            ToolCallResultEvent toolResult =>
                Scope(sessionId, threadId, toolResult with { EventFlowId = messageTurnId }),

            ToolCallEndEvent toolCompleted =>
                Scope(sessionId, threadId, toolCompleted with { EventFlowId = messageTurnId }),

            _ when evt.ShouldPersistToThread() =>
                Scope(sessionId, threadId, evt),

            _ => null
        };
    }

    private static T Scope<T>(string sessionId, string threadId, T evt)
        where T : AgentEvent =>
        evt with
        {
            EventId = evt.EventId ?? Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ThreadId = threadId
        };
}
