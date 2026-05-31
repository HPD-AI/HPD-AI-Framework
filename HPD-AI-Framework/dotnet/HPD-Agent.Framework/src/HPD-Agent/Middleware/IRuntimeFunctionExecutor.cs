using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Runtime-scoped capability for executing model-requested functions outside the chat protocol.
/// </summary>
public interface IRuntimeFunctionExecutor
{
    /// <summary>
    /// Executes function calls through the agent's middleware-owned function pipeline.
    /// </summary>
    Task<IReadOnlyList<FunctionResultContent>> ExecuteFunctionCallsAsync(
        IReadOnlyList<FunctionCallContent> functionCalls,
        AgentRunConfig? runConfig = null,
        CancellationToken cancellationToken = default);
}
