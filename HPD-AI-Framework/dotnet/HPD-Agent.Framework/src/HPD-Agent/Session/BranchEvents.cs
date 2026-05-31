using Microsoft.Extensions.AI;

namespace HPD.Agent;

public static class BranchEventTypes
{
    public const string BranchCreated = "BRANCH_CREATED";
    public const string BranchForked = "BRANCH_FORKED";
    public const string BranchMetadataUpdated = "BRANCH_METADATA_UPDATED";
    public const string BranchTreeUpdated = "BRANCH_TREE_UPDATED";
    public const string MessageStarted = "MESSAGE_STARTED";
    public const string MessageCompleted = "MESSAGE_COMPLETED";
    public const string ContentAdded = "CONTENT_ADDED";
    public const string BranchMiddlewareStateCommitted = "BRANCH_MIDDLEWARE_STATE_COMMITTED";
    public const string BranchHistoryCompacted = "BRANCH_HISTORY_COMPACTED";
}

public sealed record BranchEventDocument
{
    public string Schema { get; init; } = "hpd.agent.branch.events";
    public int Version { get; init; } = 2;
    public required string SessionId { get; init; }
    public required string BranchId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public long NextSequenceNumber { get; init; } = 1;
    public List<AgentEvent> Events { get; init; } = [];
}

public sealed record BranchCreatedEvent(
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? BranchMetadata,
    DateTime CreatedAt) : AgentEvent;

public sealed record BranchForkedEvent(
    string SourceBranchId,
    string FromMessageId,
    int ResolvedMessageIndex,
    Dictionary<string, string>? Ancestors) : AgentEvent;

public sealed record BranchMetadataUpdatedEvent(
    string? Name,
    string? Description,
    List<string>? Tags,
    Dictionary<string, object>? BranchMetadata) : AgentEvent;

public sealed record BranchTreeUpdatedEvent(
    string? ForkedFrom,
    string? ForkedAtMessageId,
    int? ForkedAtMessageIndex,
    int SiblingIndex,
    int TotalSiblings,
    bool IsOriginal,
    string? OriginalBranchId,
    string? PreviousSiblingId,
    string? NextSiblingId,
    List<string> ChildBranches) : AgentEvent;

public sealed record MessageStartedEvent(
    string MessageId,
    string Role,
    string? AuthorName,
    DateTimeOffset? CreatedAt) : AgentEvent;

public sealed record MessageCompletedEvent(string MessageId) : AgentEvent;

public sealed record ContentAddedEvent(
    string MessageId,
    AIContent Content) : AgentEvent;

public sealed record BranchMiddlewareStateCommittedEvent(
    IReadOnlyDictionary<string, string> State) : AgentEvent;

public sealed record BranchHistoryCompactedEvent(
    string CompactionId,
    IReadOnlyList<string> ModelCompactedMessageIds,
    IReadOnlyList<string> DurableCompactedMessageIds,
    IReadOnlyList<ChatMessage> ReplacementMessages,
    string StrategyKind,
    string RetentionKind,
    string BoundaryKind,
    string? SummaryContent,
    DateTimeOffset CompactedAt) : AgentEvent;

public static class BranchEventFactory
{
    public static AgentEvent BranchCreated(Branch branch) =>
        Scope(branch.SessionId, branch.Id, new BranchCreatedEvent(
            branch.Name,
            branch.Description,
            branch.Tags,
            branch.Metadata.Count > 0 ? branch.Metadata : null,
            branch.CreatedAt));

    public static AgentEvent BranchForked(Branch branch) =>
        branch.ForkedFrom is null
            ? BranchCreated(branch)
            : Scope(branch.SessionId, branch.Id, new BranchForkedEvent(
                branch.ForkedFrom,
                branch.ForkedAtMessageId ?? string.Empty,
                branch.ForkedAtMessageIndex ?? 0,
                branch.Ancestors));

    public static AgentEvent BranchMetadataUpdated(Branch branch) =>
        Scope(branch.SessionId, branch.Id, new BranchMetadataUpdatedEvent(
            branch.Name,
            branch.Description,
            branch.Tags,
            branch.Metadata.Count > 0 ? branch.Metadata : null));

    public static AgentEvent BranchTreeUpdated(Branch branch) =>
        Scope(branch.SessionId, branch.Id, new BranchTreeUpdatedEvent(
            branch.ForkedFrom,
            branch.ForkedAtMessageId,
            branch.ForkedAtMessageIndex,
            branch.SiblingIndex,
            branch.TotalSiblings,
            branch.IsOriginal,
            branch.OriginalBranchId,
            branch.PreviousSiblingId,
            branch.NextSiblingId,
            branch.ChildBranches.ToList()));

    public static AgentEvent MessageStarted(string sessionId, string branchId, ChatMessage message) =>
        Scope(sessionId, branchId, new MessageStartedEvent(
            message.MessageId ?? string.Empty,
            message.Role.Value,
            message.AuthorName,
            message.CreatedAt));

    public static AgentEvent MessageCompleted(string sessionId, string branchId, string messageId) =>
        Scope(sessionId, branchId, new MessageCompletedEvent(messageId));

    public static AgentEvent ContentAdded(string sessionId, string branchId, string messageId, AIContent content) =>
        Scope(sessionId, branchId, new ContentAddedEvent(messageId, content));

    public static AgentEvent TextMessageStarted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        string role,
        int iteration) =>
        Scope(sessionId, branchId, new TextMessageStartEvent(messageId, role)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent TextDelta(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        string text,
        int iteration) =>
        Scope(sessionId, branchId, new TextDeltaEvent(text, messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent TextMessageCompleted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        int iteration) =>
        Scope(sessionId, branchId, new TextMessageEndEvent(messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent BranchMiddlewareStateCommitted(
        string sessionId,
        string branchId,
        IReadOnlyDictionary<string, string> state) =>
        Scope(sessionId, branchId, new BranchMiddlewareStateCommittedEvent(state));

    public static AgentEvent BranchHistoryCompacted(
        string sessionId,
        string branchId,
        BranchHistoryCompactedEvent evt) =>
        Scope(sessionId, branchId, evt);

    public static AgentEvent TurnStarted(
        string sessionId,
        string branchId,
        string messageTurnId,
        string conversationId,
        string agentId,
        string agentName,
        int inputMessageCount,
        bool isResume) =>
        Scope(sessionId, branchId, new MessageTurnStartedEvent(
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
        string branchId,
        string messageTurnId,
        string conversationId,
        string agentId,
        string agentName,
        int iteration,
        string? terminationReason,
        TimeSpan duration,
        int turnMessageCount) =>
        Scope(sessionId, branchId, new MessageTurnFinishedEvent(
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
        string branchId,
        string? messageTurnId,
        string? conversationId,
        string agentId,
        string agentName,
        Exception exception) =>
        Scope(sessionId, branchId, new MessageTurnErrorEvent(exception.Message, exception)
        {
            MessageTurnId = messageTurnId,
            ConversationId = conversationId,
            AgentId = agentId,
            AgentName = agentName,
            ErrorType = exception.GetType().Name
        });

    public static AgentEvent ReasoningStarted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        string role,
        int iteration) =>
        Scope(sessionId, branchId, new ReasoningMessageStartEvent(messageId, role)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ReasoningDelta(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        string text,
        string? protectedData,
        int iteration) =>
        Scope(sessionId, branchId, new ReasoningDeltaEvent(text, messageId, protectedData)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ReasoningCompleted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string messageId,
        int iteration) =>
        Scope(sessionId, branchId, new ReasoningMessageEndEvent(messageId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallStarted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string callId,
        string name,
        string messageId,
        string? harnessName,
        ToolCallType? callType,
        int iteration) =>
        Scope(sessionId, branchId, new ToolCallStartEvent(callId, name, messageId, harnessName, callType)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallArgs(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string callId,
        string argsJson,
        int iteration) =>
        Scope(sessionId, branchId, new ToolCallArgsEvent(callId, argsJson)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent ToolCallResult(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string callId,
        string? messageId,
        ToolResultPayload result,
        string? harnessName,
        ToolCallType? callType,
        int iteration) =>
        Scope(sessionId, branchId, new ToolCallResultEvent(callId, result, harnessName, callType)
        {
            EventFlowId = messageTurnId,
            MessageId = messageId
        });

    public static AgentEvent ToolCallCompleted(
        string sessionId,
        string branchId,
        string? messageTurnId,
        string callId,
        int iteration) =>
        Scope(sessionId, branchId, new ToolCallEndEvent(callId)
        {
            EventFlowId = messageTurnId
        });

    public static AgentEvent? FromAgentEvent(
        string sessionId,
        string branchId,
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
            MessageTurnStartedEvent started => Scope(sessionId, branchId, started with
            {
                InputMessageCount = inputMessageCount,
                IsResume = isResume
            }),

            MessageTurnFinishedEvent finished => Scope(sessionId, branchId, finished with
            {
                Iteration = iteration,
                TerminationReason = terminationReason,
                TurnMessageCount = turnMessageCount
            }),

            ReasoningMessageStartEvent reasoningStarted when messageTurnId != null =>
                Scope(sessionId, branchId, reasoningStarted with { EventFlowId = messageTurnId }),

            ReasoningDeltaEvent reasoningDelta when messageTurnId != null =>
                Scope(sessionId, branchId, reasoningDelta with { EventFlowId = messageTurnId }),

            ReasoningMessageEndEvent reasoningCompleted when messageTurnId != null =>
                Scope(sessionId, branchId, reasoningCompleted with { EventFlowId = messageTurnId }),

            ToolCallStartEvent toolStarted =>
                Scope(sessionId, branchId, toolStarted with { EventFlowId = messageTurnId }),

            ToolCallArgsEvent toolArgs =>
                Scope(sessionId, branchId, toolArgs with { EventFlowId = messageTurnId }),

            ToolCallResultEvent toolResult =>
                Scope(sessionId, branchId, toolResult with { EventFlowId = messageTurnId }),

            ToolCallEndEvent toolCompleted =>
                Scope(sessionId, branchId, toolCompleted with { EventFlowId = messageTurnId }),

            _ when evt.ShouldPersistToBranch() =>
                Scope(sessionId, branchId, evt),

            _ => null
        };
    }

    private static T Scope<T>(string sessionId, string branchId, T evt)
        where T : AgentEvent =>
        evt with
        {
            EventId = evt.EventId ?? Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            BranchId = branchId
        };
}
