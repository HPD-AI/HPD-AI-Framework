using System.Collections.Immutable;

#pragma warning disable CS1591

namespace HPD.Base;

/// <summary>Provides one isolated provider adapter for the text-search certification protocol.</summary>
public interface IBaseTextCertificationFixture
{
    /// <summary>Gets the exact supported protocol.</summary>
    string ProtocolVersion { get; }
    /// <summary>Gets the provider class under test.</summary>
    BaseTextProviderClass ProviderClass { get; }
    /// <summary>Gets the stable provider identity.</summary>
    string ProviderId { get; }
    /// <summary>Gets the positive provider version.</summary>
    int ProviderVersion { get; }
    /// <summary>Creates one isolated host owned by the certification runner.</summary>
    ValueTask<IBaseTextCertificationHost> CreateAsync(BaseTextCertificationHostRequest request, CancellationToken cancellationToken);
}

/// <summary>Owns one isolated text-search certification case.</summary>
public interface IBaseTextCertificationHost : IAsyncDisposable
{
    /// <summary>Gets authoritative-record controls.</summary>
    IBaseTextCertificationAuthorityControl Authority { get; }
    /// <summary>Gets provider controls.</summary>
    IBaseTextCertificationProviderControl Provider { get; }
    /// <summary>Executes one closed operation through the real public boundary.</summary>
    ValueTask<BaseTextCertificationOperationResult> ExecuteAsync(BaseTextCertificationOperation request, CancellationToken cancellationToken);
    /// <summary>Reads bounded immutable observations.</summary>
    ValueTask<BaseTextCertificationObservationPage> ObserveAsync(BaseTextCertificationObservationRequest request, CancellationToken cancellationToken);
    /// <summary>Attempts bounded shutdown without abandoning retained work.</summary>
    ValueTask<BaseTextCertificationShutdownResult> ShutdownAsync(BaseTextCertificationShutdownRequest request, CancellationToken cancellationToken);
}

/// <summary>Controls authoritative certification records.</summary>
public interface IBaseTextCertificationAuthorityControl
{
    ValueTask<BaseTextCertificationSeedResult> SeedAsync(BaseTextCertificationSeedRequest request, CancellationToken cancellationToken);
    ValueTask<BaseTextCertificationCommitResult> CommitAsync(BaseTextCertificationCommitRequest request, CancellationToken cancellationToken);
    ValueTask<BaseMutationJournalPosition> CaptureHeadAsync(CancellationToken cancellationToken);
    ValueTask<BaseTextCertificationRevisionResult> InspectRevisionAsync(BaseTextCertificationRevisionRequest request, CancellationToken cancellationToken);
    ValueTask PruneHistoryAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken);
    ValueTask RestoreAsync(BaseTextCertificationRestoreRequest request, CancellationToken cancellationToken);
}

/// <summary>Controls provider-specific certification state.</summary>
public interface IBaseTextCertificationProviderControl
{
    ValueTask AdvanceAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken);
    ValueTask PublishVisibilityAsync(BaseMutationJournalPosition through, CancellationToken cancellationToken);
    ValueTask RebuildAsync(BaseTextCertificationRebuildRequest request, CancellationToken cancellationToken);
    ValueTask<BaseTextCertificationProviderState> InspectAsync(CancellationToken cancellationToken);
    ValueTask<BaseTextCertificationFaultState> InspectFaultAsync(CancellationToken cancellationToken);
    ValueTask<BaseTextCertificationLateWorkResult> ReleaseLateWorkAsync(BaseTextCertificationOperationKind operationKind, int occurrence, CancellationToken cancellationToken);
}

public enum BaseTextCertificationPlan { Local = 0, Live = 1, Upgrade = 2 }
public enum BaseTextCertificationOperationKind { HostCreated = 0, Query = 1, ProjectionWrite = 2, Inspection = 3, Rebuild = 4, LateWorkRelease = 5, Shutdown = 6 }
public enum BaseTextCertificationFault { QueryTimeout = 0, QueryNonCooperative = 1, ProjectionWriteTimeout = 2, ProjectionWriteNonCooperative = 3, InspectionTimeout = 4, InspectionNonCooperative = 5, RebuildTimeout = 6, RebuildNonCooperative = 7, MalformedCandidate = 8, DuplicateCandidate = 9, MissingBetterCandidate = 10, FalseScore = 11, FalseFeatureEvidence = 12, FalsePrefixExpansion = 13, FalseBoundary = 14, WrongRevision = 15, WrongSnapshot = 16, IncompletePolicyLowering = 17, JournalGap = 18, RetentionOvertake = 19, StagingCorruption = 20, FinalPublicationFailure = 21 }

public sealed record BaseTextCertificationHostRequest { public required string ProtocolVersion { get; init; } public required BaseTextProviderClass ProviderClass { get; init; } public required BaseTextCertificationPlan Plan { get; init; } public required BaseTextExecutionLimits Limits { get; init; } public required TimeProvider TimeProvider { get; init; } public required ImmutableArray<BaseOpaqueTokenKey> TokenKeys { get; init; } public required ImmutableArray<BaseTextCertificationFaultSchedule> Faults { get; init; } }
public sealed record BaseTextCertificationRecord { public required string Id { get; init; } public required string Tenant { get; init; } public required bool Active { get; init; } public required long Priority { get; init; } public required string? Optional { get; init; } public required string Title { get; init; } public required string Body { get; init; } }
public sealed record BaseTextCertificationSeedRequest { public required ImmutableArray<BaseTextCertificationRecord> Records { get; init; } }
public sealed record BaseTextCertificationSeedResult { public required BaseMutationJournalPosition Head { get; init; } public required int RecordCount { get; init; } public required ImmutableArray<byte> StateChecksum { get; init; } }
public abstract record BaseTextCertificationMutation { private BaseTextCertificationMutation() { } public sealed record Create(BaseTextCertificationRecord Record) : BaseTextCertificationMutation; public sealed record Replace(BaseTextCertificationRecord Record, RevisionToken ExpectedRevision) : BaseTextCertificationMutation; public sealed record Delete(string RecordId, RevisionToken ExpectedRevision) : BaseTextCertificationMutation; }
public sealed record BaseTextCertificationCommitRequest { public required BaseMutationRequestIdentity Identity { get; init; } public required ImmutableArray<BaseTextCertificationMutation> Mutations { get; init; } }
public sealed record BaseTextCertificationCommitResult { public required OperationStatus Status { get; init; } public required BaseMutationJournalPosition Head { get; init; } public required ImmutableArray<RevisionToken> Revisions { get; init; } }
public sealed record BaseTextCertificationRevisionRequest { public required string RecordId { get; init; } public required RevisionToken Revision { get; init; } }
public sealed record BaseTextCertificationRevisionResult { public required bool Found { get; init; } public BaseTextCertificationRecord? Record { get; init; } }
public sealed record BaseTextCertificationRestoreRequest { public required ImmutableArray<byte> Artifact { get; init; } public required long ExpectedRestoreEpoch { get; init; } }
public sealed record BaseTextCertificationRebuildRequest { public required long ExpectedGeneration { get; init; } public required BaseMutationRequestIdentity Identity { get; init; } }
public abstract record BaseTextCertificationOperation { private BaseTextCertificationOperation() { } public sealed record Query(BaseTextHttpQueryRequest Request) : BaseTextCertificationOperation; public sealed record Commit(BaseTextCertificationCommitRequest Request) : BaseTextCertificationOperation; public sealed record Inspect : BaseTextCertificationOperation; public sealed record Rebuild(BaseTextCertificationRebuildRequest Request) : BaseTextCertificationOperation; }
public sealed record BaseTextCertificationOperationResult { public required OperationStatus Status { get; init; } public BaseError? Error { get; init; } public BaseTextHttpResult<BaseTextCertificationRecord>? Query { get; init; } public required BaseTextCertificationProviderState Before { get; init; } public required BaseTextCertificationProviderState After { get; init; } public required long ObservationSequence { get; init; } }
public sealed record BaseTextCertificationLateWorkResult { public required BaseTextCertificationOperationKind OperationKind { get; init; } public required int Occurrence { get; init; } public required bool WasRetained { get; init; } public required bool Released { get; init; } public required int QuarantineCountAfterRelease { get; init; } }
public sealed record BaseTextCertificationShutdownRequest { public required TimeSpan MaximumWait { get; init; } }
public sealed record BaseTextCertificationShutdownResult { public required bool Completed { get; init; } public required int RetainedOperationCount { get; init; } public required TimeSpan Elapsed { get; init; } }
public sealed record BaseTextCertificationProviderState { public required long Generation { get; init; } public required BaseMutationJournalPosition AppliedThrough { get; init; } public required BaseMutationJournalPosition VisibleThrough { get; init; } public required BaseTextIndexState State { get; init; } public required long CarrierCount { get; init; } public required int QuarantineCount { get; init; } }
public sealed record BaseTextCertificationObservationRequest { public long? AfterSequence { get; init; } public required int Take { get; init; } }
public sealed record BaseTextCertificationObservationPage { public required ImmutableArray<BaseTextCertificationObservation> Entries { get; init; } public long? NextSequence { get; init; } public required long RetainedLowSequence { get; init; } public required long CapturedHighSequence { get; init; } public required bool Overtaken { get; init; } }
public sealed record BaseTextCertificationObservation { public required long Sequence { get; init; } public required BaseTextCertificationOperationKind Operation { get; init; } public required BaseTextProviderClass ProviderClass { get; init; } public required ImmutableArray<byte> SnapshotDigest { get; init; } public BaseTextCertificationFault? Fault { get; init; } public required OperationStatus Status { get; init; } public required BaseTextCertificationProviderState State { get; init; } public required BaseTextProviderAccounting Accounting { get; init; } }
public sealed record BaseTextCertificationFaultSchedule { public required BaseTextCertificationFault Fault { get; init; } public required int Occurrence { get; init; } public required TimeSpan Delay { get; init; } public required int PartialSuccessCount { get; init; } }
public sealed record BaseTextCertificationFaultState { public required ImmutableArray<BaseTextCertificationFaultSchedule> Configured { get; init; } public required ImmutableArray<BaseTextCertificationFault> Consumed { get; init; } }
public sealed record BaseTextCertificationCaseResult { public required string Id { get; init; } public required bool Passed { get; init; } public required OperationStatus Status { get; init; } public string? ErrorCode { get; init; } }
public sealed record BaseTextCertificationReport { public required string ProtocolVersion { get; init; } public required string ProviderId { get; init; } public required int ProviderVersion { get; init; } public required BaseTextProviderClass ProviderClass { get; init; } public required bool Passed { get; init; } public required ImmutableArray<BaseTextCertificationCaseResult> Cases { get; init; } public required ImmutableArray<byte> ContractChecksum { get; init; } public required ImmutableArray<byte> ReportChecksum { get; init; } }
