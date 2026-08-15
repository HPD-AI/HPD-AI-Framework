using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.PublicationObligation;

/// <summary>Names an audience-specific publication lifecycle disposition.</summary>
public enum PublicationDisposition
{
    /// <summary>Invalid default disposition.</summary>
    None = 0,
    /// <summary>Delivery to the audience is durably required.</summary>
    Required,
    /// <summary>A bounded delivery attempt was recorded without acknowledgement.</summary>
    Attempted,
    /// <summary>The audience acknowledgement contract was satisfied.</summary>
    Acknowledged,
    /// <summary>Another delivery is required after a retained attempt.</summary>
    RedeliveryRequired,
    /// <summary>The attempt budget is exhausted.</summary>
    Exhausted,
    /// <summary>External-recipient or derivative residue remains after local completion.</summary>
    Residual,
}

/// <summary>Defines one immutable publication obligation for one source fact and one exact audience.</summary>
public sealed record PublicationObligationFact
{
    /// <summary>Gets the publication identity.</summary>
    public SemanticId PublicationId { get; }
    /// <summary>Gets the immutable source fact identity.</summary>
    public SemanticId SourceFactId { get; }
    /// <summary>Gets the bounded audience identity token.</summary>
    public string Audience { get; }
    /// <summary>Gets the bounded stream identity token.</summary>
    public string Stream { get; }
    /// <summary>Gets the source fact's canonical payload digest.</summary>
    public CanonicalDigest PayloadDigest { get; }
    /// <summary>Gets the acknowledgement contract version.</summary>
    public ContractVersion AcknowledgementVersion { get; }
    /// <summary>Gets the durable record time at which the obligation became discoverable.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an audience-specific publication obligation.</summary>
    /// <exception cref="ArgumentException">Scope, token, version, or record time is invalid.</exception>
    public PublicationObligationFact(SemanticId publicationId, SemanticId sourceFactId, string audience, string stream,
        CanonicalDigest payloadDigest, ContractVersion acknowledgementVersion, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(payloadDigest);
        if (!publicationId.IsValid || !sourceFactId.IsValid || publicationId.Scope != sourceFactId.Scope ||
            !ScopeId.TryCreate("token", "audience", audience, out _) || !ScopeId.TryCreate("token", "stream", stream, out _) ||
            !acknowledgementVersion.IsValid || !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("Publication obligation requires one scope, bounded audience/stream, version, and Record time.");
        PublicationId = publicationId; SourceFactId = sourceFactId; Audience = audience; Stream = stream;
        PayloadDigest = payloadDigest; AcknowledgementVersion = acknowledgementVersion; RecordedAt = recordedAt;
    }
}

/// <summary>Records an append-only, audience-specific delivery or acknowledgement fact.</summary>
public sealed record PublicationDeliveryFact
{
    /// <summary>Gets the publication obligation.</summary>
    public PublicationObligationFact Obligation { get; }
    /// <summary>Gets the immutable delivery identity.</summary>
    public SemanticId DeliveryId { get; }
    /// <summary>Gets the delivery attempt number; zero only for Required.</summary>
    public uint Attempt { get; }
    /// <summary>Gets the explicit audience-specific disposition.</summary>
    public PublicationDisposition Disposition { get; }
    /// <summary>Gets the endpoint/signature/request/response or acknowledgement evidence digest.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets the named delivery or acknowledgement time.</summary>
    public NamedTime OccurredAt { get; }
    /// <summary>Gets the bounded delivery, acknowledgement, exhaustion, or residue code.</summary>
    public string Code { get; }

    /// <summary>Creates an immutable publication-delivery fact.</summary>
    /// <exception cref="ArgumentException">Scope, attempt, disposition, time, or code is invalid.</exception>
    public PublicationDeliveryFact(PublicationObligationFact obligation, SemanticId deliveryId, uint attempt,
        PublicationDisposition disposition, CanonicalDigest evidenceDigest, NamedTime occurredAt, string code)
    {
        ArgumentNullException.ThrowIfNull(obligation); ArgumentNullException.ThrowIfNull(evidenceDigest);
        var required = disposition == PublicationDisposition.Required;
        var expectedTime = disposition == PublicationDisposition.Acknowledged ? TimeKind.Acknowledged : TimeKind.Dispatch;
        if (!deliveryId.IsValid || deliveryId.Scope != obligation.PublicationId.Scope || disposition == PublicationDisposition.None || !Enum.IsDefined(disposition) ||
            required != (attempt == 0) || !occurredAt.IsValid || occurredAt.Kind != expectedTime || !ScopeId.TryCreate("token", "code", code, out _))
            throw new ArgumentException("Publication delivery requires matching scope, explicit disposition, coherent attempt, named time, and bounded code.");
        Obligation = obligation; DeliveryId = deliveryId; Attempt = attempt; Disposition = disposition;
        EvidenceDigest = evidenceDigest; OccurredAt = occurredAt; Code = code;
    }
}
