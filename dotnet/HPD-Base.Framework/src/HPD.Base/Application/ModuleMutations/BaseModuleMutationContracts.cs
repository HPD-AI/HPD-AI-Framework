using System.Collections.Immutable;
using System.Security.Cryptography;

#pragma warning disable CS1591 // XML documentation is completed with the public-surface slice.

namespace HPD.Base;

/// <summary>Audience permitted to execute one registered module mutation.</summary>
public enum BaseModuleMutationAudience { Service = 0, System = 1 }
/// <summary>Closed committed module-mutation outcome.</summary>
public enum BaseModuleMutationOutcome { Committed = 0, Duplicate = 1 }
/// <summary>Presence required by one record capture.</summary>
public enum BaseModuleCapturePresence { RequirePresent = 0, RequireMissing = 1, AllowEither = 2 }
/// <summary>Absence requirement for one generation capture.</summary>
public enum BaseModuleGenerationAbsenceBehavior { RequireExisting = 0, RequireMissing = 1, AllowEither = 2 }
/// <summary>Presence test for one captured field.</summary>
public enum BaseModuleFieldPresenceTest { Missing = 0, Null = 1, PresentValue = 2 }
/// <summary>Logical guard operator.</summary>
public enum BaseModuleLogicalGuardKind { And = 0, Or = 1, Not = 2 }
/// <summary>Generation comparison kind.</summary>
public enum BaseModuleGenerationComparisonKind { MustExist = 0, MustBeMissing = 1, MustEqual = 2 }
/// <summary>Checked numeric expression operator.</summary>
public enum BaseModuleNumericOperator
{
    IntegerAddChecked = 0, IntegerSubtractChecked = 1, DecimalAddChecked = 2,
    DecimalSubtractChecked = 3, DecimalMultiplyChecked = 4, Minimum = 5, Maximum = 6,
}
/// <summary>Decimal rounding mode.</summary>
public enum BaseModuleDecimalRounding { ToEven = 0, AwayFromZero = 1, TowardZero = 2 }

/// <summary>Immutable checksum for one registered module mutation.</summary>
public sealed class BaseModuleMutationChecksum : IEquatable<BaseModuleMutationChecksum>
{
    public const int Length = 32;
    private readonly byte[] _value;
    private BaseModuleMutationChecksum(byte[] value) => _value = value;
    public static BaseModuleMutationChecksum Create(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length) throw new ArgumentException("A module mutation checksum must contain exactly 32 bytes.", nameof(value));
        return new(value.ToArray());
    }
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        _value.CopyTo(destination);
    }
    public byte[] ToArray() => _value.ToArray();
    public bool Equals(BaseModuleMutationChecksum? other) => other is not null && CryptographicOperations.FixedTimeEquals(_value, other._value);
    public override bool Equals(object? obj) => obj is BaseModuleMutationChecksum other && Equals(other);
    public override int GetHashCode() => BitConverter.ToInt32(_value, 0);
}

/// <summary>Receipt retention contract for one operation.</summary>
public sealed record BaseModuleMutationReceiptPolicy
{
    public required TimeSpan Lifetime { get; init; }
    public required int FormatVersion { get; init; }
}

/// <summary>Safety envelope declared by one registered operation.</summary>
public sealed record BaseModuleMutationLimits
{
    public required int MaximumCaptures { get; init; }
    public required int MaximumRecordCaptures { get; init; }
    public required int MaximumRelationTargetCaptures { get; init; }
    public required int MaximumGenerationCaptures { get; init; }
    public required int MaximumRecordMutations { get; init; }
    public required int MaximumGenerationReads { get; init; }
    public required int MaximumGenerationComparisons { get; init; }
    public required int MaximumGenerationIncrements { get; init; }
    public required int MaximumGuardNodes { get; init; }
    public required int MaximumGuardDepth { get; init; }
    public required int MaximumStatements { get; init; }
    public required int MaximumBranches { get; init; }
    public required int MaximumExpressionNodes { get; init; }
    public required int MaximumReadIntervals { get; init; }
    public required int MaximumSubjectValidations { get; init; }
    public required int MaximumAuthorityReads { get; init; }
    public required int MaximumRelationChecks { get; init; }
    public required int MaximumUniqueConstraintChecks { get; init; }
    public required long MaximumRequestBytes { get; init; }
    public required long MaximumSelectedBytes { get; init; }
    public required long MaximumGenerationBytes { get; init; }
    public required long MaximumEvidenceBytes { get; init; }
    public required long MaximumWrittenBytes { get; init; }
    public required long MaximumFactBytes { get; init; }
    public required long MaximumJournalBytes { get; init; }
    public required long MaximumReceiptBytes { get; init; }
    public required long MaximumResultBytes { get; init; }
    public required long MaximumTransientBytes { get; init; }
    public required BaseAtomicMutationDeadlines Deadlines { get; init; }
}

/// <summary>Describes one provider's immutable registered module-mutation capability.</summary>
public sealed record BaseModuleMutationCapability
{
    /// <summary>Gets whether registered module mutations are supported.</summary>
    public required bool Supported { get; init; }
    /// <summary>Gets whether the provider supplies serializable execution.</summary>
    public required bool SerializableExecution { get; init; }
    /// <summary>Gets whether receipts survive the provider's declared durability boundary.</summary>
    public required bool DurableReceipts { get; init; }
    /// <summary>Gets whether provider-owned generation cells are supported.</summary>
    public required bool GenerationCells { get; init; }
    /// <summary>Gets whether records, generations, projections, and receipts commit atomically.</summary>
    public required bool AtomicRecordAndGenerationCommit { get; init; }
    /// <summary>Gets the complete provider-certified maxima.</summary>
    public required BaseModuleMutationLimits MaximumLimits { get; init; }
}

/// <summary>Provides the fixed L50 platform safety envelope.</summary>
public static class BaseModuleMutationPlatform
{
    /// <summary>Gets a fresh immutable platform-ceiling value.</summary>
    public static BaseModuleMutationLimits MaximumLimits => new()
    {
        MaximumCaptures = 256, MaximumRecordCaptures = 256, MaximumRelationTargetCaptures = 512,
        MaximumGenerationCaptures = 128, MaximumRecordMutations = 256, MaximumGenerationReads = 128,
        MaximumGenerationComparisons = 128, MaximumGenerationIncrements = 128, MaximumGuardNodes = 1_024,
        MaximumGuardDepth = 32, MaximumStatements = 512, MaximumBranches = 64, MaximumExpressionNodes = 2_048,
        MaximumReadIntervals = 1_024, MaximumSubjectValidations = 1_024, MaximumAuthorityReads = 2_048,
        MaximumRelationChecks = 4_096, MaximumUniqueConstraintChecks = 4_096,
        MaximumRequestBytes = 1_048_576, MaximumSelectedBytes = 16_777_216, MaximumGenerationBytes = 1_048_576,
        MaximumEvidenceBytes = 16_777_216, MaximumWrittenBytes = 16_777_216, MaximumFactBytes = 16_777_216,
        MaximumJournalBytes = 16_777_216, MaximumReceiptBytes = 16_777_216, MaximumResultBytes = 1_048_576,
        MaximumTransientBytes = 32_000_000,
        Deadlines = new BaseAtomicMutationDeadlines
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(30),
            CommitObservationTimeout = TimeSpan.FromSeconds(30), ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
        },
    };
}

internal static class BaseModuleMutationCapabilityContract
{
    internal static bool IsValid(BaseModuleMutationCapability? capability)
    {
        if (capability is not
            { Supported: true, SerializableExecution: true, DurableReceipts: true, GenerationCells: true,
                AtomicRecordAndGenerationCommit: true, MaximumLimits: not null }) return false;
        try
        {
            BaseModuleMutationContractValidator.ValidateLimits(capability.MaximumLimits);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool Supports(BaseModuleMutationLimits required, BaseModuleMutationCapability? capability)
    {
        if (capability is not { Supported: true, SerializableExecution: true, DurableReceipts: true,
                GenerationCells: true, AtomicRecordAndGenerationCommit: true }) return false;
        BaseModuleMutationLimits maximum = capability.MaximumLimits;
        return required.MaximumCaptures <= maximum.MaximumCaptures
            && required.MaximumRecordCaptures <= maximum.MaximumRecordCaptures
            && required.MaximumRelationTargetCaptures <= maximum.MaximumRelationTargetCaptures
            && required.MaximumGenerationCaptures <= maximum.MaximumGenerationCaptures
            && required.MaximumRecordMutations <= maximum.MaximumRecordMutations
            && required.MaximumGenerationReads <= maximum.MaximumGenerationReads
            && required.MaximumGenerationComparisons <= maximum.MaximumGenerationComparisons
            && required.MaximumGenerationIncrements <= maximum.MaximumGenerationIncrements
            && required.MaximumGuardNodes <= maximum.MaximumGuardNodes
            && required.MaximumGuardDepth <= maximum.MaximumGuardDepth
            && required.MaximumStatements <= maximum.MaximumStatements
            && required.MaximumBranches <= maximum.MaximumBranches
            && required.MaximumExpressionNodes <= maximum.MaximumExpressionNodes
            && required.MaximumReadIntervals <= maximum.MaximumReadIntervals
            && required.MaximumSubjectValidations <= maximum.MaximumSubjectValidations
            && required.MaximumAuthorityReads <= maximum.MaximumAuthorityReads
            && required.MaximumRelationChecks <= maximum.MaximumRelationChecks
            && required.MaximumUniqueConstraintChecks <= maximum.MaximumUniqueConstraintChecks
            && required.MaximumRequestBytes <= maximum.MaximumRequestBytes
            && required.MaximumSelectedBytes <= maximum.MaximumSelectedBytes
            && required.MaximumGenerationBytes <= maximum.MaximumGenerationBytes
            && required.MaximumEvidenceBytes <= maximum.MaximumEvidenceBytes
            && required.MaximumWrittenBytes <= maximum.MaximumWrittenBytes
            && required.MaximumFactBytes <= maximum.MaximumFactBytes
            && required.MaximumJournalBytes <= maximum.MaximumJournalBytes
            && required.MaximumReceiptBytes <= maximum.MaximumReceiptBytes
            && required.MaximumResultBytes <= maximum.MaximumResultBytes
            && required.MaximumTransientBytes <= maximum.MaximumTransientBytes
            && required.Deadlines.AcquisitionTimeout <= maximum.Deadlines.AcquisitionTimeout
            && required.Deadlines.TransactionTimeout <= maximum.Deadlines.TransactionTimeout
            && required.Deadlines.CommitObservationTimeout <= maximum.Deadlines.CommitObservationTimeout
            && required.Deadlines.ReceiptResolutionTimeout <= maximum.Deadlines.ReceiptResolutionTimeout;
    }
}

/// <summary>Distinct deadlines within one atomic mutation.</summary>
public sealed record BaseAtomicMutationDeadlines
{
    public required TimeSpan AcquisitionTimeout { get; init; }
    public required TimeSpan TransactionTimeout { get; init; }
    public required TimeSpan CommitObservationTimeout { get; init; }
    public required TimeSpan ReceiptResolutionTimeout { get; init; }
}

/// <summary>Complete graph-owned registered module mutation definition.</summary>
public sealed record BaseRegisteredModuleMutationDefinition
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string OwningModuleId { get; init; }
    public required string GrantId { get; init; }
    public required BaseModuleMutationAudience Audience { get; init; }
    public required string RequestTypeId { get; init; }
    public required string ResultTypeId { get; init; }
    public required ImmutableArray<string> SystemCollectionIds { get; init; }
    public required ImmutableArray<string> GenerationCellIds { get; init; }
    public required ImmutableArray<string> ImportedSubjectContractIds { get; init; }
    public required BaseModuleMutationTemplate Template { get; init; }
    public required BaseModuleMutationLimits Limits { get; init; }
    public required BaseModuleMutationReceiptPolicy ReceiptPolicy { get; init; }
    public required BaseModuleMutationChecksum Checksum { get; init; }
}

/// <summary>Closed immutable operation template.</summary>
public sealed record BaseModuleMutationTemplate
{
    public required ImmutableArray<BaseModuleCapture> Captures { get; init; }
    public required ImmutableArray<BaseModuleGuard> Guards { get; init; }
    public required BaseModuleMutationBlock Body { get; init; }
    public required BaseModuleResultProjection Result { get; init; }
}

public abstract record BaseModuleCapture { public required string Id { get; init; } }
public sealed record BaseModuleRecordCapture : BaseModuleCapture
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public required BaseModuleCapturePresence Presence { get; init; }
}
public sealed record BaseModuleGenerationCapture : BaseModuleCapture
{
    public required string CellId { get; init; }
    public BaseModuleValueExpression? Key { get; init; }
    public required BaseModuleGenerationAbsenceBehavior Absence { get; init; }
}

public abstract record BaseModuleGuard { public required string Id { get; init; } }
public sealed record BaseModuleRecordPresenceGuard : BaseModuleGuard
{
    public required string CaptureId { get; init; }
    public required bool MustBePresent { get; init; }
}
public sealed record BaseModuleRevisionEqualsGuard : BaseModuleGuard
{
    public required string CaptureId { get; init; }
    public required BaseModuleValueExpression Expected { get; init; }
}
public sealed record BaseModuleFieldEqualsGuard : BaseModuleGuard
{
    public required BaseModuleCapturedFieldReference Field { get; init; }
    public required BaseModuleValueExpression Expected { get; init; }
}
public sealed record BaseModuleFieldPresenceGuard : BaseModuleGuard
{
    public required BaseModuleCapturedFieldReference Field { get; init; }
    public required BaseModuleFieldPresenceTest Test { get; init; }
}
public sealed record BaseModuleGenerationGuard : BaseModuleGuard
{
    public required string CaptureId { get; init; }
    public required BaseModuleGenerationComparisonKind Comparison { get; init; }
    public BaseModuleValueExpression? Expected { get; init; }
}
public sealed record BaseModuleLogicalGuard : BaseModuleGuard
{
    public required BaseModuleLogicalGuardKind Kind { get; init; }
    public required ImmutableArray<string> ChildGuardIds { get; init; }
}

public sealed record BaseModuleMutationBlock { public required ImmutableArray<BaseModuleStatement> Statements { get; init; } }
public abstract record BaseModuleStatement { public required string Id { get; init; } }
public sealed record BaseModuleCreateStatement : BaseModuleStatement
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public required BaseModuleObjectExpression Payload { get; init; }
}
public sealed record BaseModulePatchStatement : BaseModuleStatement
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public required BaseModuleObjectExpression Patch { get; init; }
    public BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleReplaceStatement : BaseModuleStatement
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public required BaseModuleObjectExpression Payload { get; init; }
    public BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleDeleteStatement : BaseModuleStatement
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleUpsertStatement : BaseModuleStatement
{
    public required string CollectionId { get; init; }
    public required BaseModuleValueExpression RecordId { get; init; }
    public required BaseModuleObjectExpression Create { get; init; }
    public required BaseModuleObjectExpression Update { get; init; }
    public required RecordUpsertUpdateMode UpdateMode { get; init; }
    public BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleIncrementGenerationStatement : BaseModuleStatement
{
    public required string CaptureId { get; init; }
    public required bool CreateIfAbsent { get; init; }
}
public sealed record BaseModuleIfStatement : BaseModuleStatement
{
    public required string GuardId { get; init; }
    public required BaseModuleMutationBlock WhenTrue { get; init; }
    public required BaseModuleMutationBlock WhenFalse { get; init; }
}
public sealed record BaseModuleRequireStatement : BaseModuleStatement
{
    public required string GuardId { get; init; }
    public required string RequirementId { get; init; }
}

public sealed record BaseModuleRequestPropertyReference
{
    public required ImmutableArray<string> StablePropertyPath { get; init; }
    public required string DeclaredTypeId { get; init; }
}
public sealed record BaseModuleCapturedFieldReference
{
    public required string CaptureId { get; init; }
    public required string StableFieldId { get; init; }
    public required string DeclaredTypeId { get; init; }
}

public abstract record BaseModuleValueExpression
{
    public required string Id { get; init; }
    public required string ResultTypeId { get; init; }
}
public sealed record BaseModuleRequestPropertyExpression : BaseModuleValueExpression { public required BaseModuleRequestPropertyReference Property { get; init; } }
public sealed record BaseModuleConstantExpression : BaseModuleValueExpression { public required ImmutableArray<byte> CanonicalBaseJson { get; init; } }
public sealed record BaseModuleCapturedRecordIdExpression : BaseModuleValueExpression { public required string CaptureId { get; init; } }
public sealed record BaseModuleCapturedRevisionExpression : BaseModuleValueExpression { public required string CaptureId { get; init; } }
public sealed record BaseModuleCapturedFieldExpression : BaseModuleValueExpression { public required BaseModuleCapturedFieldReference Field { get; init; } }
public sealed record BaseModuleCapturedGenerationExpression : BaseModuleValueExpression { public required string CaptureId { get; init; } }
public sealed record BaseModuleCommittedRecordIdExpression : BaseModuleValueExpression { public required string StatementId { get; init; } }
public sealed record BaseModuleCommittedRevisionExpression : BaseModuleValueExpression { public required string StatementId { get; init; } }
public sealed record BaseModuleCommittedUpsertDispositionExpression : BaseModuleValueExpression { public required string StatementId { get; init; } }
public sealed record BaseModuleResultingGenerationExpression : BaseModuleValueExpression { public required string CaptureId { get; init; } }
public sealed record BaseModuleCoalesceExpression : BaseModuleValueExpression { public required ImmutableArray<BaseModuleValueExpression> Values { get; init; } }
public sealed record BaseModuleConditionalExpression : BaseModuleValueExpression
{
    public required string GuardId { get; init; }
    public required BaseModuleValueExpression WhenTrue { get; init; }
    public required BaseModuleValueExpression WhenFalse { get; init; }
}
public sealed record BaseModuleBinaryNumericExpression : BaseModuleValueExpression
{
    public required BaseModuleNumericOperator Operator { get; init; }
    public required BaseModuleValueExpression Left { get; init; }
    public required BaseModuleValueExpression Right { get; init; }
    public BaseModuleDecimalContext? Decimal { get; init; }
}
public sealed record BaseModuleObjectExpression : BaseModuleValueExpression { public required ImmutableArray<BaseModuleObjectPropertyExpression> Properties { get; init; } }
public sealed record BaseModuleObjectPropertyExpression
{
    public required string StablePropertyId { get; init; }
    public required BaseModuleValueExpression Value { get; init; }
}
public sealed record BaseModuleDecimalContext
{
    public required int Precision { get; init; }
    public required int Scale { get; init; }
    public required BaseModuleDecimalRounding Rounding { get; init; }
}
public sealed record BaseModuleResultProjection { public required BaseModuleObjectExpression Value { get; init; } }

/// <summary>Caller-narrowable module-mutation execution options.</summary>
public sealed record BaseModuleMutationExecutionOptions { public TimeSpan? MaximumWait { get; init; } }
/// <summary>Public typed execution result.</summary>
public sealed record BaseModuleMutationExecutionResult<TResult>
{
    public required BaseMutationRequestDisposition Disposition { get; init; }
    public required BaseModuleMutationOutcome Outcome { get; init; }
    public required TResult Result { get; init; }
}

#pragma warning restore CS1591
