using System.Collections.Immutable;

namespace HPD.Events;

/// <summary>
/// Shared correlation fields for request-session events.
/// </summary>
public interface IRequestCorrelatedEvent
{
    /// <summary>
    /// Unique identifier for this request/response interaction.
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// Name/identifier of the component that emitted this event.
    /// Examples: Middleware name, Node ID, Handler name, etc.
    /// </summary>
    string SourceName { get; }
}

/// <summary>
/// Event that starts or represents an answerable request session.
/// </summary>
public interface IRequestEvent : IRequestCorrelatedEvent
{
    /// <summary>Policy used to decide which responses may resolve the request.</summary>
    ResponsePolicy ResponsePolicy => ResponsePolicy.FirstValidResponseWins;

    /// <summary>Optional responder target for targeted request sessions.</summary>
    ResponderTarget? Target => null;

    /// <summary>Visibility hint for higher-level transports and projections.</summary>
    RequestVisibility Visibility => RequestVisibility.AllObservers;
}

/// <summary>
/// Event that attempts to resolve an answerable request session.
/// </summary>
public interface IResponseEvent : IRequestCorrelatedEvent
{
    /// <summary>Optional responder identity supplied by the responding client or host.</summary>
    string? ResponderId => null;

    /// <summary>Optional responder group supplied by the responding client or host.</summary>
    string? ResponderGroup => null;

    /// <summary>Capabilities available to the responder.</summary>
    IReadOnlySet<string> Capabilities => ImmutableHashSet<string>.Empty;
}

public enum ResponsePolicy
{
    FirstValidResponseWins,
    TargetedResponder
}

public enum RequestVisibility
{
    AllObservers,
    EligibleRespondersOnly
}

public sealed record ResponderTarget
{
    public string? ResponderId { get; init; }
    public string? ResponderGroup { get; init; }
    public IReadOnlySet<string> RequiredCapabilities { get; init; } = ImmutableHashSet<string>.Empty;
}

public enum RequestState
{
    Pending,
    Resolved,
    Expired,
    Cancelled
}

public enum RespondStatus
{
    Accepted,
    NotFound,
    AlreadyResolved,
    TimedOut,
    Cancelled,
    ResponseTypeMismatch,
    TargetMismatch,
    AmbiguousRequest
}

public sealed record RespondResult(
    RespondStatus Status,
    string RequestId,
    string? Message = null)
{
    public bool Accepted => Status == RespondStatus.Accepted;

    public static RespondResult For(RespondStatus status, string requestId, string? message = null) =>
        new(status, requestId, message);
}

public enum CancelRequestStatus
{
    Cancelled,
    NotFound,
    AlreadyResolved,
    TimedOut,
    AlreadyCancelled
}

public sealed record CancelRequestResult(
    CancelRequestStatus Status,
    string RequestId,
    string? Message = null)
{
    public bool Cancelled => Status == CancelRequestStatus.Cancelled;
}

public sealed record RequestOptions
{
    public TimeSpan? Timeout { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed record RequestSnapshot(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string ExpectedResponseEventType,
    ResponsePolicy ResponsePolicy,
    ResponderTarget? Target,
    RequestVisibility Visibility,
    RequestState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed class RequestHandle
{
    private readonly Func<string?, CancelRequestResult> _cancel;
    private readonly Func<RequestSnapshot> _snapshot;

    internal RequestHandle(
        string requestId,
        Task<Event> response,
        Func<RequestSnapshot> snapshot,
        Func<string?, CancelRequestResult> cancel)
    {
        RequestId = requestId;
        Response = response;
        _snapshot = snapshot;
        _cancel = cancel;
    }

    public string RequestId { get; }

    public Task<Event> Response { get; }

    public RequestSnapshot Snapshot => _snapshot();

    public CancelRequestResult Cancel(string? reason = null) => _cancel(reason);
}
