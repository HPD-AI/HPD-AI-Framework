
namespace HPD.Base;

using System.Collections.Immutable;
using System.Security.Cryptography;

/// <summary>Defines the ibase policy orchestrator contract.</summary>
public interface IBasePolicyOrchestrator
{
    /// <summary>Executes the evaluate read async operation.</summary>
    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateReadAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the evaluate write async operation.</summary>
    ValueTask<OperationResult<BasePolicyEvaluation>> EvaluateWriteAsync(
        BasePolicyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Represents a base policy request.</summary>
public sealed record BasePolicyRequest
{
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the collection.</summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>Gets or sets the resource kind.</summary>
    public required PolicyResourceKind ResourceKind { get; init; }
    /// <summary>Gets or sets the query.</summary>
    public RecordQuery? Query { get; init; }
    /// <summary>Gets or sets the existing record.</summary>
    public RecordEnvelope? ExistingRecord { get; init; }
    /// <summary>Gets or sets the proposed payload.</summary>
    public RecordPayload? ProposedPayload { get; init; }
    /// <summary>Gets or sets the proposed record.</summary>
    public RecordEnvelope? ProposedRecord { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public RecordId? RecordId { get; init; }
    /// <summary>Gets or sets the grants.</summary>
    public AccessGrant[]? Grants { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public Dictionary<string, string>? PolicyRefs { get; init; }
    /// <summary>Gets the optional stable vector-index identifier.</summary>
    public string? VectorIndexId { get; init; }
    /// <summary>Gets the optional stable vector-space identifier.</summary>
    public string? VectorSpaceId { get; init; }
    /// <summary>Gets the optional stable exported logical-subject contract identifier.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the optional positive exported logical-subject contract version.</summary>
    public int? SubjectContractVersion { get; init; }
}

/// <summary>Represents a base policy evaluation.</summary>
public sealed record BasePolicyEvaluation
{
    /// <summary>Gets or sets the decision.</summary>
    public required PolicyDecision Decision { get; init; }
    /// <summary>Gets or sets the effective record filter.</summary>
    public FilterExpression? EffectiveRecordFilter { get; init; }
    /// <summary>Gets the effective write check.</summary>
    public FilterExpression? EffectiveWriteCheck { get; init; }
    /// <summary>Gets or sets the effective read mask.</summary>
    public FieldMask? EffectiveReadMask { get; init; }
    /// <summary>Gets or sets the effective write mask.</summary>
    public FieldMask? EffectiveWriteMask { get; init; }
    /// <summary>Gets immutable installed-graph authority for an admitted mutation decision.</summary>
    public BasePolicyEvaluationAuthority? Authority { get; init; }
}

/// <summary>Binds one admitted policy decision to its immutable installed graph.</summary>
public sealed record BasePolicyEvaluationAuthority
{
    /// <summary>Gets the positive policy graph generation.</summary>
    public required long PolicyGraphGeneration { get; init; }
    /// <summary>Gets the exact 32-byte policy owner checksum.</summary>
    public required ImmutableArray<byte> PolicyOwnerChecksum { get; init; }
    /// <summary>Gets exact admitted grant evidence in canonical set order.</summary>
    public required ImmutableArray<BaseAdmittedGrantAuthority> AdmittedGrants { get; init; }
    /// <summary>Gets applied policies in canonical evaluator order.</summary>
    public required ImmutableArray<BaseAppliedPolicyAuthority> AppliedPolicies { get; init; }
    /// <summary>Gets the deeply owned effective constraints.</summary>
    public required BasePolicyConstraintAuthority Constraints { get; init; }
    /// <summary>Gets the exact authority checksum.</summary>
    public required BasePolicyEvaluationAuthorityChecksum Checksum { get; init; }
    internal ImmutableArray<BaseAdmittedGrantSemantics> GrantSemantics { get; init; } = [];
}

internal sealed record BaseAdmittedGrantSemantics(
    string GrantId,
    int GrantVersion,
    ImmutableArray<byte> GrantRegistrationChecksum,
    ImmutableArray<byte> GrantChecksum,
    AccessGrant Grant);

/// <summary>Contains one exact admitted grant receipt.</summary>
public sealed record BaseAdmittedGrantAuthority
{
    /// <summary>Gets the stable grant identity.</summary>
    public required string GrantId { get; init; }
    /// <summary>Gets the positive grant version.</summary>
    public required int GrantVersion { get; init; }
    /// <summary>Gets the 32-byte registration checksum.</summary>
    public required ImmutableArray<byte> GrantRegistrationChecksum { get; init; }
    /// <summary>Gets the 32-byte exact emitted-grant checksum.</summary>
    public required ImmutableArray<byte> GrantChecksum { get; init; }
}

/// <summary>Contains one applied graph-owned policy receipt.</summary>
public sealed record BaseAppliedPolicyAuthority
{
    /// <summary>Gets the canonical composition order.</summary>
    public required int CompositionOrder { get; init; }
    /// <summary>Gets the stable policy identity.</summary>
    public required string PolicyId { get; init; }
    /// <summary>Gets the positive policy version.</summary>
    public required int PolicyVersion { get; init; }
    /// <summary>Gets the exact 32-byte policy checksum.</summary>
    public required ImmutableArray<byte> PolicyChecksum { get; init; }
}

/// <summary>Owns the normalized constraints used by one admitted decision.</summary>
public sealed record BasePolicyConstraintAuthority
{
    /// <summary>Gets the effective record filter.</summary>
    public FilterExpression? EffectiveRecordFilter { get; init; }
    /// <summary>Gets the effective write check.</summary>
    public FilterExpression? EffectiveWriteCheck { get; init; }
    /// <summary>Gets the effective read mask.</summary>
    public FieldMask? EffectiveReadMask { get; init; }
    /// <summary>Gets the effective write mask.</summary>
    public FieldMask? EffectiveWriteMask { get; init; }
}

/// <summary>Opaque immutable 32-byte policy-evaluation authority checksum.</summary>
public sealed class BasePolicyEvaluationAuthorityChecksum : IEquatable<BasePolicyEvaluationAuthorityChecksum>
{
    /// <summary>Gets the required checksum length.</summary>
    public const int Length = 32;
    private readonly byte[] _value;
    private BasePolicyEvaluationAuthorityChecksum(byte[] value) => _value = value;
    /// <summary>Creates a checksum from exactly 32 bytes.</summary>
    public static BasePolicyEvaluationAuthorityChecksum Create(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length) throw new ArgumentException("A policy authority checksum must contain exactly 32 bytes.", nameof(value));
        return new BasePolicyEvaluationAuthorityChecksum(value.ToArray());
    }
    /// <summary>Copies the checksum to a destination.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        _value.CopyTo(destination);
    }
    /// <summary>Returns a defensive checksum copy.</summary>
    public byte[] ToArray() => _value.ToArray();
    /// <inheritdoc />
    public bool Equals(BasePolicyEvaluationAuthorityChecksum? other) => other is not null && CryptographicOperations.FixedTimeEquals(_value, other._value);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BasePolicyEvaluationAuthorityChecksum other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(_value, 0);
}

/// <summary>Defines the ibase record redactor contract.</summary>
public interface IBaseRecordRedactor
{
    /// <summary>Executes the redact record operation.</summary>
    RecordEnvelope RedactRecord(
        RecordEnvelope record,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);

    /// <summary>Executes the redact page operation.</summary>
    RecordPage RedactPage(
        RecordPage page,
        CollectionDefinition collection,
        BasePolicyEvaluation policy,
        VisibilityLevel view);
}
