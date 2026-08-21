using System.Collections.Immutable;

#pragma warning disable CS1591

namespace HPD.Base;

/// <summary>Classifies the durable authority relationship of a lexical provider.</summary>
public enum BaseTextProviderClass { CoLocatedTransactional = 0 }

/// <summary>Describes exact certified lexical-provider capabilities.</summary>
public sealed record BaseTextProviderCapability
{
    public required BaseTextProviderClass ProviderClass { get; init; }
    public required bool TransactionalMaintenanceSupported { get; init; }
    public required bool ExactRevisionHydrationSupported { get; init; }
    public required bool PolicyBeforeRankingSupported { get; init; }
    public required bool ExactFixedPointScoreSupported { get; init; }
    public required int MaximumIndexesPerCollection { get; init; }
    public required int MaximumFieldsPerIndex { get; init; }
    public required int MaximumFilterFields { get; init; }
    public required int MaximumQueryNodes { get; init; }
    public required int MaximumQueryDepth { get; init; }
    public required int MaximumPhraseTerms { get; init; }
    public required long MaximumQueryBytes { get; init; }
    public required int MaximumFilterNodes { get; init; }
    public required int MaximumFilterDepth { get; init; }
    public required int MaximumFilterLiterals { get; init; }
    public required int MaximumInValues { get; init; }
    public required int MaximumPrefixExpansions { get; init; }
    public required long MaximumPrefixExpansionBytes { get; init; }
    public required int MaximumSecondaryOrderFields { get; init; }
    public required long MaximumOrderingBytes { get; init; }
    public required int MaximumCandidates { get; init; }
    public required long MaximumScoreProofBytes { get; init; }
    public required int MaximumTokensPerRecord { get; init; }
    public required long MaximumNormalizedBytesPerField { get; init; }
    public required long MaximumNormalizedBytesPerRecord { get; init; }
    public required long MaximumIndexedRecords { get; init; }
    public required long MaximumPostings { get; init; }
    public required long MaximumStatisticsBytes { get; init; }
    public required int MaximumResults { get; init; }
    public required long MaximumResultBytes { get; init; }
    public required int MaximumCursorBytes { get; init; }
    public required int MaximumStatementParameters { get; init; }
    public required long MaximumRebuildStagingRows { get; init; }
    public required long MaximumRebuildBytes { get; init; }
    public required long MaximumTransientBytes { get; init; }
    public required TimeSpan MaximumWriteTime { get; init; }
    public required TimeSpan MaximumQueryTime { get; init; }
    public required TimeSpan MaximumConsistencyWait { get; init; }
    public required TimeSpan MaximumInspectionTime { get; init; }
    public required TimeSpan MaximumRebuildTime { get; init; }
    public required int MaximumQuarantinedOperations { get; init; }
}

/// <summary>Owns one complete lexical provider authority and its administration surface.</summary>
public interface IBaseTextProvider
{
    BaseTextProviderDescriptor Descriptor { get; }
    IBaseTextAuthority Authority { get; }
    ValueTask<OperationResult<BaseTextRebuildResult>> RebuildAsync(BaseTextRebuildRequest request, CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseTextIndexStatus[]>> ListAsync(CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseTextIndexStatus>> GetAsync(string collectionId, string textIndexId, CancellationToken cancellationToken);
}

/// <summary>Identifies one installed certified lexical provider.</summary>
public sealed record BaseTextProviderDescriptor
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required BaseTextProviderClass ProviderClass { get; init; }
    public required BaseTextProviderCapability Capability { get; init; }
    public required ImmutableArray<string> NativeDependencyReceipts { get; init; }
    public required ImmutableArray<byte> CertificationContractChecksum { get; init; }
    public required ImmutableArray<byte> CertificationReportChecksum { get; init; }
    public required ImmutableArray<byte> CertificationReceipt { get; init; }
}

/// <summary>Represents an opaque session-owned provider plan.</summary>
public abstract class BaseTextProviderPlan { protected BaseTextProviderPlan() { } }

/// <summary>Contains one finite lexical authority snapshot.</summary>
public sealed record BaseTextAuthoritySnapshot
{
    public required string StoreIdentityDigest { get; init; }
    public required long RestoreEpoch { get; init; }
    public required long SchemaGeneration { get; init; }
    public required string CollectionId { get; init; }
    public required long PurgeGeneration { get; init; }
    public required string TextIndexId { get; init; }
    public required int TextIndexVersion { get; init; }
    public required long TextIndexGeneration { get; init; }
    public required BaseMutationJournalPosition AuthoritativeHead { get; init; }
    public required BaseMutationJournalPosition AppliedThrough { get; init; }
    public required BaseMutationJournalPosition SearchVisibleThrough { get; init; }
    public required ImmutableArray<byte> AnalyzerReceipt { get; init; }
    public required ImmutableArray<byte> ScoringReceipt { get; init; }
}

/// <summary>Identifies one exact authoritative candidate revision.</summary>
public readonly record struct BaseTextCandidateIdentity(RecordId RecordId, RevisionToken IndexedRevision, BaseMutationJournalPosition IndexedPosition);

public enum BaseTextFeatureKind { Term = 0, Prefix = 1, Phrase = 2 }

public sealed record BaseTextFieldStatistics
{
    public required string StableFieldId { get; init; }
    public required long CandidateTokenCount { get; init; }
}

public sealed record BaseTextFeatureEvidence
{
    public required BaseTextFeatureKind Kind { get; init; }
    public required string StableFieldId { get; init; }
    public required ImmutableArray<ImmutableArray<byte>> NormalizedTokens { get; init; }
    public required int CandidateTermFrequency { get; init; }
    public required ImmutableArray<ImmutableArray<byte>> PrefixExpansions { get; init; }
}

public sealed record BaseTextCandidateScoreProof
{
    public required ImmutableArray<BaseTextFieldStatistics> Fields { get; init; }
    public required ImmutableArray<BaseTextFeatureEvidence> Features { get; init; }
    public required ImmutableArray<byte> ProofDigest { get; init; }
}

public sealed record BaseTextCandidate
{
    public required RecordId RecordId { get; init; }
    public required RevisionToken Revision { get; init; }
    public required BaseMutationJournalPosition IndexedPosition { get; init; }
    public required BaseTextScore Score { get; init; }
    public required ImmutableArray<BaseTextOrderingValue> SecondaryOrdering { get; init; }
    public required ImmutableArray<byte> CanonicalOrderingBoundary { get; init; }
    public required BaseTextCandidateScoreProof ScoreProof { get; init; }
}

public sealed record BaseTextOrderingValue
{
    public required string StableFieldId { get; init; }
    public required bool Missing { get; init; }
    public required bool Null { get; init; }
    public required ImmutableArray<byte> CanonicalJsonUtf8 { get; init; }
}

/// <summary>Represents a closed pre-ranking candidate constraint.</summary>
public abstract record BaseTextCandidateConstraint
{
    private BaseTextCandidateConstraint() { }
    public sealed record True : BaseTextCandidateConstraint;
    public sealed record False : BaseTextCandidateConstraint;
    public sealed record And(ImmutableArray<BaseTextCandidateConstraint> Children) : BaseTextCandidateConstraint;
    public sealed record Or(ImmutableArray<BaseTextCandidateConstraint> Children) : BaseTextCandidateConstraint;
    public sealed record IsMissing(BaseTextFilterField Field) : BaseTextCandidateConstraint;
    public sealed record IsNull(BaseTextFilterField Field) : BaseTextCandidateConstraint;
    public sealed record Equal(BaseTextFilterField Field, BaseTextFilterValue Value) : BaseTextCandidateConstraint;
    public sealed record In(BaseTextFilterField Field, ImmutableArray<BaseTextFilterValue> Values) : BaseTextCandidateConstraint;
}

public readonly record struct BaseTextFilterField(string StableFieldId, BaseTextFilterValueKind ValueKind);

public sealed record BaseTextFilterValue
{
    public required BaseTextFilterValueKind Kind { get; init; }
    public string? StringValue { get; init; }
    public bool? BooleanValue { get; init; }
    public long? IntegerValue { get; init; }
    public static BaseTextFilterValue FromString(string value) => new() { Kind = BaseTextFilterValueKind.String, StringValue = value ?? throw new ArgumentNullException(nameof(value)) };
    public static BaseTextFilterValue FromId(string value) => new() { Kind = BaseTextFilterValueKind.Id, StringValue = value ?? throw new ArgumentNullException(nameof(value)) };
    public static BaseTextFilterValue FromBoolean(bool value) => new() { Kind = BaseTextFilterValueKind.Boolean, BooleanValue = value };
    public static BaseTextFilterValue FromInteger(long value) => new() { Kind = BaseTextFilterValueKind.Integer, IntegerValue = value };
}

public sealed record BaseTextFieldInfluenceConstraint
{
    public required string StableFieldId { get; init; }
    public required BaseTextCandidateConstraint Constraint { get; init; }
    public required ImmutableArray<byte> ConstraintDigest { get; init; }
}

public enum BaseTextConstraintEnforcement { CompleteBeforeMatchingAndRanking = 0 }

public sealed record BaseTextLoweringReceipt
{
    public required string ProviderId { get; init; }
    public required int ProviderVersion { get; init; }
    public required BaseTextProviderClass ProviderClass { get; init; }
    public required ImmutableArray<byte> AuthoritySnapshotDigest { get; init; }
    public required ImmutableArray<byte> IndexChecksum { get; init; }
    public required ImmutableArray<byte> QueryDigest { get; init; }
    public required ImmutableArray<byte> ConstraintDigest { get; init; }
    public required ImmutableArray<byte> InfluenceConstraintsDigest { get; init; }
    public required ImmutableArray<byte> StatementShapeDigest { get; init; }
    public required ImmutableArray<byte> OrderingDigest { get; init; }
    public required ImmutableArray<byte> LimitsDigest { get; init; }
    public required ImmutableArray<byte> CertificationReceiptDigest { get; init; }
}

public sealed record BaseTextProviderPreparationRequest
{
    public required BaseTextAuthoritySnapshot Snapshot { get; init; }
    public required BaseTextIndexDefinition Index { get; init; }
    public required BaseTextQuery NormalizedQuery { get; init; }
    public required ImmutableArray<byte> QueryDigest { get; init; }
    public required BaseTextCandidateConstraint Constraint { get; init; }
    public required ImmutableArray<BaseTextOrder> Order { get; init; }
    public required ImmutableArray<byte> ConstraintDigest { get; init; }
    public required ImmutableArray<BaseTextFieldInfluenceConstraint> InfluenceConstraints { get; init; }
    public required BaseTextExecutionLimits Limits { get; init; }
}

public sealed record BaseTextConstraintPreparation
{
    public required ImmutableArray<byte> QueryDigest { get; init; }
    public required ImmutableArray<byte> ConstraintDigest { get; init; }
    public required BaseTextConstraintEnforcement Enforcement { get; init; }
    public required BaseTextLoweringReceipt Receipt { get; init; }
    public required BaseTextProviderPlan Plan { get; init; }
}

public sealed record BaseTextExecutionRequest
{
    public required BaseTextAuthoritySnapshot Snapshot { get; init; }
    public required BaseTextProviderPlan Plan { get; init; }
    public required int TakePlusOne { get; init; }
    public ImmutableArray<byte>? AfterBoundary { get; init; }
    public required BaseTextExecutionLimits Limits { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }
    public required string CorrelationId { get; init; }
}

public sealed record BaseTextCompletenessEvidence
{
    public required BaseTextProviderClass ProviderClass { get; init; }
    public required ImmutableArray<byte> LoweringReceiptDigest { get; init; }
    public required ImmutableArray<byte> CertificationReceiptDigest { get; init; }
    public required int RequestedTakePlusOne { get; init; }
    public ImmutableArray<byte>? RequestedAfterBoundary { get; init; }
    public required int ReturnedCandidateCount { get; init; }
    public required bool HasMore { get; init; }
    public ImmutableArray<byte>? FirstBoundary { get; init; }
    public ImmutableArray<byte>? LastBoundary { get; init; }
    public required BaseMutationJournalPosition VisibleThrough { get; init; }
    public required ImmutableArray<byte> ProviderExecutionDigest { get; init; }
}

public sealed record BaseTextProviderAccounting
{
    public required long InputBytes { get; init; }
    public required long QueryBytes { get; init; }
    public required long ConstraintBytes { get; init; }
    public required long StatementParameters { get; init; }
    public required long AuthorizedRecordsExamined { get; init; }
    public required long PostingsExamined { get; init; }
    public required long PrefixExpansionCount { get; init; }
    public required long PrefixExpansionBytes { get; init; }
    public required long ScoreProofBytes { get; init; }
    public required long CandidateCount { get; init; }
    public required long OrderingBytes { get; init; }
    public required long RetainedTransientBytes { get; init; }
    public required TimeSpan Elapsed { get; init; }
}

public sealed record BaseTextProviderResult
{
    public required BaseTextAuthoritySnapshot Snapshot { get; init; }
    public required ImmutableArray<BaseTextCandidate> Candidates { get; init; }
    public required BaseTextCompletenessEvidence Completeness { get; init; }
    public required BaseTextProviderAccounting Accounting { get; init; }
}

public abstract record BaseTextConsistencyRequirement
{
    private BaseTextConsistencyRequirement() { }
    public sealed record Current : BaseTextConsistencyRequirement;
    public sealed record AtLeast(BaseTextConsistencyToken Token) : BaseTextConsistencyRequirement;
    public sealed record BoundedStaleness(TimeSpan MaximumAge) : BaseTextConsistencyRequirement;
    public sealed record Available : BaseTextConsistencyRequirement;
}

public sealed record BaseTextAuthorityOpenRequest
{
    public required string CollectionId { get; init; }
    public required string TextIndexId { get; init; }
    public required int TextIndexVersion { get; init; }
    public required BaseTextConsistencyRequirement Consistency { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }
    public required string CorrelationId { get; init; }
}

public interface IBaseTextHydrationSession : IAsyncDisposable
{
    BaseTextAuthoritySnapshot Snapshot { get; }
    ValueTask<OperationResult<BaseTextConstraintPreparation>> PrepareAsync(BaseTextProviderPreparationRequest request, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<BaseTextProviderResult>> SearchAsync(BaseTextExecutionRequest request, CancellationToken cancellationToken = default);
    ValueTask<OperationResult<RecordEnvelope[]>> GetExactAsync(CollectionDefinition collection, BaseTextCandidateIdentity[] candidates, OperationContext context, CancellationToken cancellationToken = default);
}

public interface IBaseTextAuthority
{
    BaseTextProviderDescriptor Descriptor { get; }
    ValueTask<OperationResult<IBaseTextHydrationSession>> OpenAsync(BaseTextAuthorityOpenRequest request, CancellationToken cancellationToken = default);
}
