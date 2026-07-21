using HPD.Agent;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Normal hosted-runtime text input request.
/// Route scope supplies agent, session, and thread identity.
/// </summary>
public sealed record StreamTextRequest(
    string Text,
    AgentRunConfig? RunConfig = null,
    string? ClientInputId = null);

public sealed record InputSubmissionDto(
    string ThreadExecutionId,
    DateTimeOffset StartedAt);

public sealed record InterruptionSubmissionDto(
    string Status,
    ThreadExecutionDto? ActiveExecution = null);

public sealed record ThreadRuntimeStateDto(
    ThreadJournalCursor ObservedCursor,
    ThreadExecutionDto? ActiveExecution,
    IReadOnlyList<PendingAgentRequestDto> PendingRequests);

public sealed record PendingAgentRequestDto(
    AgentEvent Request,
    DateTimeOffset CreatedAt);
