using HPD.Agent.Authority;

namespace HPD.Agent.Middleware;

/// <summary>Identifies the closed category of one owner-neutral control observation.</summary>
public enum AgentControlKind : ushort
{
    /// <summary>Observes a runtime lifecycle boundary.</summary>
    RuntimeLifecycle = 1,
    /// <summary>Observes a semantic-admission boundary.</summary>
    SemanticAdmission = 2,
    /// <summary>Observes a tool transaction without granting dispatch.</summary>
    ToolObservation = 3,
}

/// <summary>Contains one bounded immutable control observation for a neutral Agent hook.</summary>
public sealed class AgentControlEnvelope
{
    /// <summary>Defines the maximum encoded owner payload size.</summary>
    public const int MaximumPayloadBytes = 65_536;

    /// <summary>Initializes an immutable control envelope.</summary>
    /// <param name="envelopeId">The non-default operation identity for this observation.</param>
    /// <param name="session">The optional authority session to which the observation belongs.</param>
    /// <param name="kind">The registered control category.</param>
    /// <param name="versionedPayload">A bounded owner-encoded payload copied by this constructor.</param>
    /// <param name="payloadType">The bounded declared payload type token.</param>
    /// <param name="schemaVersion">The positive payload schema version.</param>
    /// <param name="causalPosition">The optional durable causal position.</param>
    /// <exception cref="ArgumentException">An identity, session, kind, payload type, or causal scope is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The payload is empty or exceeds <see cref="MaximumPayloadBytes"/>, or the schema version is zero.</exception>
    public AgentControlEnvelope(
        OperationId envelopeId,
        SessionAuthorityStampV1? session,
        AgentControlKind kind,
        ReadOnlyMemory<byte> versionedPayload,
        BoundedAscii payloadType,
        ushort schemaVersion,
        JournalPositionV1? causalPosition = null)
    {
        if (!envelopeId.IsValid) throw new ArgumentException("A control envelope identity is required.", nameof(envelopeId));
        if (session is { IsValid: false }) throw new ArgumentException("The optional session is invalid.", nameof(session));
        if (!Enum.IsDefined(kind)) throw new ArgumentException("A registered control kind is required.", nameof(kind));
        if (versionedPayload.Length is 0 or > MaximumPayloadBytes) throw new ArgumentOutOfRangeException(nameof(versionedPayload));
        if (!payloadType.IsValid) throw new ArgumentException("A declared payload type is required.", nameof(payloadType));
        if (schemaVersion == 0) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (causalPosition is not null && session is null || causalPosition is { IsValid: false } ||
            causalPosition is { } causal && session is { } scoped && causal.Session != scoped)
            throw new ArgumentException("The causal position must be valid and belong to the declared session.", nameof(causalPosition));
        EnvelopeId = envelopeId;
        Session = session;
        Kind = kind;
        _payload = versionedPayload.ToArray();
        PayloadType = payloadType;
        SchemaVersion = schemaVersion;
        CausalPosition = causalPosition;
    }

    private readonly byte[] _payload;

    /// <summary>Gets the operation identity of this observation.</summary>
    public OperationId EnvelopeId { get; }
    /// <summary>Gets the optional authority session.</summary>
    public SessionAuthorityStampV1? Session { get; }
    /// <summary>Gets the registered control category.</summary>
    public AgentControlKind Kind { get; }
    /// <summary>Gets a defensive copy of the privately owned versioned payload bytes.</summary>
    public ReadOnlyMemory<byte> VersionedPayload => _payload.ToArray();
    /// <summary>Gets the declared payload type token.</summary>
    public BoundedAscii PayloadType { get; }
    /// <summary>Gets the positive payload schema version.</summary>
    public ushort SchemaVersion { get; }
    /// <summary>Gets the optional durable causal position.</summary>
    public JournalPositionV1? CausalPosition { get; }
}
