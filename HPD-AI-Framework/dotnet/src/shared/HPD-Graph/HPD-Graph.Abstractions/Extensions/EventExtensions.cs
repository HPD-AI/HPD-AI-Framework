using HPD.Events;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Events;

namespace HPDAgent.Graph.Extensions;

/// <summary>
/// Extension methods for accessing graph-specific context from any event.
/// Enables cross-domain event consumption (e.g., agents reading graph events).
/// </summary>
public static class EventExtensions
{
    /// <summary>
    /// Get graph execution context from any event.
    /// Checks GraphEvent.GraphContext first (strongly typed),
    /// then falls back to Extensions dictionary (cross-domain enrichment).
    /// </summary>
    /// <param name="evt">Event to extract context from</param>
    /// <returns>Graph execution context if available, null otherwise</returns>
    public static GraphExecutionContext? GetGraphContext(this Event evt)
    {
        // Primary: strongly typed GraphEvent field (fast, type-safe)
        if (evt is GraphEvent graphEvt)
            return graphEvt.GraphContext;

        // Fallback: extension dictionary (for cross-domain scenarios)
        if (evt.Extensions?.TryGetValue("GraphContext", out var ctx) == true)
            return ctx as GraphExecutionContext;

        return null;
    }
}

/// <summary>
/// Extension methods for emitting diagnostic events from graph contexts.
/// Provides a convenient way to log with real-time streaming via EventCoordinator.
/// </summary>
public static class GraphContextDiagnosticExtensions
{
    /// <summary>
    /// Emit a diagnostic event via the EventCoordinator.
    /// If EventCoordinator is not set, falls back to context.Log().
    /// </summary>
    /// <param name="context">The graph context</param>
    /// <param name="source">Source of the log (e.g., "Orchestrator", handler name)</param>
    /// <param name="message">The log message</param>
    /// <param name="level">Log level (default: Debug)</param>
    /// <param name="nodeId">Optional node ID</param>
    /// <param name="exception">Optional exception</param>
    /// <param name="data">Optional structured data</param>
    public static void EmitDiagnostic(
        this IGraphContext context,
        string source,
        string message,
        LogLevel level = LogLevel.Debug,
        string? nodeId = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        // Always log to context for persistence
        context.Log(source, message, level, nodeId, exception);

        // Emit event for real-time streaming if coordinator available
        context.EventCoordinator?.Emit(new GraphDiagnosticEvent
        {
            Level = level,
            Source = source,
            Message = message,
            NodeId = nodeId,
            Exception = exception,
            Data = data,
            GraphContext = new GraphExecutionContext
            {
                GraphId = context.ExecutionId,
                TotalNodes = context.Graph.Nodes.Count,
                CompletedNodes = context.CompletedNodes.Count,
                CurrentLayer = context.CurrentLayerIndex
            }
        });
    }

    /// <summary>
    /// Emit a trace-level diagnostic event.
    /// </summary>
    public static void EmitTrace(this IGraphContext context, string source, string message, string? nodeId = null)
        => context.EmitDiagnostic(source, message, LogLevel.Trace, nodeId);

    /// <summary>
    /// Emit a debug-level diagnostic event.
    /// </summary>
    public static void EmitDebug(this IGraphContext context, string source, string message, string? nodeId = null, IReadOnlyDictionary<string, object>? data = null)
        => context.EmitDiagnostic(source, message, LogLevel.Debug, nodeId, data: data);

    /// <summary>
    /// Emit an info-level diagnostic event.
    /// </summary>
    public static void EmitInfo(this IGraphContext context, string source, string message, string? nodeId = null)
        => context.EmitDiagnostic(source, message, LogLevel.Information, nodeId);

    /// <summary>
    /// Emit a warning-level diagnostic event.
    /// </summary>
    public static void EmitWarning(this IGraphContext context, string source, string message, string? nodeId = null, Exception? exception = null)
        => context.EmitDiagnostic(source, message, LogLevel.Warning, nodeId, exception);

    /// <summary>
    /// Emit an error-level diagnostic event.
    /// </summary>
    public static void EmitError(this IGraphContext context, string source, string message, string? nodeId = null, Exception? exception = null)
        => context.EmitDiagnostic(source, message, LogLevel.Error, nodeId, exception);
}
