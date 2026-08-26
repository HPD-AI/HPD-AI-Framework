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
/// <summary>Defines the closed ordering relations supported by a module field guard.</summary>
public enum BaseModuleOrderedComparisonKind
{
    /// <summary>Requires the captured value to be less than the expected value.</summary>
    LessThan = 0,
    /// <summary>Requires the captured value to be less than or equal to the expected value.</summary>
    LessThanOrEqual = 1,
    /// <summary>Requires the captured value to be greater than the expected value.</summary>
    GreaterThan = 2,
    /// <summary>Requires the captured value to be greater than or equal to the expected value.</summary>
    GreaterThanOrEqual = 3,
}
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
    /// <summary>Gets the exact separately registered grant for every declared system source.</summary>
    public required ImmutableArray<BaseModuleSystemSourceGrant> SystemSourceGrants { get; init; }
    public required ImmutableArray<string> GenerationCellIds { get; init; }
    public required ImmutableArray<string> ImportedSubjectContractIds { get; init; }
    public required BaseModuleMutationTemplate Template { get; init; }
    public required BaseModuleMutationLimits Limits { get; init; }
    public required BaseModuleMutationReceiptPolicy ReceiptPolicy { get; init; }
    public required BaseModuleMutationChecksum Checksum { get; init; }
}

/// <summary>Binds one declared system collection to its exact L38 source grant.</summary>
public sealed record BaseModuleSystemSourceGrant
{
    /// <summary>Gets the exact declared system collection.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the exact separately registered source grant.</summary>
    public required string GrantId { get; init; }
}

/// <summary>Closed immutable operation template.</summary>
public sealed record BaseModuleMutationTemplate
{
    public required ImmutableArray<BaseModuleCapture> Captures { get; init; }
    public required ImmutableArray<BaseModuleGuard> Guards { get; init; }
    public required BaseModuleMutationBlock Body { get; init; }
    public required BaseModuleResultProjection Result { get; init; }
}

public abstract record BaseModuleCapture { internal BaseModuleCapture() { } internal string Id { get; init; } = ""; }
public sealed record BaseModuleRecordCapture : BaseModuleCapture
{
    internal BaseModuleRecordCapture() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleCapturePresence Presence { get; init; }
}
public sealed record BaseModuleGenerationCapture : BaseModuleCapture
{
    internal BaseModuleGenerationCapture() { }
    internal string CellId { get; init; } = "";
    internal BaseModuleValueExpression? Key { get; init; }
    internal BaseModuleGenerationAbsenceBehavior Absence { get; init; }
}

public abstract record BaseModuleGuard { internal BaseModuleGuard() { } internal string Id { get; init; } = ""; }
public sealed record BaseModuleRecordPresenceGuard : BaseModuleGuard
{
    internal BaseModuleRecordPresenceGuard() { }
    internal string CaptureId { get; init; } = "";
    internal bool MustBePresent { get; init; }
}
public sealed record BaseModuleRevisionEqualsGuard : BaseModuleGuard
{
    internal BaseModuleRevisionEqualsGuard() { }
    internal string CaptureId { get; init; } = "";
    internal BaseModuleValueExpression Expected { get; init; } = null!;
}
public sealed record BaseModuleFieldEqualsGuard : BaseModuleGuard
{
    internal BaseModuleFieldEqualsGuard() { }
    internal BaseModuleCapturedFieldReference Field { get; init; } = null!;
    internal BaseModuleValueExpression Expected { get; init; } = null!;
}
/// <summary>Compares one captured non-null ordered scalar with an exact-type expression.</summary>
public sealed record BaseModuleFieldComparisonGuard : BaseModuleGuard
{
    internal BaseModuleFieldComparisonGuard() { }
    /// <summary>Gets the captured field.</summary>
    internal BaseModuleCapturedFieldReference Field { get; init; } = null!;
    /// <summary>Gets the required ordering relation.</summary>
    internal BaseModuleOrderedComparisonKind Comparison { get; init; }
    /// <summary>Gets the exact-type expected value.</summary>
    internal BaseModuleValueExpression Expected { get; init; } = null!;
}
public sealed record BaseModuleFieldPresenceGuard : BaseModuleGuard
{
    internal BaseModuleFieldPresenceGuard() { }
    internal BaseModuleCapturedFieldReference Field { get; init; } = null!;
    internal BaseModuleFieldPresenceTest Test { get; init; }
}
public sealed record BaseModuleGenerationGuard : BaseModuleGuard
{
    internal BaseModuleGenerationGuard() { }
    internal string CaptureId { get; init; } = "";
    internal BaseModuleGenerationComparisonKind Comparison { get; init; }
    internal BaseModuleValueExpression? Expected { get; init; }
}
/// <summary>Tests the captured semantic slot state for an installed ensure or retirement operation.</summary>
public sealed record BaseModuleSemanticActivationStateGuard : BaseModuleGuard
{
    internal BaseModuleSemanticActivationStateGuard() { }
    /// <summary>Gets the required captured semantic state.</summary>
    internal BaseModuleSemanticActivationStateTest Test { get; init; }
}
/// <summary>Classifies closed semantic-state guard tests.</summary>
public enum BaseModuleSemanticActivationStateTest
{
    /// <summary>The slot is missing.</summary>
    Missing = 1,
    /// <summary>The slot is live.</summary>
    Live = 2,
    /// <summary>The slot is retired.</summary>
    Retired = 3,
    /// <summary>The slot contains compacted permanent absence.</summary>
    CompactedAbsent = 4,
}
public sealed record BaseModuleLogicalGuard : BaseModuleGuard
{
    internal BaseModuleLogicalGuard() { }
    internal BaseModuleLogicalGuardKind Kind { get; init; }
    internal ImmutableArray<string> ChildGuardIds { get; init; }
}

public sealed record BaseModuleMutationBlock { public required ImmutableArray<BaseModuleStatement> Statements { get; init; } }
public abstract record BaseModuleStatement { internal BaseModuleStatement() { } internal string Id { get; init; } = ""; }
public sealed record BaseModuleCreateStatement : BaseModuleStatement
{
    internal BaseModuleCreateStatement() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleObjectExpression Payload { get; init; } = null!;
}
public sealed record BaseModulePatchStatement : BaseModuleStatement
{
    internal BaseModulePatchStatement() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleObjectExpression Patch { get; init; } = null!;
    internal BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleReplaceStatement : BaseModuleStatement
{
    internal BaseModuleReplaceStatement() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleObjectExpression Payload { get; init; } = null!;
    internal BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleDeleteStatement : BaseModuleStatement
{
    internal BaseModuleDeleteStatement() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleUpsertStatement : BaseModuleStatement
{
    internal BaseModuleUpsertStatement() { }
    internal string CollectionId { get; init; } = "";
    internal BaseModuleValueExpression RecordId { get; init; } = null!;
    internal BaseModuleObjectExpression Create { get; init; } = null!;
    internal BaseModuleObjectExpression Update { get; init; } = null!;
    internal RecordUpsertUpdateMode UpdateMode { get; init; }
    internal BaseModuleValueExpression? ExpectedRevision { get; init; }
}
public sealed record BaseModuleIncrementGenerationStatement : BaseModuleStatement
{
    internal BaseModuleIncrementGenerationStatement() { }
    internal string CaptureId { get; init; } = "";
    internal bool CreateIfAbsent { get; init; }
}
public sealed record BaseModuleIfStatement : BaseModuleStatement
{
    internal BaseModuleIfStatement() { }
    internal string GuardId { get; init; } = "";
    internal BaseModuleMutationBlock WhenTrue { get; init; } = null!;
    internal BaseModuleMutationBlock WhenFalse { get; init; } = null!;
}
public sealed record BaseModuleRequireStatement : BaseModuleStatement
{
    internal BaseModuleRequireStatement() { }
    internal string GuardId { get; init; } = "";
    internal string RequirementId { get; init; } = "";
}

public sealed record BaseModuleRequestPropertyReference
{
    internal BaseModuleRequestPropertyReference() { }
    public required ImmutableArray<string> StablePropertyPath { get; init; }
    public required BaseModuleDtoScalarAuthority Authority { get; init; }
}
public sealed record BaseModuleCapturedFieldReference
{
    internal BaseModuleCapturedFieldReference() { }
    public required string CaptureId { get; init; }
    public required string StableFieldId { get; init; }
    public required BaseModuleValueType Authority { get; init; }
}

public abstract record BaseModuleValueExpression
{
    internal BaseModuleValueExpression() { }
    public required string Id { get; init; }
    public BaseModuleValueType? ResultType { get; init; }
}
public sealed record BaseModuleRequestPropertyExpression : BaseModuleValueExpression { internal BaseModuleRequestPropertyExpression() { } public required BaseModuleRequestPropertyReference Property { get; init; } }
public sealed record BaseModuleConstantExpression : BaseModuleValueExpression { internal BaseModuleConstantExpression() { } public required ImmutableArray<byte> CanonicalBaseJson { get; init; } }
public sealed record BaseModuleCapturedRecordIdExpression : BaseModuleValueExpression { internal BaseModuleCapturedRecordIdExpression() { } public required string CaptureId { get; init; } }
public sealed record BaseModuleCapturedRevisionExpression : BaseModuleValueExpression { internal BaseModuleCapturedRevisionExpression() { } public required string CaptureId { get; init; } }
public sealed record BaseModuleCapturedFieldExpression : BaseModuleValueExpression { internal BaseModuleCapturedFieldExpression() { } public required BaseModuleCapturedFieldReference Field { get; init; } }
public sealed record BaseModuleCapturedGenerationExpression : BaseModuleValueExpression { internal BaseModuleCapturedGenerationExpression() { } public required string CaptureId { get; init; } }
public sealed record BaseModuleCommittedRecordIdExpression : BaseModuleValueExpression { internal BaseModuleCommittedRecordIdExpression() { } public required string StatementId { get; init; } }
public sealed record BaseModuleCommittedRevisionExpression : BaseModuleValueExpression { internal BaseModuleCommittedRevisionExpression() { } public required string StatementId { get; init; } }
public sealed record BaseModuleCommittedUpsertDispositionExpression : BaseModuleValueExpression { internal BaseModuleCommittedUpsertDispositionExpression() { } public required string StatementId { get; init; } }
public sealed record BaseModuleResultingGenerationExpression : BaseModuleValueExpression { internal BaseModuleResultingGenerationExpression() { } public required string CaptureId { get; init; } }
/// <summary>Projects the closed ensure disposition.</summary>
public sealed record BaseModuleSemanticActivationDispositionExpression : BaseModuleValueExpression { internal BaseModuleSemanticActivationDispositionExpression() { } }
/// <summary>Projects the live semantic activation ID.</summary>
public sealed record BaseModuleSemanticActivationIdExpression : BaseModuleValueExpression { internal BaseModuleSemanticActivationIdExpression() { } }
/// <summary>Projects whether ensure materialized a new activation.</summary>
public sealed record BaseModuleSemanticActivationWasMaterializedExpression : BaseModuleValueExpression { internal BaseModuleSemanticActivationWasMaterializedExpression() { } }
/// <summary>Projects the closed retirement disposition.</summary>
public sealed record BaseModuleSemanticActivationRetirementDispositionExpression : BaseModuleValueExpression { internal BaseModuleSemanticActivationRetirementDispositionExpression() { } }
public sealed record BaseModuleCoalesceExpression : BaseModuleValueExpression { internal BaseModuleCoalesceExpression() { } public required ImmutableArray<BaseModuleValueExpression> Values { get; init; } }
public sealed record BaseModuleConditionalExpression : BaseModuleValueExpression
{
    internal BaseModuleConditionalExpression() { }
    public required string GuardId { get; init; }
    public required BaseModuleValueExpression WhenTrue { get; init; }
    public required BaseModuleValueExpression WhenFalse { get; init; }
}
public sealed record BaseModuleBinaryNumericExpression : BaseModuleValueExpression
{
    internal BaseModuleBinaryNumericExpression() { }
    public required BaseModuleNumericOperator Operator { get; init; }
    public required BaseModuleValueExpression Left { get; init; }
    public required BaseModuleValueExpression Right { get; init; }
    public BaseModuleDecimalContext? Decimal { get; init; }
}
public sealed record BaseModuleObjectExpression : BaseModuleValueExpression
{
    internal BaseModuleObjectExpression() { }
    public required ImmutableArray<BaseModuleObjectPropertyExpression> Properties { get; init; }
}
public sealed record BaseModuleObjectPropertyExpression
{
    internal BaseModuleObjectPropertyExpression() { }
    public required string StablePropertyId { get; init; }
    public required BaseModuleValueExpression Value { get; init; }
}
public sealed record BaseModuleDecimalContext
{
    public required int Precision { get; init; }
    public required int Scale { get; init; }
    public required BaseModuleDecimalRounding Rounding { get; init; }
}
public sealed record BaseModuleResultProjection
{
    internal BaseModuleResultProjection() { }
    internal BaseModuleObjectExpression Value { get; init; } = null!;
}

/// <summary>Caller-narrowable module-mutation execution options.</summary>
public sealed record BaseModuleMutationExecutionOptions
{
    /// <summary>Gets an optional caller-narrowed commit observation deadline.</summary>
    public TimeSpan? MaximumWait { get; init; }
    internal BaseActivationGuard? ActivationGuard { get; init; }
    internal BaseActivationCreationExtension? ActivationCreation { get; init; }
    internal BaseSemanticActivationGuardedRequest? SemanticActivation { get; init; }
}
/// <summary>Public typed execution result.</summary>
public sealed record BaseModuleMutationExecutionResult<TResult>
{
    public required BaseMutationRequestDisposition Disposition { get; init; }
    public required BaseModuleMutationOutcome Outcome { get; init; }
    public required TResult Result { get; init; }
}

#pragma warning restore CS1591
