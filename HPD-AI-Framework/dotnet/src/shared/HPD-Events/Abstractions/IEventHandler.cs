namespace HPD.Events;

/// <summary>
/// Handler for synchronous, ordered event consumption (UI, user interaction).
/// Handlers execute synchronously and block event emission until complete.
/// Guarantees ordered execution.
/// </summary>
/// <typeparam name="TEvent">The type of event this handler handles</typeparam>
/// <remarks>
/// <para><b>Use IEventHandler for:</b></para>
/// <list type="bullet">
/// <item>Console output and UI rendering</item>
/// <item>Web UI streaming (SSE, WebSockets)</item>
/// <item>Permission prompts that need user interaction</item>
/// <item>Any scenario requiring events in order</item>
/// </list>
/// <para><b>Error Handling:</b></para>
/// <para>
/// Handler exceptions are caught and logged but don't crash the system.
/// If a handler throws, the event is still yielded to stream consumers.
/// </para>
/// <para><b>Performance Note:</b></para>
/// <para>
/// Handlers block the event stream until complete. Keep processing fast.
/// For long-running operations, consider using IEventObserver instead.
/// </para>
/// </remarks>
public interface IEventHandler<in TEvent> where TEvent : Event
{
    /// <summary>
    /// Determines if this handler should process the given event.
    /// Default: true (process all events).
    /// </summary>
    /// <param name="evt">The event to potentially process</param>
    /// <returns>True if OnEventAsync should be called, false to skip</returns>
    /// <remarks>
    /// Called for every event - keep this fast (check type, not content).
    /// </remarks>
    bool ShouldProcess(TEvent evt) => true;

    /// <summary>
    /// Processes the event synchronously within the event loop.
    /// Keep this fast - it blocks the stream until complete.
    /// </summary>
    /// <param name="evt">The event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task OnEventAsync(TEvent evt, CancellationToken cancellationToken = default);
}
