namespace HPD.Agent.Middleware;

/// <summary>
/// Immutable identity snapshot for a single function invocation.
/// </summary>
public sealed record FunctionInvocationSnapshot
{
    public required string AgentName { get; init; }

    public required string FunctionCallId { get; init; }

    public required string FunctionName { get; init; }

    public string? ConversationId { get; init; }

    public string? SessionId { get; init; }

    public string? BranchId { get; init; }

    public string? TraceId { get; init; }

    public ToolInvocationInfo? Invocation { get; init; }

    public string? BatchId => Invocation?.BatchId;

    public int? ToolCallIndex => Invocation?.ToolCallIndex;
}
