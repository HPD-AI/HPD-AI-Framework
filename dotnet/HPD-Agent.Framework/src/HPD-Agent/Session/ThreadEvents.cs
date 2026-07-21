using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class ThreadEventTypes
{
    public const string ThreadCreated = "THREAD_CREATED";
    public const string ThreadUpdated = "THREAD_UPDATED";
    public const string ContentAdded = "CONTENT_ADDED";
    public const string ThreadMiddlewareStateCommitted = "THREAD_MIDDLEWARE_STATE_COMMITTED";
    public const string ThreadHistoryCompactionCheckpoint = "THREAD_HISTORY_COMPACTION_CHECKPOINT";
}

public sealed record ThreadCreatedEvent(
    string DefaultAgentId,
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
    string? SubAgentTaskName = null,
    string? InvocationId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? ContextPolicy = null,
    string? ForkedFrom = null,
    string? ForkedAtMessageId = null,
    int? ForkedAtMessageIndex = null,
    List<string>? ChildThreads = null,
    Dictionary<string, string>? Ancestors = null) : AgentEvent;

public sealed record ThreadUpdatedEvent(
    string DefaultAgentId,
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? ThreadMetadata,
    ThreadKind ThreadKind = ThreadKind.MainAgent,
    ThreadVisibility Visibility = ThreadVisibility.Visible,
    string? ParentSessionId = null,
    string? ParentThreadId = null,
    string? SubAgentName = null,
    string? SubAgentTaskName = null,
    string? InvocationId = null,
    string? SubAgentSourceKind = null,
    string? ParentToolCallId = null,
    string? ContextPolicy = null,
    string? ForkedFrom = null,
    string? ForkedAtMessageId = null,
    int? ForkedAtMessageIndex = null,
    List<string>? ChildThreads = null,
    Dictionary<string, string>? Ancestors = null) : AgentEvent;

public sealed record ContentAddedEvent(
    string MessageId,
    string Role,
    AIContent Content,
    string? AuthorName = null,
    DateTimeOffset? CreatedAt = null,
    string? ClientInputId = null,
    AgentMessageSource Source = AgentMessageSource.Unspecified,
    AgentMessageVisibility Visibility = AgentMessageVisibility.Transcript,
    AgentMessagePersistence Persistence = AgentMessagePersistence.ThreadHistory,
    AdditionalPropertiesDictionary? AdditionalProperties = null) : AgentEvent;

public sealed record ThreadMiddlewareStateCommittedEvent(
    IReadOnlyDictionary<string, string> State) : AgentEvent;

public sealed record CompactionPointDescriptor(
    string Kind,
    string? MessageId = null,
    string? TurnId = null,
    long? ExpectedJournalGeneration = null)
{
    public static CompactionPointDescriptor From(CompactionPoint point) => point switch
    {
        CompactAtCurrentHead => new("currentHead"),
        CompactAtMessage message => new("message", message.MessageId, null, message.ExpectedJournalGeneration),
        CompactAtTurn turn => new("turn", null, turn.TurnId, turn.ExpectedJournalGeneration),
        _ => throw new ArgumentOutOfRangeException(nameof(point))
    };
}

public sealed record CompactionPreservationDescriptor(
    string Kind,
    int? Count = null,
    long? TokenBudget = null)
{
    public static CompactionPreservationDescriptor From(CompactionPreservation preservation) => preservation switch
    {
        PreserveNoPreviousHistory => new("none"),
        PreservePreviousTurns turns => new("previousTurns", turns.Count),
        PreservePreviousUserMessages { Limit: PreviousItemCountLimit count } =>
            new("previousUserMessages", count.Count),
        PreservePreviousUserMessages { Limit: PreviousTokenBudgetLimit budget } =>
            new("previousUserMessages", TokenBudget: budget.Tokens),
        _ => throw new ArgumentOutOfRangeException(nameof(preservation))
    };
}

public sealed record CompactionStrategyDescriptor(
    string Kind,
    string? Instructions = null)
{
    public static CompactionStrategyDescriptor From(CompactionStrategy strategy) => strategy switch
    {
        RemovalCompaction => new("removal"),
        SummarizingCompaction summarizing => new("summarizing", summarizing.Instructions),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };
}

public sealed record ThreadHistoryCompactionCheckpointEvent(
    string CompactionId,
    CompactionPointDescriptor Point,
    CompactionPreservationDescriptor Preservation,
    IReadOnlyList<string> CompactedMessageIds,
    IReadOnlyList<string> PreservedMessageIds,
    IReadOnlyList<string> CarriedUserMessageSourceIds,
    IReadOnlyList<string> AfterPointMessageIds,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    CompactionStrategyDescriptor Strategy,
    CompactionCommitMode CommitMode,
    DateTimeOffset CompactedAt) : AgentEvent;

public static class ThreadEventFactory
{
    public static AgentEvent ThreadCreated(Thread thread) =>
        Scope(thread.SessionId, thread.Id, new ThreadCreatedEvent(
            thread.DefaultAgentId,
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
            thread.SubAgentTaskName,
            thread.InvocationId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.ContextPolicy,
            thread.ForkedFrom,
            thread.ForkedAtMessageId,
            thread.ForkedAtMessageIndex,
            thread.ChildThreads.ToList(),
            thread.Ancestors));

    public static AgentEvent ThreadUpdated(Thread thread) =>
        Scope(thread.SessionId, thread.Id, new ThreadUpdatedEvent(
            thread.DefaultAgentId,
            thread.Name,
            thread.Description,
            thread.Tags,
            thread.Metadata.Count > 0 ? thread.Metadata : null,
            thread.Kind,
            thread.Visibility,
            thread.ParentSessionId,
            thread.ParentThreadId,
            thread.SubAgentName,
            thread.SubAgentTaskName,
            thread.InvocationId,
            thread.SubAgentSourceKind,
            thread.ParentToolCallId,
            thread.ContextPolicy,
            thread.ForkedFrom,
            thread.ForkedAtMessageId,
            thread.ForkedAtMessageIndex,
            thread.ChildThreads.ToList(),
            thread.Ancestors));

    public static AgentEvent ContentAdded(
        string sessionId,
        string threadId,
        ChatMessage message,
        AIContent content,
        string? clientInputId = null) =>
        Scope(sessionId, threadId, new ContentAddedEvent(
            message.MessageId ?? string.Empty,
            message.Role.Value,
            content,
            message.AuthorName,
            message.CreatedAt,
            clientInputId,
            message.GetSource(),
            message.GetVisibility(),
            message.GetPersistence(),
            message.AdditionalProperties is null
                ? null
                : SanitizeAdditionalProperties(message.AdditionalProperties)));

    public static AgentEvent ContentAdded(
        string sessionId,
        string threadId,
        string messageId,
        AIContent content,
        string role = "assistant",
        string? clientInputId = null) =>
        Scope(sessionId, threadId, new ContentAddedEvent(
            messageId,
            role,
            content,
            ClientInputId: clientInputId));

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
        TextMessageStarted(
            sessionId,
            threadId,
            messageTurnId,
            messageId,
            role,
            SourceFromRole(role),
            VisibilityFromRole(role),
            iteration);

    public static AgentEvent TextMessageStarted(
        string sessionId,
        string threadId,
        string? messageTurnId,
        string messageId,
        string role,
        AgentMessageSource source,
        AgentMessageVisibility visibility,
        int iteration,
        AgentMessagePersistence persistence = AgentMessagePersistence.ThreadHistory,
        string? authorName = null,
        DateTimeOffset? createdAt = null,
        string? clientInputId = null,
        AdditionalPropertiesDictionary? additionalProperties = null) =>
        Scope(sessionId, threadId, new TextMessageStartEvent(
            messageId,
            role,
            source,
            visibility,
            persistence,
            authorName,
            createdAt,
            clientInputId,
            additionalProperties is null ? null : SanitizeAdditionalProperties(additionalProperties))
        {
            EventFlowId = messageTurnId
        });

    private static AgentMessageSource SourceFromRole(string role) =>
        string.Equals(role, ChatRole.User.Value, StringComparison.OrdinalIgnoreCase)
            ? AgentMessageSource.UserInput
            : string.Equals(role, ChatRole.Assistant.Value, StringComparison.OrdinalIgnoreCase)
                ? AgentMessageSource.AssistantOutput
                : string.Equals(role, ChatRole.System.Value, StringComparison.OrdinalIgnoreCase)
                    ? AgentMessageSource.SystemInstruction
                    : string.Equals(role, ChatRole.Tool.Value, StringComparison.OrdinalIgnoreCase)
                        ? AgentMessageSource.ToolResult
                        : AgentMessageSource.Unspecified;

    private static AgentMessageVisibility VisibilityFromRole(string role) =>
        string.Equals(role, ChatRole.System.Value, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, ChatRole.Tool.Value, StringComparison.OrdinalIgnoreCase)
            ? AgentMessageVisibility.Hidden
            : AgentMessageVisibility.Transcript;

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

    public static AgentEvent ThreadHistoryCompactionCheckpoint(
        string sessionId,
        string threadId,
        ThreadHistoryCompactionCheckpointEvent evt) =>
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

            _ => Scope(sessionId, threadId, evt)
        };
    }

    private static T Scope<T>(string sessionId, string threadId, T evt)
        where T : AgentEvent =>
        evt with
        {
            EventId = evt.EventId,
            SessionId = sessionId,
            ThreadId = threadId
        };
}
