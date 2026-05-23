namespace HPD.Events;

/// <summary>
/// Request/response surface for bidirectional event workflows.
/// </summary>
public interface IRequestResponseBus
{
    /// <summary>
    /// Emit a bidirectional request and wait for its matching response.
    /// </summary>
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IBidirectionalEvent
        where TResponse : Event;

    /// <summary>
    /// Complete a pending request with a response, throwing when no waiter exists.
    /// </summary>
    void Respond(string requestId, Event response);

    /// <summary>
    /// Try to complete a pending request with a response.
    /// </summary>
    bool TryRespond(string requestId, Event response);
}
