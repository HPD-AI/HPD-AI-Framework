using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Gateway.ControlPlane;

[BaseCollection(GatewayAuthoritySchema.AcceptedRevisions, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("gateway.revisions.namespace-target", nameof(NamespaceId), nameof(TargetNodeId), Required = false)]
internal sealed partial record GatewayAcceptedRevision
{
    private byte[] _canonicalConfigurationUtf8 = [];
    [BaseField("revision.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("revision.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("revision.content-hash-algorithm")] public required string ContentHashAlgorithm { get; init; }
    [BaseField("revision.content-hash-value")] public required string ContentHashValue { get; init; }
    [BaseField("revision.canonical-configuration")] public required byte[] CanonicalConfigurationUtf8 { get => [.. _canonicalConfigurationUtf8]; init => _canonicalConfigurationUtf8 = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
    [BaseField("revision.schema-version")] public required string SchemaVersion { get; init; }
    [BaseField("revision.canonicalization-version")] public required string CanonicalizationVersion { get; init; }
    [BaseField("revision.parent-id")] public string? ParentRevisionId { get; init; }
    [BaseField("revision.derived-from-id")] public string? DerivedFromRevisionId { get; init; }
    [BaseField("revision.validation-id")] public required string ValidationId { get; init; }
    [BaseField("revision.actor-id")] public required string ActorId { get; init; }
    [BaseField("revision.source-kind")] public required string SourceKind { get; init; }
    [BaseField("revision.source-id")] public required string SourceId { get; init; }
    [BaseField("revision.correlation-id")] public required string CorrelationId { get; init; }
    [BaseField("revision.description")] public string? Description { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.ValidationRecords, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("gateway.validations.namespace-target", nameof(NamespaceId), nameof(TargetNodeId), Required = false)]
internal sealed partial record GatewayValidationRecord
{
    private byte[] _diagnosticsJson = [];
    [BaseField("validation.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("validation.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("validation.outcome")] public required GatewayValidationOutcome Outcome { get; init; }
    [BaseField("validation.content-hash-value")] public string? ContentHashValue { get; init; }
    [BaseField("validation.diagnostics-json")] public required byte[] DiagnosticsJson { get => [.. _diagnosticsJson]; init => _diagnosticsJson = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
    [BaseField("validation.correlation-id")] public required string CorrelationId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeAudit, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("gateway.audit.namespace", nameof(NamespaceId), Required = false)]
[BaseIndex("gateway.audit.actor", nameof(ActorId), Required = false)]
internal sealed partial record GatewayAdministrativeAuditRecord
{
    [BaseField("audit.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("audit.actor-id")] public required string ActorId { get; init; }
    [BaseField("audit.authentication-scheme")] public required string AuthenticationScheme { get; init; }
    [BaseField("audit.authorization-policy")] public required string AuthorizationPolicy { get; init; }
    [BaseField("audit.operation")] public required string Operation { get; init; }
    [BaseField("audit.result-code")] public required string ResultCode { get; init; }
    [BaseField("audit.correlation-id")] public required string CorrelationId { get; init; }
    [BaseField("audit.subject-id")] public required string SubjectId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.TargetOwnership, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
[BaseIndex("gateway.ownership.namespace", nameof(NamespaceId), Required = false)]
internal sealed partial record GatewayTargetOwnership
{
    [BaseField("ownership.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("ownership.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("ownership.namespace-id")] public required string NamespaceId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.TargetEpochReservations, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayTargetEpochReservation
{
    [BaseField("epoch-reservation.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("epoch-reservation.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("epoch-reservation.authority-epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("epoch-reservation.contract-version")] public required string ContractVersion { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.TargetEpochReservationReceipts, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayTargetEpochReservationReceipt
{
    [BaseField("epoch-reservation-receipt.reservation-id")] public required string ReservationId { get; init; }
    [BaseField("epoch-reservation-receipt.epoch-digest")] public required string EpochDigest { get; init; }
    [BaseField("epoch-reservation-receipt.result-code")] public required string StableResultCode { get; init; }
    [BaseField("epoch-reservation-receipt.contract-version")] public required string ContractVersion { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.DesiredStates, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
[BaseIndex("gateway.desired.namespace", nameof(NamespaceId), Required = false)]
internal sealed partial record GatewayDesiredState
{
    [BaseField("desired.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("desired.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("desired.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("desired.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("desired.revision-id")] public required string RevisionId { get; init; }
    [BaseField("desired.candidate-id")] public required string CandidateId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.NodeDeliveryAuthorities, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
internal sealed partial record GatewayNodeDeliveryAuthorityState
{
    [BaseField("delivery-authority.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("delivery-authority.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("delivery-authority.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("delivery-authority.authority-id")] public required string AuthorityId { get; init; }
    [BaseField("delivery-authority.epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("delivery-authority.next-version")] public required long NextAuthorityVersion { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.ActivationIntents, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
[BaseIndex("gateway.intents.namespace-target", nameof(NamespaceId), nameof(TargetNodeId), Required = false)]
internal sealed partial record GatewayActivationIntent
{
    [BaseField("intent.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("intent.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("intent.revision-id")] public required string RevisionId { get; init; }
    [BaseField("intent.candidate-id")] public required string CandidateId { get; init; }
    [BaseField("intent.content-hash-value")] public required string ContentHashValue { get; init; }
    [BaseField("intent.authority-id")] public required string AuthorityId { get; init; }
    [BaseField("intent.authority-epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("intent.authority-version")] public required long AuthorityVersion { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.DeliveryOutbox, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
[BaseIndex("gateway.outbox.state", nameof(State), Required = false)]
[BaseIndex("gateway.outbox.state-target", nameof(State), nameof(TargetNodeId), Required = false)]
[BaseIndex("gateway.outbox.namespace-target", nameof(NamespaceId), nameof(TargetNodeId), Required = false)]
[BaseIndex("gateway.outbox.state-next-attempt", nameof(State), nameof(NextAttemptAt), Required = false)]
[BaseIndex("gateway.outbox.state-claim-expiry", nameof(State), nameof(ClaimExpiresAt), Required = false)]
internal sealed partial record GatewayDeliveryOutboxItem
{
    [BaseField("outbox.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("outbox.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("outbox.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("outbox.state")] public required GatewayDeliveryState State { get; init; }
    [BaseField("outbox.attempt-count")] public required int AttemptCount { get; init; }
    [BaseField("outbox.next-attempt-at", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public DateTimeOffset? NextAttemptAt { get; init; }
    [BaseField("outbox.claim-id")] public string? ClaimId { get; init; }
    [BaseField("outbox.claim-expires-at", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public DateTimeOffset? ClaimExpiresAt { get; init; }
    [BaseField("outbox.pending-outcome-kind")] public GatewayNodeOutcomeKind? PendingOutcomeKind { get; init; }
    [BaseField("outbox.pending-outcome-code")] public string? PendingOutcomeCode { get; init; }
    [BaseField("outbox.pending-application-id")] public string? PendingApplicationId { get; init; }
    [BaseField("outbox.pending-symbolic-plan-algorithm")] public string? PendingSymbolicPlanAlgorithm { get; init; }
    [BaseField("outbox.pending-symbolic-plan-value")] public string? PendingSymbolicPlanValue { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.NodeOutcomes, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("gateway.outcomes.intent", nameof(ActivationIntentId), Required = false)]
[BaseIndex("gateway.outcomes.namespace-target", nameof(NamespaceId), nameof(TargetNodeId), Required = false)]
internal sealed partial record GatewayNodeActivationOutcome
{
    [BaseField("outcome.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("outcome.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("outcome.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("outcome.authority-id")] public required string AuthorityId { get; init; }
    [BaseField("outcome.authority-epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("outcome.authority-version", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public required long AuthorityVersion { get; init; }
    [BaseField("outcome.kind")] public required GatewayNodeOutcomeKind Kind { get; init; }
    [BaseField("outcome.code")] public required string Code { get; init; }
    [BaseField("outcome.application-id")] public string? ApplicationId { get; init; }
    [BaseField("outcome.symbolic-plan-algorithm")] public string? SymbolicPlanAlgorithm { get; init; }
    [BaseField("outcome.symbolic-plan-value")] public string? SymbolicPlanValue { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.CommandReceipts, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
[BaseIndex("gateway.receipts.lookup", nameof(NamespaceId), nameof(TargetNodeId), nameof(Operation), nameof(IdempotencyKey), Required = false)]
[BaseIndex("gateway.receipts.operation", nameof(NamespaceId), nameof(StableOperationId), Required = false)]
internal sealed partial record GatewayCommandReceipt
{
    private byte[] _fingerprint = [];
    [BaseField("receipt.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("receipt.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("receipt.operation")] public required string Operation { get; init; }
    [BaseField("receipt.idempotency-key")] public required string IdempotencyKey { get; init; }
    [BaseField("receipt.fingerprint")] public required byte[] Fingerprint { get => [.. _fingerprint]; init => _fingerprint = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
    [BaseField("receipt.result-code")] public required string StableResultCode { get; init; }
    [BaseField("receipt.operation-id")] public required string StableOperationId { get; init; }
    [BaseField("receipt.revision-id")] public string? StableRevisionId { get; init; }
    [BaseField("receipt.activation-intent-id")] public string? StableActivationIntentId { get; init; }
    [BaseField("receipt.desired-state-token")] public string? StableDesiredStateToken { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeOperationIntents, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayAdministrativeOperationIntent
{
    private byte[]? _purgeRecordIdsJson;
    [BaseField("admin-intent.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("admin-intent.operation")] public required GatewayAdministrativeOperationKind Operation { get; init; }
    [BaseField("admin-intent.actor-id")] public required string ActorId { get; init; }
    [BaseField("admin-intent.authentication-scheme")] public required string AuthenticationScheme { get; init; }
    [BaseField("admin-intent.authorization-policy")] public required string AuthorizationPolicy { get; init; }
    [BaseField("admin-intent.subject-digest")] public required string SubjectDigest { get; init; }
    [BaseField("admin-intent.backup-sink-name")] public string? BackupSinkName { get; init; }
    [BaseField("admin-intent.backup-artifact-label")] public string? BackupArtifactLabel { get; init; }
    [BaseField("admin-intent.expected-generation")] public long? ExpectedGeneration { get; init; }
    [BaseField("admin-intent.purge-collection-id")] public string? PurgeCollectionId { get; init; }
    [BaseField("admin-intent.purge-record-ids-json")] public byte[]? PurgeRecordIdsJson { get => _purgeRecordIdsJson is null ? null : [.. _purgeRecordIdsJson]; init => _purgeRecordIdsJson = value is null ? null : [.. value]; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeArtifacts, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayAdministrativeArtifactObservation
{
    [BaseField("admin-artifact.intent-id")] public required string IntentId { get; init; }
    [BaseField("admin-artifact.sink-name")] public required string SinkName { get; init; }
    [BaseField("admin-artifact.public-reference")] public required string PublicReference { get; init; }
    [BaseField("admin-artifact.observed-at")] public required DateTimeOffset ObservedAt { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeExecutions, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
internal sealed partial record GatewayAdministrativeExecutionState
{
    [BaseField("admin-execution.intent-id")] public required string IntentId { get; init; }
    [BaseField("admin-execution.phase")] public required GatewayAdministrativeExecutionPhase Phase { get; init; }
    [BaseField("admin-execution.state-revision", Operators = BaseFieldOperator.Equal | BaseFieldOperator.Order)] public required long StateRevision { get; init; }
    [BaseField("admin-execution.claim-id")] public string? ClaimId { get; init; }
    [BaseField("admin-execution.lease-expires-at")] public DateTimeOffset? LeaseExpiresAt { get; init; }
    [BaseField("admin-execution.boundary-crossed-at")] public DateTimeOffset? BoundaryCrossedAt { get; init; }
    [BaseField("admin-execution.observation-id")] public string? ObservationId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.PurgeAuthorities, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
internal sealed partial record GatewayPurgeAuthorityState
{
    [BaseField("purge-authority.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("purge-authority.collection-id")] public required string CollectionId { get; init; }
    [BaseField("purge-authority.confirmed-generation")] public required long ConfirmedGeneration { get; init; }
    [BaseField("purge-authority.pending-intent-id")] public string? PendingIntentId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeObservations, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayAdministrativeOperationObservation
{
    private byte[] _resultJson = [];
    [BaseField("admin-observation.intent-id")] public required string IntentId { get; init; }
    [BaseField("admin-observation.kind")] public required GatewayAdministrativeObservationKind Kind { get; init; }
    [BaseField("admin-observation.result-code")] public required string ResultCode { get; init; }
    [BaseField("admin-observation.provider-generation")] public long? ProviderGeneration { get; init; }
    [BaseField("admin-observation.result-json")] public required byte[] ResultJson { get => [.. _resultJson]; init => _resultJson = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeCompletions, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
internal sealed partial record GatewayAdministrativeOperationCompletion
{
    [BaseField("admin-completion.intent-id")] public required string IntentId { get; init; }
    [BaseField("admin-completion.observation-id")] public required string ObservationId { get; init; }
    [BaseField("admin-completion.state")] public required GatewayAdministrativeCompletionState State { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GatewayAcceptedRevision))]
[JsonSerializable(typeof(GatewayValidationRecord))]
[JsonSerializable(typeof(GatewayAdministrativeAuditRecord))]
[JsonSerializable(typeof(GatewayTargetOwnership))]
[JsonSerializable(typeof(GatewayTargetEpochReservation))]
[JsonSerializable(typeof(GatewayTargetEpochReservationReceipt))]
[JsonSerializable(typeof(GatewayDesiredState))]
[JsonSerializable(typeof(GatewayNodeDeliveryAuthorityState))]
[JsonSerializable(typeof(GatewayActivationIntent))]
[JsonSerializable(typeof(GatewayDeliveryOutboxItem))]
[JsonSerializable(typeof(GatewayNodeActivationOutcome))]
[JsonSerializable(typeof(GatewayCommandReceipt))]
[JsonSerializable(typeof(GatewayAdministrativeOperationIntent))]
[JsonSerializable(typeof(GatewayAdministrativeExecutionState))]
[JsonSerializable(typeof(GatewayAdministrativeArtifactObservation))]
[JsonSerializable(typeof(GatewayAdministrativeOperationObservation))]
[JsonSerializable(typeof(GatewayAdministrativeOperationCompletion))]
[JsonSerializable(typeof(GatewayPurgeAuthorityState))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class GatewayManagementJsonContext : JsonSerializerContext;
