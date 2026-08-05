using HPD.Agent.Middleware;
using HPD.Events;

namespace HPD.Agent;

/// <summary>Defines the Native AOT-safe runtime boundary implemented by multi-agent workflows.</summary>
/// <remarks>The process-local parent context is never serialized or transported remotely.</remarks>
public interface IMultiAgentWorkflow
{
    /// <summary>Executes the workflow and streams its public events.</summary>
    /// <param name="input">The workflow input.</param>
    /// <param name="parentContext">The invoking function context, or <see langword="null"/> for a root invocation.</param>
    /// <param name="cancellationToken">Cancels the complete workflow invocation.</param>
    /// <returns>A one-shot asynchronous event stream.</returns>
    IAsyncEnumerable<Event> ExecuteStreamingAsync(
        string input,
        FunctionExecutionContext? parentContext,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the workflow and returns its final text.</summary>
    /// <param name="input">The workflow input.</param>
    /// <param name="parentContext">The invoking function context, or <see langword="null"/> for a root invocation.</param>
    /// <param name="cancellationToken">Cancels the complete workflow invocation.</param>
    /// <returns>The workflow's final answer or formatted output.</returns>
    Task<string> RunAsync(
        string input,
        FunctionExecutionContext? parentContext,
        CancellationToken cancellationToken = default);
}
