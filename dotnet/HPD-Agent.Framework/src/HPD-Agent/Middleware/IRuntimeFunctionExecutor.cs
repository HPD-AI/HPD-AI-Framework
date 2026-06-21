using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-native result from executing a model-requested function outside the chat protocol.
/// </summary>
public sealed record RuntimeFunctionExecutionResult
{
    public required string CallId { get; init; }

    public string? FunctionName { get; init; }

    public object? Result { get; init; }

    public required HPD.Agent.ToolResultPayload Payload { get; init; }

    public Exception? Exception { get; init; }

    public bool Succeeded { get; init; }

    public bool WasBlocked { get; init; }

    public bool WasUnknown { get; init; }

    public bool WasOutputTool { get; init; }

    public ToolResultMetadata ResultMetadata { get; init; } = new();
}

/// <summary>
/// Runtime-scoped capability for executing model-requested functions outside the chat protocol.
/// </summary>
public interface IRuntimeFunctionExecutor
{
    /// <summary>
    /// Executes function calls through the agent's middleware-owned function pipeline.
    /// </summary>
    Task<IReadOnlyList<RuntimeFunctionExecutionResult>> ExecuteFunctionCallsAsync(
        IReadOnlyList<FunctionCallContent> functionCalls,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default);
}
