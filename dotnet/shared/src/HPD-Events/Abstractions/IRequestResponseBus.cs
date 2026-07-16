namespace HPD.Events;

/// <summary>
/// Request/response surface for request-session event workflows.
/// </summary>
public interface IRequestResponseBus
{
    /// <summary>Returns answerable requests currently owned by this bus hierarchy.</summary>
    IReadOnlyList<PendingRequestSnapshot> GetPendingRequests();

    /// <summary>
    /// Start a tracked answerable request session without requiring the caller to await it immediately.
    /// </summary>
    RequestHandle StartRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent;

    RequestHandle RegisterRequest<TRequest, TResponse>(
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

    /// <summary>
    /// Validates and reserves a matching request, runs the required completion boundary,
    /// and only then releases the waiting requester.
    /// </summary>
    ValueTask<RespondResult> RespondAsync(
        string requestId,
        Event response,
        Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion,
        CancellationToken cancellationToken = default);
}
