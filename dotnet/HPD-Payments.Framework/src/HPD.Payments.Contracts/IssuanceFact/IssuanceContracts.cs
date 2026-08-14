using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.IssuanceFact;

/// <summary>Distinguishes immutable artifact issuance from additive void and supersession facts.</summary>
public enum IssuanceFactKind
{
    /// <summary>Invalid default; it cannot be admitted.</summary>
    None = 0,
    /// <summary>Issues exact artifact bytes under a unique number claim.</summary>
    Issued = 1,
    /// <summary>Voids an earlier issuance without erasing it.</summary>
    Voided = 2,
    /// <summary>Supersedes an earlier issuance with a separately issued artifact.</summary>
    Superseded = 3,
}

/// <summary>Guards uniqueness of an issuer/profile/number tuple at one numbering generation.</summary>
public sealed record IssuanceNumberClaim
{
    /// <summary>Gets the explicit issuer identity.</summary>
    public SemanticId IssuerId { get; }
    /// <summary>Gets the stable numbering profile revision.</summary>
    public Revision NumberingProfileRevision { get; }
    /// <summary>Gets the bounded exact number token claimed by this issuance.</summary>
    public string Number { get; }
    /// <summary>Gets the generation expected for this numbering domain.</summary>
    public OwnerGeneration ExpectedNumberGeneration { get; }

    /// <summary>Creates an immutable numbering guard; uniqueness still requires an atomic adapter compare-bind.</summary>
    /// <param name="issuerId">Explicit issuer identity.</param><param name="numberingProfileRevision">Numbering policy/profile revision.</param>
    /// <param name="number">Exact bounded number token.</param><param name="expectedNumberGeneration">Current generation to compare atomically.</param>
    /// <exception cref="ArgumentException">An identity, revision, generation, or number token is invalid.</exception>
    public IssuanceNumberClaim(SemanticId issuerId, Revision numberingProfileRevision, string number, OwnerGeneration expectedNumberGeneration)
    {
        if (!issuerId.IsValid || !numberingProfileRevision.IsValid || !expectedNumberGeneration.IsValid ||
            !ScopeId.TryCreate("number", "number", number, out _)) throw new ArgumentException("Invalid issuance number claim.");
        IssuerId = issuerId; NumberingProfileRevision = numberingProfileRevision; Number = number; ExpectedNumberGeneration = expectedNumberGeneration;
    }
}

/// <summary>Commands Issuance Fact authority to issue, void, or supersede an exact immutable artifact.</summary>
/// <remarks>Artifact bytes are defensively owned. Rendering, localization, tax, accounting, statutory acceptance, and delivery remain external or separately evidenced.</remarks>
public sealed record RecordIssuanceCommand
{
    /// <summary>Gets this immutable issuance-fact identity.</summary>
    public SemanticId FactId { get; }
    /// <summary>Gets the stable economic-document lineage identity.</summary>
    public SemanticId ArtifactId { get; }
    /// <summary>Gets the exact source manifest identity used to render or justify the artifact.</summary>
    public SemanticId SourceManifestId { get; }
    /// <summary>Gets the exact canonical source-manifest digest.</summary>
    public CanonicalDigest SourceManifestDigest { get; }
    /// <summary>Gets the issue, void, or supersede operation.</summary>
    public IssuanceFactKind Kind { get; }
    /// <summary>Gets the number uniqueness guard.</summary>
    public IssuanceNumberClaim NumberClaim { get; }
    /// <summary>Gets the exact owned bytes; callers receive copies rather than retained aliases.</summary>
    public OwnedClassifiedBytes ArtifactBytes { get; }
    /// <summary>Gets the digest computed over the exact artifact bytes.</summary>
    public CanonicalDigest ArtifactDigest { get; }
    /// <summary>Gets the named issue time.</summary>
    public NamedTime IssuedAt { get; }
    /// <summary>Gets the earlier issuance fact being voided or superseded; absent only for initial issuance.</summary>
    public SemanticId? PriorIssuanceFactId { get; }

    /// <summary>Creates a command and verifies that its digest binds the supplied owned bytes exactly.</summary>
    /// <param name="factId">Identity of this issuance fact.</param><param name="artifactId">Stable artifact lineage identity.</param>
    /// <param name="sourceManifestId">Exact render/source manifest identity.</param><param name="sourceManifestDigest">Digest binding that manifest.</param>
    /// <param name="kind">Issue, void, or supersede operation.</param><param name="numberClaim">Issuer/profile/number uniqueness guard.</param>
    /// <param name="artifactBytes">Owned classified bytes, defensively recopied by this command.</param><param name="artifactDigest">Digest that must match those exact bytes.</param>
    /// <param name="issuedAt">Named issue time.</param><param name="priorIssuanceFactId">Required earlier fact for void/supersession and forbidden for initial issue.</param>
    /// <exception cref="ArgumentException">Scope, kind, lineage, issue time, classification, or exact-byte digest binding is invalid.</exception>
    public RecordIssuanceCommand(SemanticId factId, SemanticId artifactId, SemanticId sourceManifestId,
        CanonicalDigest sourceManifestDigest, IssuanceFactKind kind, IssuanceNumberClaim numberClaim,
        OwnedClassifiedBytes artifactBytes, CanonicalDigest artifactDigest, NamedTime issuedAt,
        SemanticId? priorIssuanceFactId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceManifestDigest); ArgumentNullException.ThrowIfNull(numberClaim);
        ArgumentNullException.ThrowIfNull(artifactBytes); ArgumentNullException.ThrowIfNull(artifactDigest);
        var sameScope = factId.IsValid && artifactId.IsValid && sourceManifestId.IsValid &&
            factId.Scope == artifactId.Scope && artifactId.Scope == sourceManifestId.Scope && numberClaim.IssuerId.Scope == artifactId.Scope;
        var validKind = kind != IssuanceFactKind.None && Enum.IsDefined(kind);
        var priorValid = priorIssuanceFactId is { } p && p.IsValid && p.Scope == artifactId.Scope;
        var needsPrior = kind != IssuanceFactKind.Issued;
        var computed = CanonicalDigest.Sha256(artifactDigest.Profile, artifactBytes.CopyBytes());
        if (!sameScope || !validKind || needsPrior != priorValid || !issuedAt.IsValid || issuedAt.Kind != TimeKind.Issue ||
            artifactBytes.Length == 0 || !computed.Equals(artifactDigest))
            throw new ArgumentException("Invalid issuance scope, lineage, issue time, bytes, or digest binding.");
        FactId = factId; ArtifactId = artifactId; SourceManifestId = sourceManifestId; SourceManifestDigest = sourceManifestDigest;
        Kind = kind; NumberClaim = numberClaim; ArtifactBytes = new OwnedClassifiedBytes(artifactBytes.CopyBytes(), artifactBytes.Mark, artifactBytes.Length);
        ArtifactDigest = artifactDigest; IssuedAt = issuedAt; PriorIssuanceFactId = priorIssuanceFactId;
    }
}

/// <summary>Records one admitted immutable issuance fact and both resulting owner generations.</summary>
public sealed record IssuanceFactRecord
{
    /// <summary>Gets the command retained as the exact immutable fact payload.</summary>
    public RecordIssuanceCommand Command { get; }
    /// <summary>Gets the resulting artifact-lineage generation.</summary>
    public OwnerGeneration ArtifactGeneration { get; }
    /// <summary>Gets the resulting numbering-domain generation.</summary>
    public OwnerGeneration NumberGeneration { get; }
    /// <summary>Gets local durable record time, independent from issue time.</summary>
    public NamedTime RecordedAt { get; }

    /// <summary>Creates an admitted record only when both guarded generations advance exactly once.</summary>
    /// <param name="command">Validated immutable issuance command.</param><param name="expectedArtifactGeneration">Current artifact-lineage generation.</param>
    /// <param name="artifactGeneration">Exact successor artifact generation.</param><param name="numberGeneration">Exact successor numbering generation.</param>
    /// <param name="recordedAt">Named local durable record time.</param>
    /// <exception cref="ArgumentException">A generation or record time violates the guarded transition.</exception>
    public IssuanceFactRecord(RecordIssuanceCommand command, OwnerGeneration expectedArtifactGeneration,
        OwnerGeneration artifactGeneration, OwnerGeneration numberGeneration, NamedTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!expectedArtifactGeneration.TryNext(out var nextArtifact) || artifactGeneration != nextArtifact ||
            !command.NumberClaim.ExpectedNumberGeneration.TryNext(out var nextNumber) || numberGeneration != nextNumber ||
            !recordedAt.IsValid || recordedAt.Kind != TimeKind.Record)
            throw new ArgumentException("Issuance generations must be exact guarded successors and record time must be named Record time.");
        Command = command; ArtifactGeneration = artifactGeneration; NumberGeneration = numberGeneration; RecordedAt = recordedAt;
    }
}

/// <summary>Names closed Issuance Fact admission outcomes without claiming rendering or statutory acceptance.</summary>
public enum IssuanceAdmissionKind
{
    /// <summary>Invalid default result.</summary>
    None = 0,
    /// <summary>The issuance fact was appended.</summary>
    Admitted,
    /// <summary>The exact fact and digest were already present.</summary>
    Replay,
    /// <summary>Artifact lineage, number uniqueness, identity, or digest conflicted.</summary>
    Conflict,
    /// <summary>Current numbering or artifact evidence could not be established.</summary>
    Unknown,
    /// <summary>The fact was rejected without owner mutation.</summary>
    Rejected,
}

/// <summary>Returns an issuance record only for admitted/replay outcomes and a bounded code otherwise.</summary>
public sealed record IssuanceAdmissionResult
{
    /// <summary>Gets the exact outcome.</summary>
    public IssuanceAdmissionKind Kind { get; }
    /// <summary>Gets the immutable record for admitted or replay outcomes.</summary>
    public IssuanceFactRecord? Record { get; }
    /// <summary>Gets the bounded stable diagnostic for non-record outcomes.</summary>
    public string? Code { get; }
    private IssuanceAdmissionResult(IssuanceAdmissionKind kind, IssuanceFactRecord? record, string? code) => (Kind, Record, Code) = (kind, record, code);

    /// <summary>Creates an admitted or replay result.</summary>
    /// <param name="kind">Admitted or Replay.</param><param name="record">Immutable admitted/replayed record.</param>
    /// <returns>A closed record-bearing result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not Admitted or Replay.</exception>
    public static IssuanceAdmissionResult WithRecord(IssuanceAdmissionKind kind, IssuanceFactRecord record) =>
        kind is IssuanceAdmissionKind.Admitted or IssuanceAdmissionKind.Replay
            ? new(kind, record ?? throw new ArgumentNullException(nameof(record)), null)
            : throw new ArgumentOutOfRangeException(nameof(kind));

    /// <summary>Creates a conflict, unknown, or rejected result without fabricating an issuance record.</summary>
    /// <param name="kind">Conflict, Unknown, or Rejected.</param><param name="code">Bounded stable diagnostic token.</param>
    /// <returns>A closed non-record result.</returns>
    /// <exception cref="ArgumentException">The kind or code is invalid.</exception>
    public static IssuanceAdmissionResult WithoutRecord(IssuanceAdmissionKind kind, string code) =>
        kind is IssuanceAdmissionKind.Conflict or IssuanceAdmissionKind.Unknown or IssuanceAdmissionKind.Rejected && ScopeId.TryCreate("code", "code", code, out _)
            ? new(kind, null, code)
            : throw new ArgumentException("A non-record result requires a closed non-success kind and bounded code.");
}
