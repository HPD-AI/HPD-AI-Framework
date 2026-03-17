namespace HPD.Events;

/// <summary>
/// Observer for fire-and-forget event processing (telemetry, logging, analytics).
/// Observers process events asynchronously without blocking the event stream.
/// No ordering guarantees.
/// </summary>
/// <typeparam name="TEvent">The type of event this observer handles</typeparam>
/// <remarks>
/// <para><b>Use IEventObserver for:</b></para>
/// <list type="bullet">
/// <item>Telemetry and metrics collection</item>
/// <item>Background logging</item>
/// <item>Analytics that don't need ordering</item>
/// <item>Caching and persistence</item>
/// </list>
/// <para><b>Performance Note:</b></para>
/// <para>
/// Observers are invoked asynchronously and do not block event emission.
/// Multiple observers can run concurrently.
/// </para>
/// </remarks>
public interface IEventObserver<in TEvent> where TEvent : Event
{
    /// <summary>
    /// Determines if this observer should process the given event.
    /// Default: true (process all events).
    /// Override to filter out unwanted events for performance.
    /// </summary>
    /// <param name="evt">The event to potentially process</param>
    /// <returns>True if OnEventAsync should be called, false to skip</returns>
    /// <remarks>
    /// Called for every event - keep this fast (check type, not content).
    /// </remarks>
    bool ShouldProcess(TEvent evt) => true;

    /// <summary>
    /// Called when an event is emitted (fire-and-forget).
    /// Observers should handle events asynchronously without blocking.
    /// </summary>
    /// <param name="evt">The event to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <remarks>
    /// <para><b>Error Handling:</b></para>
    /// <para>
    /// Observer exceptions are caught and logged but don't crash the system.
    /// If an observer throws, other observers still process the event.
    /// </para>
    /// </remarks>
    Task OnEventAsync(TEvent evt, CancellationToken cancellationToken = default);
}
