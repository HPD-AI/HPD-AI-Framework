namespace HPD.Events;

/// <summary>
/// Request/response surface for request-session event workflows.
/// </summary>
public interface IRequestResponseBus
{
    /// <summary>
    /// Start a tracked answerable request session without requiring the caller to await it immediately.
    /// </summary>
    RequestHandle StartRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent;

    /// <summary>
    /// Start a request session and wait for its matching response.
    /// </summary>
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent;

    /// <summary>
    /// Attempt to resolve a pending request with a response.
    /// </summary>
    RespondResult Respond(Event response);

    /// <summary>
    /// Attempt to resolve a pending request with a response.
    /// </summary>
    RespondResult Respond(string requestId, Event response);
}
