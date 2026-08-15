using HPD.Payments.Contracts.ExternalEffect;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Connectors.Simulator.Core;

/// <summary>Names a deterministic provider event understood by the bootstrap simulator.</summary>
public enum SimulatorEventKind
{
    /// <summary>Invalid default event.</summary>
    None = 0,
    /// <summary>The provider rejects the request before its send boundary.</summary>
    Reject,
    /// <summary>The provider accepts the request without delaying its response.</summary>
    Accept,
    /// <summary>The request crosses the irreversible provider send boundary.</summary>
    CrossSendBoundary,
    /// <summary>The response is lost after the send boundary, leaving occurrence unknown.</summary>
    LoseResponse,
    /// <summary>A provider poll reports an occurrence disposition.</summary>
    Poll,
    /// <summary>A webhook reports an occurrence disposition.</summary>
    Webhook,
    /// <summary>A settlement feed reports an occurrence disposition.</summary>
    Settlement,
    /// <summary>The active credential revision changes.</summary>
    RotateCredential,
    /// <summary>The active configuration revision changes.</summary>
    RotateConfiguration,
}

/// <summary>Names what one provider observation says about external occurrence.</summary>
public enum SimulatorOccurrence
{
    /// <summary>No occurrence assertion accompanies the event.</summary>
    None = 0,
    /// <summary>The provider says the external effect occurred.</summary>
    Occurred,
    /// <summary>The provider says the external effect did not occur.</summary>
    NotOccurred,
}

/// <summary>Names settlement-inclusion knowledge without collapsing it into provider occurrence.</summary>
public enum SimulatorSettlementState
{
    /// <summary>No settlement authority evidence has been observed.</summary>
    Unknown = 0,
    /// <summary>Settlement authority reports inclusion.</summary>
    Included,
    /// <summary>Settlement authority reports non-inclusion.</summary>
    NotIncluded,
    /// <summary>Settlement authority reports conflict within its own question.</summary>
    Conflicted,
}

/// <summary>Defines one immutable event at a virtual offset from scenario start.</summary>
public readonly record struct SimulatorEvent
{
    /// <summary>Gets the virtual offset at which the event becomes observable.</summary>
    public TimeSpan Offset { get; }
    /// <summary>Gets the event kind.</summary>
    public SimulatorEventKind Kind { get; }
    /// <summary>Gets the optional occurrence assertion.</summary>
    public SimulatorOccurrence Occurrence { get; }
    /// <summary>Gets the revision value used by a revision-rotation event.</summary>
    public ulong RevisionValue { get; }

    /// <summary>Creates a validated deterministic simulator event.</summary>
    /// <exception cref="ArgumentException">The offset, event kind, occurrence, or revision payload is invalid.</exception>
    public SimulatorEvent(TimeSpan offset, SimulatorEventKind kind, SimulatorOccurrence occurrence = SimulatorOccurrence.None, ulong revisionValue = 0)
    {
        var observation = kind is SimulatorEventKind.Poll or SimulatorEventKind.Webhook or SimulatorEventKind.Settlement;
        var rotation = kind is SimulatorEventKind.RotateCredential or SimulatorEventKind.RotateConfiguration;
        if (offset < TimeSpan.Zero || kind == SimulatorEventKind.None || !Enum.IsDefined(kind) ||
            observation != (occurrence != SimulatorOccurrence.None) || !Enum.IsDefined(occurrence) ||
            rotation != (revisionValue > 0))
            throw new ArgumentException("Simulator events require a non-negative offset and an event-specific payload.");
        Offset = offset; Kind = kind; Occurrence = occurrence; RevisionValue = revisionValue;
    }
}

/// <summary>Defines one bounded, ordered, immutable provider scenario.</summary>
public sealed class SimulatorScenario
{
    /// <summary>Specifies the maximum events retained by one scenario.</summary>
    public const int MaximumEvents = 256;
    private readonly SimulatorEvent[] _events;
    /// <summary>Gets the bounded stable scenario name.</summary>
    public string Name { get; }
    /// <summary>Gets a read-only view of simulator-owned event storage.</summary>
    public IReadOnlyList<SimulatorEvent> Events => Array.AsReadOnly(_events);

    /// <summary>Creates an immutable scenario and takes ownership by copying the event sequence.</summary>
    /// <exception cref="ArgumentException">The name, count, ordering, or event sequence is invalid.</exception>
    public SimulatorScenario(string name, IEnumerable<SimulatorEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (!ScopeId.TryCreate("simulator", "scenario", name, out _)) throw new ArgumentException("Scenario name is invalid.", nameof(name));
        _events = events.ToArray();
        if (_events.Length is 0 or > MaximumEvents || !_events.Select(static x => x.Offset).SequenceEqual(_events.Select(static x => x.Offset).Order()))
            throw new ArgumentException("Scenario events must be non-empty, bounded, and ordered by virtual offset.", nameof(events));
        Name = name;
    }
}

/// <summary>Supplies the exact request and pinned revisions for one simulated attempt.</summary>
public readonly record struct SimulatorRequest
{
    /// <summary>Gets the bounded request correlation token.</summary>
    public string CorrelationId { get; }
    /// <summary>Gets the credential revision pinned by the caller.</summary>
    public Revision CredentialRevision { get; }
    /// <summary>Gets the configuration revision pinned by the caller.</summary>
    public Revision ConfigurationRevision { get; }

    /// <summary>Creates a request whose revisions must match simulator state before dispatch.</summary>
    /// <exception cref="ArgumentException">The correlation token or either revision is invalid.</exception>
    public SimulatorRequest(string correlationId, Revision credentialRevision, Revision configurationRevision)
    {
        if (!ScopeId.TryCreate("simulator", "correlation", correlationId, out _) || !credentialRevision.IsValid || !configurationRevision.IsValid)
            throw new ArgumentException("A simulator request requires a bounded correlation and valid revisions.");
        CorrelationId = correlationId; CredentialRevision = credentialRevision; ConfigurationRevision = configurationRevision;
    }
}

/// <summary>Records one owned, immutable observation in a deterministic simulator trace.</summary>
public sealed record SimulatorTraceEntry
{
    /// <summary>Gets the zero-based trace position.</summary>
    public int Sequence { get; }
    /// <summary>Gets virtual UTC observation time.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>Gets the source event kind.</summary>
    public SimulatorEventKind Event { get; }
    /// <summary>Gets the resulting conservative external-effect state.</summary>
    public ExternalEffectState State { get; }
    /// <summary>Gets a bounded stable diagnostic code.</summary>
    public string Code { get; }

    /// <summary>Creates one immutable trace entry.</summary>
    /// <exception cref="ArgumentException">The sequence, time, event, state, or code is invalid.</exception>
    public SimulatorTraceEntry(int sequence, DateTimeOffset observedAtUtc, SimulatorEventKind @event, ExternalEffectState state, string code)
    {
        if (sequence < 0 || observedAtUtc.Offset != TimeSpan.Zero || @event == SimulatorEventKind.None || !Enum.IsDefined(@event) ||
            state == ExternalEffectState.None || !Enum.IsDefined(state) || !ScopeId.TryCreate("simulator", "trace-code", code, out _))
            throw new ArgumentException("Trace entry fields must be bounded and explicit.");
        Sequence = sequence; ObservedAtUtc = observedAtUtc; Event = @event; State = state; Code = code;
    }
}

/// <summary>Contains the bounded owned result of executing one simulator scenario.</summary>
public sealed class SimulatorResult
{
    private readonly SimulatorTraceEntry[] _trace;
    /// <summary>Gets the final conservative external-effect knowledge state.</summary>
    public ExternalEffectState State { get; }
    /// <summary>Gets whether mutually inconsistent provider observations were retained.</summary>
    public bool HasDisagreement { get; }
    /// <summary>Gets settlement-inclusion knowledge independently from occurrence knowledge.</summary>
    public SimulatorSettlementState SettlementState { get; }
    /// <summary>Gets whether occurrence and settlement projections expose a cross-authority mismatch requiring reconciliation.</summary>
    public bool HasCrossAuthorityMismatch { get; }
    /// <summary>Gets a read-only view over simulator-owned trace storage.</summary>
    public IReadOnlyList<SimulatorTraceEntry> Trace => Array.AsReadOnly(_trace);

    internal SimulatorResult(ExternalEffectState state, bool hasDisagreement, SimulatorSettlementState settlementState,
        bool hasCrossAuthorityMismatch, SimulatorTraceEntry[] trace)
    {
        State = state; HasDisagreement = hasDisagreement; SettlementState = settlementState;
        HasCrossAuthorityMismatch = hasCrossAuthorityMismatch; _trace = (SimulatorTraceEntry[])trace.Clone();
    }
}
