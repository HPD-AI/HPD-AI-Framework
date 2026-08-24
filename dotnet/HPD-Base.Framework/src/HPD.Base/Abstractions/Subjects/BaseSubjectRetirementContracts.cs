using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Classifies one consumer's participation in exported-subject retirement.</summary>
public enum BaseSubjectRetirementParticipation
{
    /// <summary>The consumer observes lifecycle facts and has no acknowledgement authority.</summary>
    ObserveOnly = 0,
    /// <summary>The consumer may attest handling without delaying purge.</summary>
    AdvisoryAcknowledgement = 1,
    /// <summary>The mutually accepted consumer must attest before physical purge.</summary>
    RequiredBeforePurge = 2,
}

/// <summary>Classifies an accepted consumer attestation.</summary>
public enum BaseSubjectAcknowledgementDisposition
{
    /// <summary>The registered consumer handling completed.</summary>
    Completed = 0,
    /// <summary>The consumer intentionally retains its data but releases purge coordination.</summary>
    RetainedByPolicy = 1,
}

/// <summary>Classifies current retirement-barrier authority.</summary>
public enum BaseSubjectRetirementBarrierState
{
    /// <summary>Required consumer attestations remain outstanding.</summary>
    Pending = 0,
    /// <summary>Every required consumer attested before the deadline.</summary>
    Satisfied = 1,
    /// <summary>The coordination deadline elapsed and requires operator action.</summary>
    TimedOut = 2,
    /// <summary>The coordination deadline elapsed under fail-closed quarantine policy.</summary>
    Quarantined = 3,
    /// <summary>An authorized operator released the barrier.</summary>
    Overridden = 4,
}

/// <summary>Classifies the configured deadline outcome.</summary>
public enum BaseSubjectRetirementTimeoutBehavior
{
    /// <summary>Quarantines current retirement authority until an override.</summary>
    Quarantine = 0,
    /// <summary>Records a timed-out barrier requiring an operator decision.</summary>
    RequireOperatorDecision = 1,
}

/// <summary>Defines acknowledgement execution bounds for one consumer.</summary>
public sealed record BaseSubjectRetirementConsumerLimits
{
    /// <summary>Gets the maximum acknowledgements in one atomic commit.</summary>
    public required int MaximumAcknowledgementsPerCommit { get; init; }
    /// <summary>Gets the maximum canonical request bytes.</summary>
    public required int MaximumAcknowledgementRequestBytes { get; init; }
    /// <summary>Gets the maximum canonical receipt bytes.</summary>
    public required int MaximumReceiptBytes { get; init; }
    /// <summary>Gets the acknowledgement transaction timeout.</summary>
    public required TimeSpan AcknowledgementTimeout { get; init; }
    /// <summary>Gets the receipt-resolution timeout.</summary>
    public required TimeSpan ReceiptResolutionTimeout { get; init; }
}

/// <summary>Declares one consumer-owned retirement profile.</summary>
public sealed record BaseSubjectRetirementConsumerDefinition
{
    /// <summary>Gets the installed L47 consumer ID.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the installed L47 consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the owning module ID.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the closed worker audience.</summary>
    public required BaseSubjectLifecycleConsumerAudience Audience { get; init; }
    /// <summary>Gets the installed lifecycle-consumer checksum.</summary>
    public required string LifecycleConsumerChecksum { get; init; }
    /// <summary>Gets the stable retirement-profile ID.</summary>
    public required string RetirementProfileId { get; init; }
    /// <summary>Gets the retirement-profile version.</summary>
    public required int RetirementProfileVersion { get; init; }
    /// <summary>Gets the retirement-profile checksum.</summary>
    public required string RetirementProfileChecksum { get; init; }
    /// <summary>Gets the participation kind.</summary>
    public required BaseSubjectRetirementParticipation Participation { get; init; }
    /// <summary>Gets the exact acknowledgement grant ID.</summary>
    public required string AcknowledgementGrantId { get; init; }
    /// <summary>Gets immutable acknowledgement limits.</summary>
    public required BaseSubjectRetirementConsumerLimits Limits { get; init; }
}

/// <summary>Contains one exporter-accepted required consumer.</summary>
public sealed record BaseAcceptedRetirementConsumer
{
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public required string ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public required int ConsumerVersion { get; init; }
/// <summary>Defines OwningModuleId for coordinated subject retirement.</summary>
    public required string OwningModuleId { get; init; }
/// <summary>Defines Audience for coordinated subject retirement.</summary>
    public required BaseSubjectLifecycleConsumerAudience Audience { get; init; }
/// <summary>Defines LifecycleConsumerChecksum for coordinated subject retirement.</summary>
    public required string LifecycleConsumerChecksum { get; init; }
/// <summary>Defines RetirementProfileId for coordinated subject retirement.</summary>
    public required string RetirementProfileId { get; init; }
/// <summary>Defines RetirementProfileVersion for coordinated subject retirement.</summary>
    public required int RetirementProfileVersion { get; init; }
/// <summary>Defines RetirementProfileChecksum for coordinated subject retirement.</summary>
    public required string RetirementProfileChecksum { get; init; }
/// <summary>Defines Participation for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementParticipation Participation { get; init; }
/// <summary>Defines AcknowledgementGrantId for coordinated subject retirement.</summary>
    public required string AcknowledgementGrantId { get; init; }
/// <summary>Defines Limits for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementConsumerLimits Limits { get; init; }
/// <summary>Defines RetirementConsumerChecksum for coordinated subject retirement.</summary>
    public required string RetirementConsumerChecksum { get; init; }
}

/// <summary>Defines BASE-enforced minimum tombstone retention.</summary>
public sealed record BaseSubjectPurgeRetentionPolicy
{
    /// <summary>Gets the minimum age before final purge is eligible.</summary>
    public required TimeSpan MinimumTombstoneAge { get; init; }
}

/// <summary>Declares the exporter-owned required-consumer policy.</summary>
public sealed record BaseSubjectRetirementPolicy
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines AcceptedConsumers for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseAcceptedRetirementConsumer> AcceptedConsumers { get; init; }
/// <summary>Defines CoordinationWindow for coordinated subject retirement.</summary>
    public required TimeSpan CoordinationWindow { get; init; }
/// <summary>Defines TimeoutBehavior for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementTimeoutBehavior TimeoutBehavior { get; init; }
/// <summary>Defines PurgeRetention for coordinated subject retirement.</summary>
    public required BaseSubjectPurgeRetentionPolicy PurgeRetention { get; init; }
/// <summary>Defines PolicyChecksum for coordinated subject retirement.</summary>
    public required string PolicyChecksum { get; init; }
}

/// <summary>Contains one current coordinated-retirement barrier.</summary>
public sealed record BaseSubjectRetirementBarrier
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines RequiredConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string RequiredConsumerSetChecksum { get; init; }
/// <summary>Defines CreatedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
/// <summary>Defines DeadlineUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
/// <summary>Defines State for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrierState State { get; init; }
/// <summary>Defines Generation for coordinated subject retirement.</summary>
    public required long Generation { get; init; }
/// <summary>Defines BarrierChecksum for coordinated subject retirement.</summary>
    public required string BarrierChecksum { get; init; }
}

/// <summary>Contains protected provider scope and one current barrier.</summary>
public sealed record BaseSubjectRetirementBarrierRow
{
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
/// <summary>Defines Barrier for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrier Barrier { get; init; }
    /// <summary>Gets canonical sorted acknowledgement checksum inputs for hostile-result validation.</summary>
    public required ImmutableArray<string> AcknowledgementChecksumInputs { get; init; }
}

/// <summary>Requests capture authority for every retirement projection affected by one mutation.</summary>
public sealed record BaseSubjectRetirementCaptureExtension
{
    /// <summary>Gets dense source-ordered projections.</summary>
    public required ImmutableArray<BaseSubjectRetirementProjectionCaptureRequest> Projections { get; init; }
}

/// <summary>Identifies one contract-bound retirement projection during provider capture.</summary>
public sealed record BaseSubjectRetirementProjectionCaptureRequest
{
/// <summary>Defines SourceMutationOrdinal for coordinated subject retirement.</summary>
    public required int SourceMutationOrdinal { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines ContractChecksum for coordinated subject retirement.</summary>
    public required string ContractChecksum { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
/// <summary>Defines AcceptedConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string AcceptedConsumerSetChecksum { get; init; }
}

/// <summary>Contains Runtime-finalized barrier projections.</summary>
public sealed record BaseSubjectRetirementProjectionPlan
{
/// <summary>Defines Items for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectRetirementProjectionPlanItem> Items { get; init; }
/// <summary>Defines PlanChecksum for coordinated subject retirement.</summary>
    public required string PlanChecksum { get; init; }
}

/// <summary>Contains one exact tombstone-to-barrier projection.</summary>
public sealed record BaseSubjectRetirementProjectionPlanItem
{
/// <summary>Defines ProjectionOrdinal for coordinated subject retirement.</summary>
    public required int ProjectionOrdinal { get; init; }
/// <summary>Defines SourceMutationOrdinal for coordinated subject retirement.</summary>
    public required int SourceMutationOrdinal { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines ContractChecksum for coordinated subject retirement.</summary>
    public required string ContractChecksum { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines TombstonedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset TombstonedAtUtc { get; init; }
/// <summary>Defines DeadlineUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
/// <summary>Defines RequiredConsumers for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseAcceptedRetirementConsumer> RequiredConsumers { get; init; }
/// <summary>Defines AcceptedConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string AcceptedConsumerSetChecksum { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
}

/// <summary>Contains provider-prepared evidence exactly corresponding to a retirement projection plan.</summary>
public sealed record BaseSubjectRetirementPreparedEvidence
{
/// <summary>Defines Items for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectRetirementPreparedEvidenceItem> Items { get; init; }
/// <summary>Defines PlanChecksum for coordinated subject retirement.</summary>
    public required string PlanChecksum { get; init; }
}

/// <summary>Contains one prepared absent-to-current barrier transition.</summary>
public sealed record BaseSubjectRetirementPreparedEvidenceItem
{
/// <summary>Defines ProjectionOrdinal for coordinated subject retirement.</summary>
    public required int ProjectionOrdinal { get; init; }
/// <summary>Defines Previous for coordinated subject retirement.</summary>
    public BaseSubjectRetirementBarrier? Previous { get; init; }
/// <summary>Defines Resulting for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrier Resulting { get; init; }
/// <summary>Defines ProtectedScope for coordinated subject retirement.</summary>
    public required BaseProtectedSubjectScope ProtectedScope { get; init; }
/// <summary>Defines PublicationPosition for coordinated subject retirement.</summary>
    public required long PublicationPosition { get; init; }
}

/// <summary>Contains applied retirement evidence before Runtime-owned commit finalization.</summary>
public sealed record BaseSubjectRetirementProvisionalEvidence
{
/// <summary>Defines Items for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectRetirementPreparedEvidenceItem> Items { get; init; }
/// <summary>Defines PlanChecksum for coordinated subject retirement.</summary>
    public required string PlanChecksum { get; init; }
}

/// <summary>Contains the expected current required barrier carried by an acknowledgement.</summary>
public sealed record BaseSubjectRequiredBarrierExpectation
{
/// <summary>Defines Checksum for coordinated subject retirement.</summary>
    public required string Checksum { get; init; }
/// <summary>Defines Generation for coordinated subject retirement.</summary>
    public required long Generation { get; init; }
}

/// <summary>Contains one identified lifecycle acknowledgement.</summary>
public sealed record BaseSubjectLifecycleAcknowledgement
{
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public required string ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public required int ConsumerVersion { get; init; }
/// <summary>Defines ConsumerChecksum for coordinated subject retirement.</summary>
    public required string ConsumerChecksum { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ThroughSubjectSequence for coordinated subject retirement.</summary>
    public required long ThroughSubjectSequence { get; init; }
/// <summary>Defines Disposition for coordinated subject retirement.</summary>
    public required BaseSubjectAcknowledgementDisposition Disposition { get; init; }
/// <summary>Defines Participation for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementParticipation Participation { get; init; }
/// <summary>Defines RequiredBarrier for coordinated subject retirement.</summary>
    public BaseSubjectRequiredBarrierExpectation? RequiredBarrier { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Contains one advisory lifecycle delivery and its purpose-bound acknowledgement authority.</summary>
public sealed record BaseSubjectAdvisoryLifecycleDelivery<TSubject>
{
    /// <summary>Gets the underlying durable lifecycle delivery.</summary>
    public required BaseSubjectLifecycleDelivery<TSubject> Lifecycle { get; init; }
    /// <summary>Gets opaque advisory acknowledgement evidence.</summary>
    public required BaseSubjectAdvisoryAcknowledgementEvidence<TSubject> Acknowledgement { get; init; }
    /// <summary>Gets the deterministic identified-mutation identity for this evidence.</summary>
    public required BaseMutationRequestIdentity AcknowledgementIdentity { get; init; }
}

/// <summary>Contains one required lifecycle delivery and its barrier-bound acknowledgement authority.</summary>
public sealed record BaseSubjectRequiredLifecycleDelivery<TSubject>
{
    /// <summary>Gets the underlying durable lifecycle delivery.</summary>
    public required BaseSubjectLifecycleDelivery<TSubject> Lifecycle { get; init; }
    /// <summary>Gets opaque required acknowledgement evidence.</summary>
    public required BaseSubjectRequiredAcknowledgementEvidence<TSubject> Acknowledgement { get; init; }
    /// <summary>Gets the deterministic identified-mutation identity for this evidence.</summary>
    public required BaseMutationRequestIdentity AcknowledgementIdentity { get; init; }
}

/// <summary>Represents immutable purpose-bound advisory acknowledgement evidence.</summary>
public sealed class BaseSubjectAdvisoryAcknowledgementEvidence<TSubject>
{
    private readonly byte[] _encodedToken;
    internal BaseSubjectAdvisoryAcknowledgementEvidence(ReadOnlySpan<byte> value) => _encodedToken = value.ToArray();
    internal ReadOnlyMemory<byte> EncodedToken => _encodedToken;
    /// <summary>Returns the canonical unpadded Base64url token.</summary>
    public override string ToString() => System.Text.Encoding.ASCII.GetString(_encodedToken);
}

/// <summary>Represents immutable purpose-bound required acknowledgement evidence.</summary>
public sealed class BaseSubjectRequiredAcknowledgementEvidence<TSubject>
{
    private readonly byte[] _encodedToken;
    internal BaseSubjectRequiredAcknowledgementEvidence(ReadOnlySpan<byte> value) => _encodedToken = value.ToArray();
    internal ReadOnlyMemory<byte> EncodedToken => _encodedToken;
    /// <summary>Returns the canonical unpadded Base64url token.</summary>
    public override string ToString() => System.Text.Encoding.ASCII.GetString(_encodedToken);
}

/// <summary>Classifies a committed retirement operation result.</summary>
public enum BaseSubjectRetirementMutationOutcome
{
    /// <summary>The operation committed new state.</summary>
    Applied = 0,
    /// <summary>The identified operation exactly matched a stored result.</summary>
    Duplicate = 1,
    /// <summary>The requested advance was already superseded.</summary>
    Obsolete = 2,
}

/// <summary>Returns one committed acknowledgement outcome.</summary>
public sealed record BaseSubjectAcknowledgementResult
{
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines BarrierState for coordinated subject retirement.</summary>
    public BaseSubjectRetirementBarrierState? BarrierState { get; init; }
/// <summary>Defines BarrierGeneration for coordinated subject retirement.</summary>
    public long? BarrierGeneration { get; init; }
/// <summary>Defines BarrierChecksum for coordinated subject retirement.</summary>
    public string? BarrierChecksum { get; init; }
/// <summary>Defines ThroughSubjectSequence for coordinated subject retirement.</summary>
    public required long ThroughSubjectSequence { get; init; }
}

/// <summary>Contains one provider-bound acknowledgement request after Runtime authorization.</summary>
public sealed record BaseSubjectRetirementProviderAcknowledgementRequest
{
/// <summary>Defines Acknowledgement for coordinated subject retirement.</summary>
    public required BaseSubjectLifecycleAcknowledgement Acknowledgement { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
/// <summary>Defines RetirementConsumerChecksum for coordinated subject retirement.</summary>
    public required string RetirementConsumerChecksum { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
/// <summary>Defines ObservedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
/// <summary>Defines DeadlineUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
    /// <summary>Gets the optional same-store activation fence.</summary>
    public BaseActivationGuard? ActivationGuard { get; init; }
}

/// <summary>Contains one Runtime-authorized timeout transition for provider application.</summary>
public sealed record BaseSubjectRetirementProviderTimeoutRequest
{
/// <summary>Defines Request for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementTimeoutRequest Request { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
/// <summary>Defines ObservedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

/// <summary>Contains one Runtime-authorized override transition for provider application.</summary>
public sealed record BaseSubjectRetirementProviderOverrideRequest
{
/// <summary>Defines Request for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementOverrideRequest Request { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
/// <summary>Defines ObservedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

/// <summary>Contains one Runtime-authorized physical purge for provider application.</summary>
public sealed record BaseSubjectRetirementProviderPurgeRequest
{
/// <summary>Defines Request for coordinated subject retirement.</summary>
    public required BaseSubjectFinalPurgeRequest Request { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
/// <summary>Defines ContractChecksum for coordinated subject retirement.</summary>
    public required string ContractChecksum { get; init; }
/// <summary>Defines RetirementPolicyChecksum for coordinated subject retirement.</summary>
    public required string RetirementPolicyChecksum { get; init; }
/// <summary>Defines MinimumTombstoneAge for coordinated subject retirement.</summary>
    public required TimeSpan MinimumTombstoneAge { get; init; }
/// <summary>Defines ObservedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset ObservedAtUtc { get; init; }
/// <summary>Defines Operation for coordinated subject retirement.</summary>
    public required OperationContext Operation { get; init; }
}

/// <summary>Contains provider-applied purge evidence before receipt commit.</summary>
public sealed record BaseSubjectRetirementPurgeApplied
{
/// <summary>Defines Result for coordinated subject retirement.</summary>
    public required BaseSubjectFinalPurgeResult Result { get; init; }
/// <summary>Defines Mutation for coordinated subject retirement.</summary>
    public required BaseRecordMutationFact Mutation { get; init; }
/// <summary>Defines Terminal for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementTerminalReceipt Terminal { get; init; }
}

/// <summary>Identifies one protected, canonically ordered barrier boundary.</summary>
public sealed record BaseSubjectRetirementBarrierKey
{
/// <summary>Defines ScopeKind for coordinated subject retirement.</summary>
    public required BaseSubjectScopeKind ScopeKind { get; init; }
/// <summary>Defines ScopeIndexDigest for coordinated subject retirement.</summary>
    public required byte[] ScopeIndexDigest { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
}

/// <summary>Requests one authorized bounded page of current retirement barriers.</summary>
public sealed record BaseSubjectRetirementBarrierReadRequest
{
/// <summary>Defines ApplicationId for coordinated subject retirement.</summary>
    public required string ApplicationId { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines ScopeAuthority for coordinated subject retirement.</summary>
    public required BaseSubjectScopeQueryAuthority ScopeAuthority { get; init; }
/// <summary>Defines State for coordinated subject retirement.</summary>
    public BaseSubjectRetirementBarrierState? State { get; init; }
/// <summary>Defines After for coordinated subject retirement.</summary>
    public BaseSubjectRetirementBarrierKey? After { get; init; }
/// <summary>Defines Take for coordinated subject retirement.</summary>
    public required int Take { get; init; }
/// <summary>Defines MaximumResultBytes for coordinated subject retirement.</summary>
    public required long MaximumResultBytes { get; init; }
/// <summary>Defines DeadlineUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Reports exact bounded provider work for a retirement read.</summary>
public sealed record BaseSubjectRetirementReadAccounting
{
/// <summary>Defines BarrierRows for coordinated subject retirement.</summary>
    public required int BarrierRows { get; init; }
/// <summary>Defines AcknowledgementRows for coordinated subject retirement.</summary>
    public required int AcknowledgementRows { get; init; }
/// <summary>Defines ResultBytes for coordinated subject retirement.</summary>
    public required long ResultBytes { get; init; }
/// <summary>Defines EvidenceBytes for coordinated subject retirement.</summary>
    public required long EvidenceBytes { get; init; }
/// <summary>Defines TransientBytes for coordinated subject retirement.</summary>
    public required long TransientBytes { get; init; }
}

/// <summary>Contains one authorized page of current retirement barriers.</summary>
public sealed record BaseSubjectRetirementBarrierPage
{
/// <summary>Defines Barriers for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectRetirementBarrierRow> Barriers { get; init; }
/// <summary>Defines Next for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrierKey? Next { get; init; }
/// <summary>Defines CapturedBarrierGeneration for coordinated subject retirement.</summary>
    public required long CapturedBarrierGeneration { get; init; }
/// <summary>Defines Intervals for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseReadIntervalEvidence> Intervals { get; init; }
/// <summary>Defines Accounting for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementReadAccounting Accounting { get; init; }
}

/// <summary>Contains one opaque authenticated continuation for retirement-barrier paging.</summary>
public sealed class BaseSubjectRetirementCursor
{
    private readonly byte[] _value;
    internal BaseSubjectRetirementCursor(ReadOnlySpan<byte> value)=>_value=value.ToArray();
    internal byte[] ToArray()=>_value.ToArray();
    /// <summary>Returns the opaque canonical cursor text.</summary>
    public override string ToString()=>System.Text.Encoding.ASCII.GetString(_value);
}

/// <summary>Contains one sanitized application-facing retirement-barrier page.</summary>
public sealed record BaseSubjectRetirementPage
{
/// <summary>Defines Barriers for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectRetirementBarrier> Barriers { get; init; }
/// <summary>Defines Next for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementCursor? Next { get; init; }
/// <summary>Defines CapturedBarrierGeneration for coordinated subject retirement.</summary>
    public required long CapturedBarrierGeneration { get; init; }
}

/// <summary>Requests inspection of one exact restore-domain subject lifetime.</summary>
public sealed record BaseSubjectRetirementInspectionRequest
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ScopeAuthority for coordinated subject retirement.</summary>
    public required BaseSubjectScopeQueryAuthority ScopeAuthority { get; init; }
/// <summary>Defines IncludeTerminalSummary for coordinated subject retirement.</summary>
    public required bool IncludeTerminalSummary { get; init; }
/// <summary>Defines MaximumResultBytes for coordinated subject retirement.</summary>
    public required long MaximumResultBytes { get; init; }
/// <summary>Defines DeadlineUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
}

/// <summary>Contains sanitized terminal evidence for one purged lifetime.</summary>
public sealed record BaseSubjectRetirementTerminalSummary
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines RetiredPosition for coordinated subject retirement.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
/// <summary>Defines PurgedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset PurgedAtUtc { get; init; }
/// <summary>Defines TerminalReceiptChecksum for coordinated subject retirement.</summary>
    public required string TerminalReceiptChecksum { get; init; }
}

/// <summary>Contains one authorized current or terminal retirement inspection result.</summary>
public sealed record BaseSubjectRetirementInspection
{
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
/// <summary>Defines CurrentBarrier for coordinated subject retirement.</summary>
    public BaseSubjectRetirementBarrier? CurrentBarrier { get; init; }
/// <summary>Defines TerminalSummary for coordinated subject retirement.</summary>
    public BaseSubjectRetirementTerminalSummary? TerminalSummary { get; init; }
/// <summary>Defines Accounting for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementReadAccounting Accounting { get; init; }
    /// <summary>Gets canonical sorted acknowledgement checksum inputs for the current barrier.</summary>
    public required ImmutableArray<string> AcknowledgementChecksumInputs { get; init; }
}

/// <summary>Provides the executable coordinated-retirement provider boundary.</summary>
public interface IBaseSubjectRetirementStore
{
    /// <summary>Executes one identified retirement mutation atomically.</summary>
    ValueTask<RecordMutationExecutionResult> ExecuteAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads one bounded, authorized barrier page.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementBarrierPage>> ReadBarriersAsync(BaseSubjectRetirementBarrierReadRequest request, CancellationToken cancellationToken = default);
    /// <summary>Inspects one exact subject lifetime and its terminal evidence.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementInspection>> InspectAsync(BaseSubjectRetirementInspectionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads committed controls without using the ordinary mutation journal.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementPublicationPage>> ReadPublicationsAsync(BaseSubjectRetirementPublicationReadRequest request, CancellationToken cancellationToken = default);
}

internal static class BaseSubjectRetirementReadIntervals
{
    internal static ImmutableArray<BaseReadIntervalEvidence> Create(
        string contractId, int contractVersion, BaseSubjectRetirementBarrierState? state,
        BaseProtectedSubjectScope? exactScope, BaseSubjectRetirementBarrierKey? after,
        BaseSubjectRetirementBarrierKey? through)
    {
        byte[] lower = after is null ? [] : EncodeKey(after);
        byte[] upper = through is null ? lower.ToArray() : EncodeKey(through);
        string scope = exactScope is null ? "all" : $"{(int)exactScope.Kind}:{Convert.ToHexString(exactScope.IndexDigest)}";
        return [new BaseReadIntervalEvidence
        {
            LogicalAccessPathId = $"subjectRetirement:barriers:{contractId}:{contractVersion}:{(state is null ? "all" : ((int)state).ToString(System.Globalization.CultureInfo.InvariantCulture))}:{scope}",
            LowerInclusive = lower,
            UpperInclusive = upper,
        }];
    }

    internal static bool Matches(
        ImmutableArray<BaseReadIntervalEvidence> intervals, string contractId, int contractVersion,
        BaseSubjectRetirementBarrierState? state, BaseProtectedSubjectScope? exactScope,
        BaseSubjectRetirementBarrierKey? after, BaseSubjectRetirementBarrierKey? through)
    {
        if (intervals.IsDefault || intervals.Length != 1) return false;
        BaseReadIntervalEvidence expected = Create(contractId, contractVersion, state, exactScope, after, through)[0];
        BaseReadIntervalEvidence actual = intervals[0];
        return actual.LogicalAccessPathId == expected.LogicalAccessPathId
            && actual.LowerInclusive.AsSpan().SequenceEqual(expected.LowerInclusive)
            && actual.UpperInclusive.AsSpan().SequenceEqual(expected.UpperInclusive);
    }

    private static byte[] EncodeKey(BaseSubjectRetirementBarrierKey key) =>
        System.Text.Encoding.UTF8.GetBytes($"{(int)key.ScopeKind:D2}\0{Convert.ToHexString(key.ScopeIndexDigest)}\0{key.ContractId}\0{key.ContractVersion:D10}\0{key.SubjectId.Value}\0{key.AuthorityEpoch.ToBase64Url()}\0{key.Incarnation.ToBase64Url()}");
}

/// <summary>Identifies the one payload present in a retirement receipt.</summary>
public enum BaseSubjectRetirementReceiptOperation
{
    /// <summary>A consumer acknowledgement.</summary>
    Acknowledgement = 0,
    /// <summary>A deadline transition.</summary>
    Timeout = 1,
    /// <summary>An audited operator override.</summary>
    Override = 2,
    /// <summary>A final physical purge.</summary>
    FinalPurge = 3,
    /// <summary>A consumer-removal operation.</summary>
    ConsumerRemoval = 4,
    /// <summary>A bounded maintenance operation.</summary>
    Maintenance = 5,
}

/// <summary>Stores the closed receipt result for one retirement operation.</summary>
public sealed record BaseSubjectRetirementReceiptResult
{
/// <summary>Defines Operation for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementReceiptOperation Operation { get; init; }
/// <summary>Defines Acknowledgement for coordinated subject retirement.</summary>
    public BaseSubjectAcknowledgementResult? Acknowledgement { get; init; }
/// <summary>Defines Timeout for coordinated subject retirement.</summary>
    public BaseSubjectRetirementTimeoutResult? Timeout { get; init; }
/// <summary>Defines Override for coordinated subject retirement.</summary>
    public BaseSubjectRetirementOverrideResult? Override { get; init; }
/// <summary>Defines Purge for coordinated subject retirement.</summary>
    public BaseSubjectFinalPurgeResult? Purge { get; init; }
/// <summary>Defines ConsumerRemoval for coordinated subject retirement.</summary>
    public BaseSubjectRetirementConsumerRemovalResult? ConsumerRemoval { get; init; }
/// <summary>Defines Maintenance for coordinated subject retirement.</summary>
    public BaseSubjectRetirementMaintenanceResult? Maintenance { get; init; }
}

/// <summary>Requests an identified timeout transition for one barrier.</summary>
public sealed record BaseSubjectRetirementTimeoutRequest
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ExpectedBarrierGeneration for coordinated subject retirement.</summary>
    public required long ExpectedBarrierGeneration { get; init; }
/// <summary>Defines ExpectedBarrierChecksum for coordinated subject retirement.</summary>
    public required string ExpectedBarrierChecksum { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns the durable result of timeout processing.</summary>
public sealed record BaseSubjectRetirementTimeoutResult
{
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines State for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrierState State { get; init; }
/// <summary>Defines Generation for coordinated subject retirement.</summary>
    public required long Generation { get; init; }
/// <summary>Defines BarrierChecksum for coordinated subject retirement.</summary>
    public required string BarrierChecksum { get; init; }
}

/// <summary>Requests an audited ControlPlane override of one barrier.</summary>
public sealed record BaseSubjectRetirementOverrideRequest
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ExpectedTombstoneSequence for coordinated subject retirement.</summary>
    public required long ExpectedTombstoneSequence { get; init; }
/// <summary>Defines ExpectedBarrierGeneration for coordinated subject retirement.</summary>
    public required long ExpectedBarrierGeneration { get; init; }
/// <summary>Defines ExpectedBarrierChecksum for coordinated subject retirement.</summary>
    public required string ExpectedBarrierChecksum { get; init; }
/// <summary>Defines Intent for coordinated subject retirement.</summary>
    public required string Intent { get; init; }
/// <summary>Defines ChangeReference for coordinated subject retirement.</summary>
    public required string ChangeReference { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns the durable result of one barrier override.</summary>
public sealed record BaseSubjectRetirementOverrideResult
{
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines Generation for coordinated subject retirement.</summary>
    public required long Generation { get; init; }
/// <summary>Defines BarrierChecksum for coordinated subject retirement.</summary>
    public required string BarrierChecksum { get; init; }
}

/// <summary>Requests removal of one mutually accepted required consumer.</summary>
public sealed record BaseSubjectRetirementConsumerRemovalRequest
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public required string ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public required int ConsumerVersion { get; init; }
/// <summary>Defines ExpectedConsumerChecksum for coordinated subject retirement.</summary>
    public required string ExpectedConsumerChecksum { get; init; }
/// <summary>Defines ExpectedAcceptedSetChecksum for coordinated subject retirement.</summary>
    public required string ExpectedAcceptedSetChecksum { get; init; }
/// <summary>Defines ExpectedGraphGeneration for coordinated subject retirement.</summary>
    public required long ExpectedGraphGeneration { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns terminal evidence for one completed consumer removal.</summary>
public sealed record BaseSubjectRetirementConsumerRemovalResult
{
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines PublishedGraphGeneration for coordinated subject retirement.</summary>
    public required long PublishedGraphGeneration { get; init; }
/// <summary>Defines AcceptedConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string AcceptedConsumerSetChecksum { get; init; }
/// <summary>Defines ExaminedBarriers for coordinated subject retirement.</summary>
    public required int ExaminedBarriers { get; init; }
/// <summary>Defines ResolvedBarriers for coordinated subject retirement.</summary>
    public required int ResolvedBarriers { get; init; }
}

/// <summary>Identifies one retirement maintenance operation.</summary>
public enum BaseSubjectRetirementMaintenanceKind
{
    /// <summary>Removes one installed consumer after resolving its barriers.</summary>
    RemoveConsumer = 0,
    /// <summary>Transforms current authority during restore.</summary>
    RestoreTransform = 1,
    /// <summary>Rebuilds protected current indexes.</summary>
    RebuildIndexes = 2,
    /// <summary>Prunes terminal evidence under the installed retention authority.</summary>
    PruneTerminalEvidence = 3,
    /// <summary>Recovers an interrupted publication.</summary>
    RecoverPublication = 4,
    /// <summary>Rotates protected scope authority across subject state.</summary>
    RotateScopeProtection = 5,
}

/// <summary>Stores the closed durable retirement-maintenance result.</summary>
public sealed record BaseSubjectRetirementMaintenanceResult
{
/// <summary>Defines Kind for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMaintenanceKind Kind { get; init; }
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines ExaminedCount for coordinated subject retirement.</summary>
    public required long ExaminedCount { get; init; }
/// <summary>Defines ChangedCount for coordinated subject retirement.</summary>
    public required long ChangedCount { get; init; }
/// <summary>Defines CanonicalBytes for coordinated subject retirement.</summary>
    public required long CanonicalBytes { get; init; }
/// <summary>Defines RollingChecksum for coordinated subject retirement.</summary>
    public required string RollingChecksum { get; init; }
/// <summary>Defines PublishedBarrierControlGeneration for coordinated subject retirement.</summary>
    public required long PublishedBarrierControlGeneration { get; init; }
}

/// <summary>Contains the lifecycle half of one shared subject-authority maintenance plan.</summary>
public sealed record BaseSubjectLifecycleMaintenancePlan
{
/// <summary>Defines Kind for coordinated subject retirement.</summary>
    public required BaseSubjectLifecycleMaintenanceKind Kind { get; init; }
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public string? ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public int? ContractVersion { get; init; }
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public string? ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public int? ConsumerVersion { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public BaseOwnedSubjectScopeEvidence? Scope { get; init; }
/// <summary>Defines RetainedFrom for coordinated subject retirement.</summary>
    public BaseSubjectLifecycleOrderingBoundary? RetainedFrom { get; init; }
/// <summary>Defines ExpectedDeliveryEpoch for coordinated subject retirement.</summary>
    public long? ExpectedDeliveryEpoch { get; init; }
/// <summary>Defines ExpectedProjectionGeneration for coordinated subject retirement.</summary>
    public long? ExpectedProjectionGeneration { get; init; }
/// <summary>Defines PlanChecksum for coordinated subject retirement.</summary>
    public required byte[] PlanChecksum { get; init; }
}

/// <summary>Contains the optional retirement half of shared subject-authority maintenance.</summary>
public sealed record BaseSubjectRetirementMaintenancePlan
{
/// <summary>Defines Kind for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMaintenanceKind Kind { get; init; }
/// <summary>Defines ScopeAuthority for coordinated subject retirement.</summary>
    public required BaseSubjectScopeQueryAuthority ScopeAuthority { get; init; }
/// <summary>Defines ExpectedGraphGeneration for coordinated subject retirement.</summary>
    public required long ExpectedGraphGeneration { get; init; }
/// <summary>Defines ExpectedBarrierControlGeneration for coordinated subject retirement.</summary>
    public required long ExpectedBarrierControlGeneration { get; init; }
/// <summary>Defines PlanChecksum for coordinated subject retirement.</summary>
    public required byte[] PlanChecksum { get; init; }
}

/// <summary>Requests one identified shared lifecycle/retirement maintenance operation.</summary>
public sealed record BaseSubjectAuthorityMaintenanceExecutionRequest
{
/// <summary>Defines Lifecycle for coordinated subject retirement.</summary>
    public required BaseSubjectLifecycleMaintenancePlan Lifecycle { get; init; }
/// <summary>Defines Retirement for coordinated subject retirement.</summary>
    public BaseSubjectRetirementMaintenancePlan? Retirement { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
/// <summary>Defines CombinedPlanChecksum for coordinated subject retirement.</summary>
    public required byte[] CombinedPlanChecksum { get; init; }
/// <summary>Defines ExpectedStoreGeneration for coordinated subject retirement.</summary>
    public required long ExpectedStoreGeneration { get; init; }
/// <summary>Defines ExpectedSchemaGeneration for coordinated subject retirement.</summary>
    public required long ExpectedSchemaGeneration { get; init; }
/// <summary>Defines ExpectedRestoreEpoch for coordinated subject retirement.</summary>
    public required long ExpectedRestoreEpoch { get; init; }
/// <summary>Defines ExpectedScopeProtectionGeneration for coordinated subject retirement.</summary>
    public required long ExpectedScopeProtectionGeneration { get; init; }
/// <summary>Defines ExpectedScopeProtectionKeyId for coordinated subject retirement.</summary>
    public required string ExpectedScopeProtectionKeyId { get; init; }
/// <summary>Defines ReplacementScopeProtectionKeyId for coordinated subject retirement.</summary>
    public string? ReplacementScopeProtectionKeyId { get; init; }
/// <summary>Gets the exact L53 authority generation when semantic authority participates in scope rotation.</summary>
    public long? ExpectedSemanticActivationAuthorityGeneration { get; init; }
/// <summary>Gets the exact L53 installed definition-set checksum when semantic authority participates in scope rotation.</summary>
    public ImmutableArray<byte> ExpectedSemanticActivationDefinitionSetChecksum { get; init; }
/// <summary>Defines PageSize for coordinated subject retirement.</summary>
    public required int PageSize { get; init; }
/// <summary>Defines OperationTimeout for coordinated subject retirement.</summary>
    public required TimeSpan OperationTimeout { get; init; }
/// <summary>Defines CommitCompletionTimeout for coordinated subject retirement.</summary>
    public required TimeSpan CommitCompletionTimeout { get; init; }
}

/// <summary>Requests one bounded page in a shared subject-authority maintenance session.</summary>
public sealed record BaseSubjectAuthorityMaintenancePageRequest
{
/// <summary>Defines FormatVersion for coordinated subject retirement.</summary>
    public required int FormatVersion { get; init; }
/// <summary>Defines LifecycleKind for coordinated subject retirement.</summary>
    public required BaseSubjectLifecycleMaintenanceKind LifecycleKind { get; init; }
/// <summary>Defines RetirementKind for coordinated subject retirement.</summary>
    public BaseSubjectRetirementMaintenanceKind? RetirementKind { get; init; }
/// <summary>Defines PageOrdinal for coordinated subject retirement.</summary>
    public required long PageOrdinal { get; init; }
/// <summary>Defines CombinedPlanChecksum for coordinated subject retirement.</summary>
    public required byte[] CombinedPlanChecksum { get; init; }
/// <summary>Defines LastCanonicalKey for coordinated subject retirement.</summary>
    public byte[]? LastCanonicalKey { get; init; }
/// <summary>Defines PageSize for coordinated subject retirement.</summary>
    public required int PageSize { get; init; }
}

/// <summary>Returns cumulative evidence for one shared maintenance page.</summary>
public sealed record BaseSubjectAuthorityMaintenancePageResult
{
/// <summary>Defines PageOrdinal for coordinated subject retirement.</summary>
    public required long PageOrdinal { get; init; }
/// <summary>Defines HasMore for coordinated subject retirement.</summary>
    public required bool HasMore { get; init; }
/// <summary>Defines NextCanonicalKey for coordinated subject retirement.</summary>
    public byte[]? NextCanonicalKey { get; init; }
/// <summary>Defines LifecycleExaminedCount for coordinated subject retirement.</summary>
    public required long LifecycleExaminedCount { get; init; }
/// <summary>Defines LifecycleChangedCount for coordinated subject retirement.</summary>
    public required long LifecycleChangedCount { get; init; }
/// <summary>Defines RetirementExaminedCount for coordinated subject retirement.</summary>
    public required long RetirementExaminedCount { get; init; }
/// <summary>Defines RetirementChangedCount for coordinated subject retirement.</summary>
    public required long RetirementChangedCount { get; init; }
/// <summary>Defines CanonicalBytes for coordinated subject retirement.</summary>
    public required long CanonicalBytes { get; init; }
/// <summary>Defines RollingChecksum for coordinated subject retirement.</summary>
    public required string RollingChecksum { get; init; }
    /// <summary>Gets the exact committed lifecycle result on the terminal page only.</summary>
    public BaseSubjectLifecycleMaintenanceResult? LifecycleResult { get; init; }
    /// <summary>Gets the exact committed retirement result on the terminal page only.</summary>
    public BaseSubjectRetirementMaintenanceResult? RetirementResult { get; init; }
}

/// <summary>Provides one native transaction page for shared subject-authority maintenance.</summary>
public interface IBaseSubjectAuthorityMaintenanceSession
{
    /// <summary>Executes one bounded page within the maintenance transaction.</summary>
    ValueTask<OperationResult<BaseSubjectAuthorityMaintenancePageResult>> ExecutePageAsync(
        BaseSubjectAuthorityMaintenancePageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Owns validation and publication for shared subject-authority maintenance.</summary>
public interface IBaseSubjectAuthorityMaintenanceProcessor
{
    /// <summary>Validates and publishes one shared maintenance execution.</summary>
    ValueTask<RecordMutationExecutionResult> ExecuteAsync(
        IBaseSubjectAuthorityMaintenanceSession session,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes bounded shared subject-authority maintenance.</summary>
public interface IBaseSubjectAuthorityMaintenanceStore
{
    /// <summary>Executes bounded shared subject-authority maintenance.</summary>
    ValueTask<RecordMutationExecutionResult> ExecuteMaintenanceAsync(
        IBaseSubjectAuthorityMaintenanceProcessor processor,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Requests final physical purge after coordinated retirement.</summary>
public sealed record BaseSubjectFinalPurgeRequest
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ExpectedTombstoneSequence for coordinated subject retirement.</summary>
    public required long ExpectedTombstoneSequence { get; init; }
/// <summary>Defines ExpectedPrivateRevision for coordinated subject retirement.</summary>
    public required RevisionToken ExpectedPrivateRevision { get; init; }
/// <summary>Defines ExpectedBarrierGeneration for coordinated subject retirement.</summary>
    public required long ExpectedBarrierGeneration { get; init; }
/// <summary>Defines ExpectedBarrierChecksum for coordinated subject retirement.</summary>
    public required string ExpectedBarrierChecksum { get; init; }
/// <summary>Defines Identity for coordinated subject retirement.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
}

/// <summary>Returns terminal purge evidence.</summary>
public sealed record BaseSubjectFinalPurgeResult
{
/// <summary>Defines Outcome for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementMutationOutcome Outcome { get; init; }
/// <summary>Defines RetiredSubjectSequence for coordinated subject retirement.</summary>
    public required long RetiredSubjectSequence { get; init; }
/// <summary>Defines RetiredPosition for coordinated subject retirement.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
/// <summary>Defines TerminalReceiptChecksum for coordinated subject retirement.</summary>
    public required string TerminalReceiptChecksum { get; init; }
}

/// <summary>Identifies one durable retirement publication position.</summary>
[JsonConverter(typeof(BaseSubjectRetirementPositionJsonConverter))]
public readonly record struct BaseSubjectRetirementPosition
{
/// <summary>Defines BaseSubjectRetirementPosition for coordinated subject retirement.</summary>
    public BaseSubjectRetirementPosition(long value) { ArgumentOutOfRangeException.ThrowIfLessThan(value, 1); Value = value; }
/// <summary>Defines Value for coordinated subject retirement.</summary>
    public long Value { get; }
}

/// <summary>Encodes retirement publication positions as their positive canonical integer.</summary>
public sealed class BaseSubjectRetirementPositionJsonConverter : JsonConverter<BaseSubjectRetirementPosition>
{
    /// <inheritdoc />
    public override BaseSubjectRetirementPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long value)
            ? new BaseSubjectRetirementPosition(value)
            : throw new JsonException("A retirement publication position must be a positive integer.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseSubjectRetirementPosition value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

/// <summary>Classifies one sanitized coordinated-retirement control publication.</summary>
public enum BaseSubjectRetirementPublicationKind
{
    /// <summary>A required-consumer barrier was created.</summary>
    BarrierCreated = 0,
    /// <summary>A required acknowledgement was accepted.</summary>
    RequiredAcknowledgementAccepted = 1,
    /// <summary>All required consumers satisfied the barrier.</summary>
    BarrierSatisfied = 2,
    /// <summary>The coordination deadline elapsed.</summary>
    BarrierTimedOut = 3,
    /// <summary>The elapsed barrier entered quarantine.</summary>
    BarrierQuarantined = 4,
    /// <summary>An authorized operator overrode the barrier.</summary>
    BarrierOverridden = 5,
    /// <summary>An advisory acknowledgement was accepted.</summary>
    AdvisoryAcknowledgementAccepted = 6,
    /// <summary>The subject was physically purged.</summary>
    SubjectPurged = 7,
    /// <summary>The accepted consumer set changed.</summary>
    ConsumerSetChanged = 8,
    /// <summary>Restore transformed current retirement authority.</summary>
    RestoreTransformed = 9,
}

/// <summary>Contains one closed coordinated-retirement control publication.</summary>
public sealed record BaseSubjectRetirementPublicationFact
{
/// <summary>Defines Position for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementPosition Position { get; init; }
/// <summary>Defines Kind for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementPublicationKind Kind { get; init; }
    /// <summary>Gets the fixed, transactionally persisted audit action.</summary>
    public string? AuditAction { get; init; }
    /// <summary>Gets the deterministic transactionally persisted invalidation identity.</summary>
    public string? InvalidationEventId { get; init; }
    /// <summary>Gets the contract identity bound to the invalidation.</summary>
    public string? InvalidationContractId { get; init; }
    /// <summary>Gets the contract version bound to the invalidation.</summary>
    public int InvalidationContractVersion { get; init; }
    /// <summary>Gets the checksum of the complete durable audit/invalidation authority.</summary>
    public string? ControlChecksum { get; init; }
/// <summary>Defines Barrier for coordinated subject retirement.</summary>
    public BaseSubjectBarrierPublication? Barrier { get; init; }
/// <summary>Defines AdvisoryAcknowledgement for coordinated subject retirement.</summary>
    public BaseSubjectAdvisoryAcknowledgementPublication? AdvisoryAcknowledgement { get; init; }
/// <summary>Defines Purged for coordinated subject retirement.</summary>
    public BaseSubjectPurgedPublication? Purged { get; init; }
/// <summary>Defines ConsumerSet for coordinated subject retirement.</summary>
    public BaseSubjectConsumerSetPublication? ConsumerSet { get; init; }
/// <summary>Defines Restore for coordinated subject retirement.</summary>
    public BaseSubjectRetirementRestorePublication? Restore { get; init; }
}

/// <summary>Contains one subject-bearing barrier publication.</summary>
public sealed record BaseSubjectBarrierPublication
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines PreviousGeneration for coordinated subject retirement.</summary>
    public required long PreviousGeneration { get; init; }
/// <summary>Defines PublishedGeneration for coordinated subject retirement.</summary>
    public required long PublishedGeneration { get; init; }
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public string? ConsumerId { get; init; }
}

/// <summary>Contains one advisory acknowledgement publication.</summary>
public sealed record BaseSubjectAdvisoryAcknowledgementPublication
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines ThroughSubjectSequence for coordinated subject retirement.</summary>
    public required long ThroughSubjectSequence { get; init; }
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public required string ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public required int ConsumerVersion { get; init; }
/// <summary>Defines Disposition for coordinated subject retirement.</summary>
    public required BaseSubjectAcknowledgementDisposition Disposition { get; init; }
}

/// <summary>Contains one terminal physical-purge publication.</summary>
public sealed record BaseSubjectPurgedPublication
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines FinalBarrierGeneration for coordinated subject retirement.</summary>
    public required long FinalBarrierGeneration { get; init; }
/// <summary>Defines FinalBarrierChecksum for coordinated subject retirement.</summary>
    public required string FinalBarrierChecksum { get; init; }
/// <summary>Defines TerminalReceiptChecksum for coordinated subject retirement.</summary>
    public required string TerminalReceiptChecksum { get; init; }
/// <summary>Defines RetiredLifecyclePosition for coordinated subject retirement.</summary>
    public required BaseMutationJournalPosition RetiredLifecyclePosition { get; init; }
}

/// <summary>Contains one contract-level accepted-consumer-set publication.</summary>
public sealed record BaseSubjectConsumerSetPublication
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines PreviousConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string PreviousConsumerSetChecksum { get; init; }
/// <summary>Defines PublishedConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string PublishedConsumerSetChecksum { get; init; }
/// <summary>Defines PreviousGraphGeneration for coordinated subject retirement.</summary>
    public required long PreviousGraphGeneration { get; init; }
/// <summary>Defines PublishedGraphGeneration for coordinated subject retirement.</summary>
    public required long PublishedGraphGeneration { get; init; }
/// <summary>Defines RemovedConsumerId for coordinated subject retirement.</summary>
    public string? RemovedConsumerId { get; init; }
}

/// <summary>Contains one contract-level restore transformation publication.</summary>
public sealed record BaseSubjectRetirementRestorePublication
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines RestoreEpoch for coordinated subject retirement.</summary>
    public required long RestoreEpoch { get; init; }
/// <summary>Defines PreviousControlGeneration for coordinated subject retirement.</summary>
    public required long PreviousControlGeneration { get; init; }
/// <summary>Defines PublishedControlGeneration for coordinated subject retirement.</summary>
    public required long PublishedControlGeneration { get; init; }
/// <summary>Defines TransformedBarrierCount for coordinated subject retirement.</summary>
    public required int TransformedBarrierCount { get; init; }
/// <summary>Defines TransformedAcknowledgementCount for coordinated subject retirement.</summary>
    public required int TransformedAcknowledgementCount { get; init; }
/// <summary>Defines TransformationChecksum for coordinated subject retirement.</summary>
    public required string TransformationChecksum { get; init; }
}

/// <summary>Stores protected scope authority beside one retirement publication.</summary>
public sealed record BaseSubjectRetirementPublicationRow
{
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public BaseProtectedSubjectScope? Scope { get; init; }
/// <summary>Defines Fact for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementPublicationFact Fact { get; init; }
}

/// <summary>Requests a bounded page from the dedicated retirement control authority.</summary>
public sealed record BaseSubjectRetirementPublicationReadRequest
{
    /// <summary>Gets the exclusive positive position boundary.</summary>
    public BaseSubjectRetirementPosition? After { get; init; }
    /// <summary>Gets the maximum rows.</summary>
    public required int Take { get; init; }
}

/// <summary>Contains one page from the dedicated retirement control authority.</summary>
public sealed record BaseSubjectRetirementPublicationPage
{
    /// <summary>Gets ordered deeply-owned publication rows.</summary>
    public required ImmutableArray<BaseSubjectRetirementPublicationRow> Rows { get; init; }
    /// <summary>Gets the finite high-water captured by this read.</summary>
    public required BaseSubjectRetirementPosition HighWater { get; init; }
}

/// <summary>Contains one sanitized post-commit retirement control and its fixed audit action.</summary>
public sealed record BaseSubjectRetirementControlNotice
{
    /// <summary>Gets the durable publication.</summary>
    public required BaseSubjectRetirementPublicationFact Publication { get; init; }
    /// <summary>Gets the fixed audit action.</summary>
    public required string AuditAction { get; init; }
    /// <summary>Gets the persisted invalidation event identity.</summary>
    public required string InvalidationEventId { get; init; }
    /// <summary>Gets the checksum of the durable control authority.</summary>
    public required string ControlChecksum { get; init; }
}

/// <summary>Observes validated post-commit retirement controls without participating in their transaction. Implementations must consume <see cref="BaseSubjectRetirementControlNotice.InvalidationEventId"/> idempotently across restart.</summary>
public interface IBaseSubjectRetirementControlObserver
{
    /// <summary>Observes one validated, non-replayed control.</summary>
    ValueTask ObserveAsync(BaseSubjectRetirementControlNotice notice,CancellationToken cancellationToken=default);
}

/// <summary>Stores one consumer acknowledgement in terminal purge evidence.</summary>
public sealed record BaseSubjectTerminalAcknowledgement
{
/// <summary>Defines ConsumerId for coordinated subject retirement.</summary>
    public required string ConsumerId { get; init; }
/// <summary>Defines ConsumerVersion for coordinated subject retirement.</summary>
    public required int ConsumerVersion { get; init; }
/// <summary>Defines ConsumerChecksum for coordinated subject retirement.</summary>
    public required string ConsumerChecksum { get; init; }
/// <summary>Defines ThroughSubjectSequence for coordinated subject retirement.</summary>
    public required long ThroughSubjectSequence { get; init; }
/// <summary>Defines Disposition for coordinated subject retirement.</summary>
    public required BaseSubjectAcknowledgementDisposition Disposition { get; init; }
/// <summary>Defines AcknowledgedPosition for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementPosition AcknowledgedPosition { get; init; }
}

/// <summary>Stores immutable provider-owned evidence that authorized one physical purge.</summary>
public sealed record BaseSubjectRetirementTerminalReceipt
{
/// <summary>Defines ContractId for coordinated subject retirement.</summary>
    public required string ContractId { get; init; }
/// <summary>Defines ContractVersion for coordinated subject retirement.</summary>
    public required int ContractVersion { get; init; }
/// <summary>Defines SubjectId for coordinated subject retirement.</summary>
    public required BaseSubjectId SubjectId { get; init; }
/// <summary>Defines Scope for coordinated subject retirement.</summary>
    public required BaseProtectedSubjectScope Scope { get; init; }
/// <summary>Defines AuthorityEpoch for coordinated subject retirement.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
/// <summary>Defines Incarnation for coordinated subject retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
/// <summary>Defines TombstoneSequence for coordinated subject retirement.</summary>
    public required long TombstoneSequence { get; init; }
/// <summary>Defines AuthorizingState for coordinated subject retirement.</summary>
    public required BaseSubjectRetirementBarrierState AuthorizingState { get; init; }
/// <summary>Defines FinalBarrierGeneration for coordinated subject retirement.</summary>
    public required long FinalBarrierGeneration { get; init; }
/// <summary>Defines FinalBarrierChecksum for coordinated subject retirement.</summary>
    public required string FinalBarrierChecksum { get; init; }
/// <summary>Defines RequiredConsumerSetChecksum for coordinated subject retirement.</summary>
    public required string RequiredConsumerSetChecksum { get; init; }
/// <summary>Defines Acknowledgements for coordinated subject retirement.</summary>
    public required ImmutableArray<BaseSubjectTerminalAcknowledgement> Acknowledgements { get; init; }
/// <summary>Defines RetiredPosition for coordinated subject retirement.</summary>
    public required BaseMutationJournalPosition RetiredPosition { get; init; }
/// <summary>Defines PurgedAtUtc for coordinated subject retirement.</summary>
    public required DateTimeOffset PurgedAtUtc { get; init; }
/// <summary>Defines ReceiptChecksum for coordinated subject retirement.</summary>
    public required string ReceiptChecksum { get; init; }
}

/// <summary>Defines certified provider bounds for coordinated retirement.</summary>
public sealed record BaseSubjectRetirementCapability
{
/// <summary>Defines TransactionalBarrierSupported for coordinated subject retirement.</summary>
    public required bool TransactionalBarrierSupported { get; init; }
/// <summary>Defines TransactionalFinalPurgeSupported for coordinated subject retirement.</summary>
    public required bool TransactionalFinalPurgeSupported { get; init; }
/// <summary>Defines MaximumRequiredConsumersPerContract for coordinated subject retirement.</summary>
    public required int MaximumRequiredConsumersPerContract { get; init; }
/// <summary>Defines MaximumAcknowledgementsPerCommit for coordinated subject retirement.</summary>
    public required int MaximumAcknowledgementsPerCommit { get; init; }
/// <summary>Defines MaximumPendingBarriers for coordinated subject retirement.</summary>
    public required long MaximumPendingBarriers { get; init; }
/// <summary>Defines MaximumCoordinationWindow for coordinated subject retirement.</summary>
    public required TimeSpan MaximumCoordinationWindow { get; init; }
/// <summary>Defines MaximumAdministrationPageSize for coordinated subject retirement.</summary>
    public required int MaximumAdministrationPageSize { get; init; }
/// <summary>Defines MaximumResultBytes for coordinated subject retirement.</summary>
    public required long MaximumResultBytes { get; init; }
/// <summary>Defines MaximumRetirementProjectionsPerCommit for coordinated subject retirement.</summary>
    public required int MaximumRetirementProjectionsPerCommit { get; init; }
/// <summary>Defines MaximumBarrierReadsPerCommit for coordinated subject retirement.</summary>
    public required int MaximumBarrierReadsPerCommit { get; init; }
/// <summary>Defines MaximumAcknowledgementReadsPerCommit for coordinated subject retirement.</summary>
    public required int MaximumAcknowledgementReadsPerCommit { get; init; }
/// <summary>Defines MaximumPublicationsPerCommit for coordinated subject retirement.</summary>
    public required int MaximumPublicationsPerCommit { get; init; }
/// <summary>Defines MaximumEvidenceBytes for coordinated subject retirement.</summary>
    public required long MaximumEvidenceBytes { get; init; }
/// <summary>Defines MaximumPublicationBytes for coordinated subject retirement.</summary>
    public required long MaximumPublicationBytes { get; init; }
/// <summary>Defines MaximumTransientBytes for coordinated subject retirement.</summary>
    public required long MaximumTransientBytes { get; init; }
/// <summary>Defines MaximumAcquisitionTimeout for coordinated subject retirement.</summary>
    public required TimeSpan MaximumAcquisitionTimeout { get; init; }
/// <summary>Defines MaximumTransactionTimeout for coordinated subject retirement.</summary>
    public required TimeSpan MaximumTransactionTimeout { get; init; }
/// <summary>Defines MaximumCommitCompletionTimeout for coordinated subject retirement.</summary>
    public required TimeSpan MaximumCommitCompletionTimeout { get; init; }
/// <summary>Defines MaximumReceiptResolutionTimeout for coordinated subject retirement.</summary>
    public required TimeSpan MaximumReceiptResolutionTimeout { get; init; }
}

/// <summary>Provides built-in InMemory and SQLite retirement capability.</summary>
public static class BaseSubjectRetirementProviderCapabilities
{
/// <summary>Defines BuiltIn for coordinated subject retirement.</summary>
    public static BaseSubjectRetirementCapability BuiltIn { get; } = new()
    {
        TransactionalBarrierSupported = true,
        TransactionalFinalPurgeSupported = true,
        MaximumRequiredConsumersPerContract = 32,
        MaximumAcknowledgementsPerCommit = 256,
        MaximumPendingBarriers = 1_000_000,
        MaximumCoordinationWindow = TimeSpan.FromDays(30),
        MaximumAdministrationPageSize = 256,
        MaximumResultBytes = 1_048_576,
        MaximumRetirementProjectionsPerCommit = 256,
        MaximumBarrierReadsPerCommit = 256,
        MaximumAcknowledgementReadsPerCommit = 256,
        MaximumPublicationsPerCommit = 256,
        MaximumEvidenceBytes = 1_048_576,
        MaximumPublicationBytes = 1_048_576,
        MaximumTransientBytes = 32_000_000,
        MaximumAcquisitionTimeout = TimeSpan.FromSeconds(5),
        MaximumTransactionTimeout = TimeSpan.FromSeconds(30),
        MaximumCommitCompletionTimeout = TimeSpan.FromSeconds(30),
        MaximumReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
    };
}
