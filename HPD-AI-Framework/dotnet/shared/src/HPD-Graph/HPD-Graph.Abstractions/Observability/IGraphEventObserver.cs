using HPD.Events;
using HPDAgent.Graph.Abstractions.Events;

namespace HPDAgent.Graph.Abstractions.Observability;

/// <summary>
/// Observer that processes graph events for observability purposes.
/// Inherits from HPD.Events.IEventObserver for cross-domain consistency.
/// Implementations can log, emit telemetry, track progress, etc.
/// </summary>
/// <remarks>
/// <para><b>Use IGraphEventObserver for:</b></para>
/// <list type="bullet">
/// <item>Logging graph execution lifecycle</item>
/// <item>Collecting telemetry and metrics</item>
/// <item>Performance monitoring</item>
/// <item>Caching node results</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class GraphTelemetryObserver : IGraphEventObserver
/// {
///     public bool ShouldProcess(GraphEvent evt)
///     {
///         return evt is GraphExecutionStartedEvent or GraphExecutionCompletedEvent;
///     }
///
///     public async Task OnEventAsync(GraphEvent evt, CancellationToken ct)
///     {
///         switch (evt)
///         {
///             case GraphExecutionStartedEvent started:
///                 await RecordGraphStart(started.GraphContext.GraphId);
///                 break;
///             case GraphExecutionCompletedEvent completed:
///                 await RecordGraphCompletion(completed.Duration);
///                 break;
///         }
///     }
/// }
/// </code>
/// </example>
public interface IGraphEventObserver : IEventObserver<GraphEvent>
{
    // Inherits:
    // - bool ShouldProcess(GraphEvent evt)
    // - Task OnEventAsync(GraphEvent evt, CancellationToken cancellationToken)
}
