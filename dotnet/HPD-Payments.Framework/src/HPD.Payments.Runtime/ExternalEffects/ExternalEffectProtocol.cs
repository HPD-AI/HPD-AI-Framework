using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.ExternalEffects;

/// <summary>Immutable knowledge-state kernel for one irreversible external effect attempt.</summary>
public sealed record ExternalEffectProtocolState
{
    /// <summary>Gets the exact operation and attempt binding.</summary>
    public ExternalEffectOperation Operation { get; }
    /// <summary>Gets the current local knowledge state.</summary>
    public ExternalEffectState State { get; }
    /// <summary>Gets the digest of the latest admitted fact.</summary>
    public CanonicalDigest LatestFactDigest { get; }
    /// <summary>Gets whether synchronization or adjudication is required before another dispatch.</summary>
    public bool RequiresResolution => State is ExternalEffectState.Dispatching or ExternalEffectState.PossibleDispatch;
    /// <summary>Gets whether another dispatch is permitted from current knowledge.</summary>
    public bool PermitsDispatch => State is ExternalEffectState.NotDispatched or ExternalEffectState.ConfirmedNotOccurred;

    private ExternalEffectProtocolState(ExternalEffectOperation operation, ExternalEffectState state, CanonicalDigest latestFactDigest) =>
        (Operation, State, LatestFactDigest) = (operation, state, latestFactDigest);

    /// <summary>Creates the recorded pre-dispatch state.</summary>
    public static ExternalEffectProtocolState Create(ExternalEffectOperation operation, CanonicalDigest initialFactDigest)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(initialFactDigest);
        return new(operation, ExternalEffectState.NotDispatched, initialFactDigest);
    }

    /// <summary>Rehydrates one previously admitted knowledge state from authenticated persistence.</summary>
    public static ExternalEffectProtocolState Restore(ExternalEffectOperation operation, ExternalEffectState state, CanonicalDigest latestFactDigest)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(latestFactDigest);
        if (state == ExternalEffectState.None || !Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        return new(operation, state, latestFactDigest);
    }

    /// <summary>Records local dispatch start without claiming that bytes crossed the boundary.</summary>
    public ExternalEffectProtocolTransition BeginDispatch(CanonicalDigest factDigest)
    {
        ArgumentNullException.ThrowIfNull(factDigest);
        return State is ExternalEffectState.NotDispatched or ExternalEffectState.ConfirmedNotOccurred
            ? Accept(new(Operation, ExternalEffectState.Dispatching, factDigest), "dispatching")
            : Reject("dispatch-not-safe");
    }

    /// <summary>Records that the send boundary may have been crossed.</summary>
    public ExternalEffectProtocolTransition MarkPossibleDispatch(CanonicalDigest factDigest)
    {
        ArgumentNullException.ThrowIfNull(factDigest);
        return State == ExternalEffectState.Dispatching
            ? Accept(new(Operation, ExternalEffectState.PossibleDispatch, factDigest), "possible-dispatch")
            : Reject("send-boundary-not-active");
    }

    /// <summary>Admits synchronized evidence about occurrence without deriving truth from transport health.</summary>
    public ExternalEffectProtocolTransition Synchronize(bool confirmedOccurred, CanonicalDigest factDigest)
    {
        ArgumentNullException.ThrowIfNull(factDigest);
        if (State is not (ExternalEffectState.Dispatching or ExternalEffectState.PossibleDispatch))
            return Reject("synchronization-not-required");
        var next = confirmedOccurred ? ExternalEffectState.ConfirmedOccurred : ExternalEffectState.ConfirmedNotOccurred;
        return Accept(new(Operation, next, factDigest), confirmedOccurred ? "confirmed-occurred" : "confirmed-not-occurred");
    }

    /// <summary>Admits a governed adjudication after conflicting evidence.</summary>
    public ExternalEffectProtocolTransition Adjudicate(CanonicalDigest factDigest)
    {
        ArgumentNullException.ThrowIfNull(factDigest);
        return State is ExternalEffectState.PossibleDispatch or ExternalEffectState.ConfirmedOccurred or ExternalEffectState.ConfirmedNotOccurred
            ? Accept(new(Operation, ExternalEffectState.Adjudicated, factDigest), "adjudicated")
            : Reject("adjudication-not-admissible");
    }

    private ExternalEffectProtocolTransition Reject(string code) => new(this, false, code);
    private static ExternalEffectProtocolTransition Accept(ExternalEffectProtocolState state, string code) => new(state, true, code);
}

/// <summary>Represents one immutable external-effect knowledge transition.</summary>
public sealed record ExternalEffectProtocolTransition
{
    /// <summary>Gets the resulting state.</summary>
    public ExternalEffectProtocolState State { get; }
    /// <summary>Gets whether the transition was accepted.</summary>
    public bool Accepted { get; }
    /// <summary>Gets a bounded stable result code.</summary>
    public string Code { get; }

    internal ExternalEffectProtocolTransition(ExternalEffectProtocolState state, bool accepted, string code) =>
        (State, Accepted, Code) = (state, accepted, code);
}
