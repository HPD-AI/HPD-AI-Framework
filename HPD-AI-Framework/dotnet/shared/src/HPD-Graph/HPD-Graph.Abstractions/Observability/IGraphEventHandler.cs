using HPD.Events;
using HPDAgent.Graph.Abstractions.Events;

namespace HPDAgent.Graph.Abstractions.Observability;

/// <summary>
/// Handler for synchronous graph event consumption (UI progress bars, HITL prompts).
/// Inherits from HPD.Events.IEventHandler for cross-domain consistency.
/// Executes synchronously with ordering guarantees.
/// </summary>
/// <remarks>
/// <para><b>Use IGraphEventHandler for:</b></para>
/// <list type="bullet">
/// <item>Console progress output during graph execution</item>
/// <item>UI progress bars and status updates</item>
/// <item>Human-in-the-loop approval prompts</item>
/// <item>Web UI streaming (SSE, WebSockets)</item>
/// <item>Any scenario requiring ordered event processing</item>
/// </list>
/// <para><b>Performance Note:</b></para>
/// <para>
/// Handlers block graph event emission until complete. Keep processing fast.
/// For background telemetry, use IGraphEventObserver instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class ConsoleProgressHandler : IGraphEventHandler
/// {
///     public bool ShouldProcess(GraphEvent evt)
///     {
///         return evt is NodeExecutionCompletedEvent or LayerExecutionStartedEvent;
///     }
///
///     public async Task OnEventAsync(GraphEvent evt, CancellationToken ct)
///     {
///         switch (evt)
///         {
///             case LayerExecutionStartedEvent layer:
///                 Console.WriteLine($"Layer {layer.LayerIndex}: {layer.NodeCount} nodes");
///                 break;
///             case NodeExecutionCompletedEvent node:
///                 Console.Write($"✓ {node.NodeId} ");
///                 break;
///         }
///     }
/// }
/// </code>
/// </example>
public interface IGraphEventHandler : IEventHandler<GraphEvent>
{
    // Inherits:
    // - bool ShouldProcess(GraphEvent evt)
    // - Task OnEventAsync(GraphEvent evt, CancellationToken cancellationToken)
}
