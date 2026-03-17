namespace HPD.Agent;

/// <summary>
/// Handler for primary event stream consumption (UI, console, web streams).
/// Unlike <see cref="IAgentEventObserver"/>, handlers are awaited synchronously
/// in the event loop, guaranteeing ordered execution.
/// </summary>
/// <remarks>
/// <para><b>Use IAgentEventHandler for:</b></para>
/// <list type="bullet">
/// <item>Console output</item>
/// <item>Web UI streaming (SSE, WebSockets)</item>
/// <item>Permission prompts that need user interaction</item>
/// <item>Any UI that requires events in order</item>
/// </list>
/// <para><b>Use IAgentEventObserver for:</b></para>
/// <list type="bullet">
/// <item>Telemetry and metrics</item>
/// <item>Background logging</item>
/// <item>Analytics that don't need ordering</item>
/// </list>
/// <para><b>Error Handling:</b></para>
/// <para>
/// Handler exceptions are caught and logged but don't crash the agent.
/// If a handler throws, the event is still yielded to stream consumers.
/// </para>
/// <para><b>Performance Note:</b></para>
/// <para>
/// Handlers block the event stream until complete. Keep processing fast.
/// For long-running operations, consider using <see cref="IAgentEventObserver"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class ConsoleEventHandler : IAgentEventHandler
/// {
///     public bool ShouldProcess(AgentEvent evt)
///     {
///         return evt is TextDeltaEvent or PermissionRequestEvent;
///     }
///
///     public async Task OnEventAsync(AgentEvent evt, CancellationToken ct)
///     {
///         switch (evt)
///         {
///             case TextDeltaEvent textDelta:
///                 Console.Write(textDelta.Text);
///                 break;
///             case PermissionRequestEvent permReq:
///                 await HandlePermissionAsync(permReq, ct);
///                 break;
///         }
///     }
/// }
///
/// // Register with agent
/// var agent = new AgentBuilder(config)
///     .WithEventHandler(new ConsoleEventHandler())  // Synchronous, ordered
///     .WithObserver(new TelemetryObserver())        // Fire-and-forget
///     .Build();
/// </code>
/// </example>
public interface IAgentEventHandler : HPD.Events.IEventHandler<AgentEvent>
{
    // Inherits:
    // - bool ShouldProcess(AgentEvent evt)
    // - Task OnEventAsync(AgentEvent evt, CancellationToken cancellationToken)
}
