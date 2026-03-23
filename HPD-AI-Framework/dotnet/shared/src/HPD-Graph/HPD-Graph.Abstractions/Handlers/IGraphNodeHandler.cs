using HPDAgent.Graph.Abstractions.Execution;

namespace HPDAgent.Graph.Abstractions.Handlers;

/// <summary>
/// Handler for executing a graph node.
/// Generic over TContext to support different execution contexts.
/// Universal interface - works for ANY workflow (not domain-specific).
/// </summary>
/// <typeparam name="TContext">The graph context type</typeparam>
public interface IGraphNodeHandler<TContext> where TContext : class
{
    /// <summary>
    /// Handler name (must be unique within the application).
    /// Used by graph configuration to reference this handler.
    /// </summary>
    string HandlerName { get; }

    /// <summary>
    /// Execute the node logic.
    /// </summary>
    /// <param name="context">Execution context (cross-cutting concerns: logging, DI, services)</param>
    /// <param name="inputs">Typed inputs from previous nodes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result (Success, Failure, Skipped, Suspended, or Cancelled)</returns>
    Task<NodeExecutionResult> ExecuteAsync(
        TContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default);
}
