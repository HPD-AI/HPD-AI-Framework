using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.CapabilityEvidence;

/// <summary>Names the explicit support disposition asserted by one contextual capability-evidence fact.</summary>
public enum CapabilityDisposition
{
    /// <summary>Invalid default disposition.</summary>
    None = 0,
    /// <summary>The exact context is supported for the stated validity interval.</summary>
    Positive,
    /// <summary>The exact context is explicitly unsupported.</summary>
    Negative,
    /// <summary>Support exists only when the retained condition is satisfied.</summary>
    Conditional,
    /// <summary>Previously admitted evidence is outside its validity interval.</summary>
    Expired,
    /// <summary>The evidence issuer explicitly withdrew the prior assertion.</summary>
    Withdrawn,
    /// <summary>Incomparable current evidence disagrees; support is not established.</summary>
    Conflicted,
}

/// <summary>Identifies the complete connector context to which capability evidence applies.</summary>
/// <remarks>Registration, loading, health, or interface presence does not create this context or prove support.</remarks>
public sealed record CapabilityContext
{
    /// <summary>Gets the scoped provider-account identity.</summary>
    public SemanticId ProviderAccountId { get; }
    /// <summary>Gets the bounded normalized operation token.</summary>
    public string Operation { get; }
    /// <summary>Gets the bounded provider API token.</summary>
    public string Api { get; }
    /// <summary>Gets the exact code revision.</summary>
    public Revision CodeRevision { get; }
    /// <summary>Gets the exact configuration revision.</summary>
    public Revision ConfigurationRevision { get; }
    /// <summary>Gets the exact credential generation.</summary>
    public Revision CredentialRevision { get; }
    /// <summary>Gets the bounded execution-lane token.</summary>
    public string Lane { get; }
    /// <summary>Gets the bounded runtime-identifier token.</summary>
    public string RuntimeIdentifier { get; }

    /// <summary>Creates a fully scoped capability context.</summary>
    /// <exception cref="ArgumentException">An identity, token, or revision is invalid.</exception>
    public CapabilityContext(SemanticId providerAccountId, string operation, string api, Revision codeRevision,
        Revision configurationRevision, Revision credentialRevision, string lane, string runtimeIdentifier)
    {
        if (!providerAccountId.IsValid || providerAccountId.Provider is null ||
            !ScopeId.TryCreate("token", "context", operation, out _) || !ScopeId.TryCreate("token", "context", api, out _) ||
            !codeRevision.IsValid || !configurationRevision.IsValid || !credentialRevision.IsValid ||
            !ScopeId.TryCreate("token", "context", lane, out _) || !ScopeId.TryCreate("token", "context", runtimeIdentifier, out _))
            throw new ArgumentException("Capability context requires an external provider account and bounded operation/API/revision/lane/RID components.");
        ProviderAccountId = providerAccountId; Operation = operation; Api = api; CodeRevision = codeRevision;
        ConfigurationRevision = configurationRevision; CredentialRevision = credentialRevision; Lane = lane; RuntimeIdentifier = runtimeIdentifier;
    }
}

/// <summary>Records an immutable, contextual capability assertion without turning existence into support.</summary>
public sealed record CapabilityEvidenceFact
{
    /// <summary>Gets the immutable evidence identity.</summary>
    public SemanticId EvidenceId { get; }
    /// <summary>Gets the exact context certified by the evidence.</summary>
    public CapabilityContext Context { get; }
    /// <summary>Gets the explicit positive, negative, conditional, expired, withdrawn, or conflicted disposition.</summary>
    public CapabilityDisposition Disposition { get; }
    /// <summary>Gets the bounded condition or reason code.</summary>
    public string Reason { get; }
    /// <summary>Gets when the evidence was verified.</summary>
    public NamedTime VerifiedAt { get; }
    /// <summary>Gets when the evidence stops being current.</summary>
    public NamedTime ValidUntil { get; }
    /// <summary>Gets the exact evidence payload digest.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets the prior evidence fact superseded or contradicted, when present.</summary>
    public SemanticId? PredecessorEvidenceId { get; }

    /// <summary>Creates one immutable capability-evidence fact.</summary>
    /// <exception cref="ArgumentException">Scope, disposition, reason, time, or lineage is invalid.</exception>
    public CapabilityEvidenceFact(SemanticId evidenceId, CapabilityContext context, CapabilityDisposition disposition,
        string reason, NamedTime verifiedAt, NamedTime validUntil, CanonicalDigest evidenceDigest, SemanticId? predecessorEvidenceId = null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(evidenceDigest);
        var requiresPrior = disposition is CapabilityDisposition.Expired or CapabilityDisposition.Withdrawn or CapabilityDisposition.Conflicted;
        var priorValid = predecessorEvidenceId is { } prior && prior.IsValid && prior.Scope == evidenceId.Scope;
        if (!evidenceId.IsValid || evidenceId.Scope != context.ProviderAccountId.Scope || disposition == CapabilityDisposition.None || !Enum.IsDefined(disposition) ||
            !ScopeId.TryCreate("token", "reason", reason, out _) || !verifiedAt.IsValid || verifiedAt.Kind != TimeKind.Verify ||
            !validUntil.IsValid || validUntil.Kind != TimeKind.Expiry || validUntil.Value < verifiedAt.Value || requiresPrior != priorValid)
            throw new ArgumentException("Capability evidence requires matching scope, explicit disposition, bounded reason, ordered Verify/Expiry times, and valid lineage.");
        EvidenceId = evidenceId; Context = context; Disposition = disposition; Reason = reason; VerifiedAt = verifiedAt;
        ValidUntil = validUntil; EvidenceDigest = evidenceDigest; PredecessorEvidenceId = predecessorEvidenceId;
    }

    /// <summary>Returns whether this exact fact establishes support at the supplied UTC instant.</summary>
    /// <remarks>Only positive evidence can establish support; conditional and conflicted evidence require separate adjudication.</remarks>
    /// <param name="atUtc">The UTC instant to compare with the retained validity interval.</param>
    /// <returns><see langword="true"/> only for current positive evidence.</returns>
    /// <exception cref="ArgumentException"><paramref name="atUtc"/> is not UTC.</exception>
    public bool EstablishesSupport(DateTimeOffset atUtc)
    {
        if (atUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Capability evaluation time must be UTC.", nameof(atUtc));
        return Disposition == CapabilityDisposition.Positive && atUtc >= VerifiedAt.Value && atUtc <= ValidUntil.Value;
    }
}
