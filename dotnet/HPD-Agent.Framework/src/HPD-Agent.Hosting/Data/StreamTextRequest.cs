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

/// <summary>Describes hosted admission of a semantic agent input.</summary>
public sealed record InputSubmissionDto(
    string Disposition,
    string? ThreadExecutionId = null,
    DateTimeOffset? StartedAt = null,
    ThreadExecutionDto? ActiveExecution = null);

public sealed record ThreadRuntimeStateDto(
    ThreadJournalCursor ObservedCursor,
    ThreadExecutionDto? ActiveExecution,
    IReadOnlyList<PendingAgentRequestDto> PendingRequests);

public sealed record PendingAgentRequestDto(
    AgentEvent Request,
    DateTimeOffset CreatedAt);
