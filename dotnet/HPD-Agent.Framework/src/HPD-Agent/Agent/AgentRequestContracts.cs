namespace HPD.Agent;

/// <summary>Agent-domain event that opens an answerable request.</summary>
/// <remarks>
/// This Agent-facing contract inherits the generic HPD Events request protocol so the
/// same event instance is persisted, published, routed, and inspected without wrappers.
/// Agent-specific request semantics may be added here without changing that identity.
/// </remarks>
public interface IAgentRequestEvent : HPD.Events.IRequestEvent;

/// <summary>Agent request whose expected response type is durable type metadata.</summary>
public interface IAgentRequestEvent<TResponse> : IAgentRequestEvent
    where TResponse : AgentEvent, IAgentResponseEvent;

/// <summary>Agent-domain event that attempts to resolve a correlated request.</summary>
/// <remarks>
/// This Agent-facing contract inherits the generic HPD Events response protocol so the
/// lower coordinator can validate and route the original Agent event directly.
/// </remarks>
public interface IAgentResponseEvent : HPD.Events.IResponseEvent;

/// <summary>Typed result of attempting to answer an Agent request.</summary>
public enum AgentRespondStatus
{
    /// <summary>The active request accepted the response.</summary>
    Accepted,
    /// <summary>No durable request with the supplied identity exists.</summary>
    NotFound,
    /// <summary>A durable response already resolved the request.</summary>
    AlreadyResolved,
    /// <summary>The request expired before the response arrived.</summary>
    TimedOut,
    /// <summary>The request was cancelled before the response arrived.</summary>
    Cancelled,
    /// <summary>The response type does not match the request's declared response type.</summary>
    ResponseTypeMismatch,
    /// <summary>The response target does not match the request target.</summary>
    TargetMismatch,
    /// <summary>The response cannot be associated with exactly one active request.</summary>
    AmbiguousRequest,
    /// <summary>The execution that owned the request has ended.</summary>
    ExecutionEnded,
    /// <summary>The owning execution is active, but its response waiter is unavailable.</summary>
    RuntimeUnavailable
}

/// <summary>Result returned for every valid Agent response attempt.</summary>
public sealed record AgentRespondResult(
    AgentRespondStatus Status,
    string RequestId,
    string? Message = null)
{
    /// <summary>Gets whether the response was accepted by the active request.</summary>
    public bool Accepted => Status == AgentRespondStatus.Accepted;
}

internal static class AgentRespondResultMapping
{
    public static AgentRespondResult ToAgentResult(this HPD.Events.RespondResult result) => new(
        result.Status switch
        {
            HPD.Events.RespondStatus.Accepted => AgentRespondStatus.Accepted,
            HPD.Events.RespondStatus.NotFound => AgentRespondStatus.NotFound,
            HPD.Events.RespondStatus.AlreadyResolved => AgentRespondStatus.AlreadyResolved,
            HPD.Events.RespondStatus.TimedOut => AgentRespondStatus.TimedOut,
            HPD.Events.RespondStatus.Cancelled => AgentRespondStatus.Cancelled,
            HPD.Events.RespondStatus.ResponseTypeMismatch => AgentRespondStatus.ResponseTypeMismatch,
            HPD.Events.RespondStatus.TargetMismatch => AgentRespondStatus.TargetMismatch,
            HPD.Events.RespondStatus.AmbiguousRequest => AgentRespondStatus.AmbiguousRequest,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
        },
        result.RequestId,
        result.Message);
}

/// <summary>Terminal reason recorded for a durable unanswered Agent request.</summary>
public enum AgentRequestTerminalKind
{
    /// <summary>The request's configured deadline elapsed.</summary>
    Expired,
    /// <summary>Cancellation ended the request.</summary>
    Cancelled,
    /// <summary>The owning execution ended or was lost before resolution.</summary>
    Abandoned
}

/// <summary>Durable terminal fact for a request that did not receive a response.</summary>
public sealed record AgentRequestTerminatedEvent(
    string RequestId,
    string SourceName,
    AgentRequestTerminalKind TerminalKind,
    string? Reason,
    DateTimeOffset TerminatedAt) : AgentEvent
{
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    public override HPD.Events.EventChannel Channel { get; init; } = HPD.Events.EventChannel.Control;
    public override HPD.Events.EventDirection Direction { get; init; } = HPD.Events.EventDirection.Upstream;
}
