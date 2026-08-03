using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Gateway.Management;

[BaseCollection(GatewayAuthoritySchema.AcceptedRevisions, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("namespace", nameof(NamespaceId))]
public sealed partial record GatewayAcceptedRevision
{
    private byte[] _canonicalConfigurationUtf8 = [];
    [BaseField("revision.namespace-id")] public required string NamespaceId { get; init; }
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
[BaseIndex("namespace", nameof(NamespaceId))]
public sealed partial record GatewayValidationRecord
{
    private byte[] _diagnosticsJson = [];
    [BaseField("validation.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("validation.outcome")] public required GatewayValidationOutcome Outcome { get; init; }
    [BaseField("validation.content-hash-value")] public string? ContentHashValue { get; init; }
    [BaseField("validation.diagnostics-json")] public required byte[] DiagnosticsJson { get => [.. _diagnosticsJson]; init => _diagnosticsJson = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
    [BaseField("validation.correlation-id")] public required string CorrelationId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeAudit, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("namespace", nameof(NamespaceId))]
[BaseIndex("actor", nameof(ActorId))]
public sealed partial record GatewayAdministrativeAuditRecord
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
[BaseIndex("namespace", nameof(NamespaceId))]
public sealed partial record GatewayTargetOwnership
{
    [BaseField("ownership.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("ownership.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("ownership.namespace-id")] public required string NamespaceId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.DesiredStates, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
[BaseIndex("namespace", nameof(NamespaceId))]
public sealed partial record GatewayDesiredState
{
    [BaseField("desired.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("desired.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("desired.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("desired.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("desired.revision-id")] public required string RevisionId { get; init; }
    [BaseField("desired.candidate-id")] public required string CandidateId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.NodeDeliveryAuthorities, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.Mutable)]
public sealed partial record GatewayNodeDeliveryAuthorityState
{
    [BaseField("delivery-authority.management-authority-id")] public required string ManagementAuthorityId { get; init; }
    [BaseField("delivery-authority.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("delivery-authority.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("delivery-authority.authority-id")] public required string AuthorityId { get; init; }
    [BaseField("delivery-authority.epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("delivery-authority.next-version")] public required long NextAuthorityVersion { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.ActivationIntents, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
[BaseIndex("target", nameof(TargetNodeId))]
public sealed partial record GatewayActivationIntent
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
[BaseIndex("state", nameof(State), nameof(TargetNodeId))]
public sealed partial record GatewayDeliveryOutboxItem
{
    [BaseField("outbox.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("outbox.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("outbox.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("outbox.state")] public required GatewayDeliveryState State { get; init; }
    [BaseField("outbox.attempt-count")] public required int AttemptCount { get; init; }
    [BaseField("outbox.next-attempt-at")] public DateTimeOffset? NextAttemptAt { get; init; }
    [BaseField("outbox.claim-id")] public string? ClaimId { get; init; }
    [BaseField("outbox.claim-expires-at")] public DateTimeOffset? ClaimExpiresAt { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.NodeOutcomes, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("intent", nameof(ActivationIntentId))]
public sealed partial record GatewayNodeActivationOutcome
{
    [BaseField("outcome.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("outcome.target-node-id")] public required string TargetNodeId { get; init; }
    [BaseField("outcome.activation-intent-id")] public required string ActivationIntentId { get; init; }
    [BaseField("outcome.authority-id")] public required string AuthorityId { get; init; }
    [BaseField("outcome.authority-epoch")] public required string AuthorityEpoch { get; init; }
    [BaseField("outcome.authority-version")] public required long AuthorityVersion { get; init; }
    [BaseField("outcome.kind")] public required GatewayNodeOutcomeKind Kind { get; init; }
    [BaseField("outcome.code")] public required string Code { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.CommandReceipts, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)]
[BaseIndex("lookup", nameof(NamespaceId), nameof(Operation), nameof(IdempotencyKey))]
public sealed partial record GatewayCommandReceipt
{
    private byte[] _fingerprint = [];
    [BaseField("receipt.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("receipt.operation")] public required string Operation { get; init; }
    [BaseField("receipt.idempotency-key")] public required string IdempotencyKey { get; init; }
    [BaseField("receipt.fingerprint")] public required byte[] Fingerprint { get => [.. _fingerprint]; init => _fingerprint = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
    [BaseField("receipt.result-code")] public required string StableResultCode { get; init; }
    [BaseField("receipt.operation-id")] public required string StableOperationId { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeOperationIntents, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
public sealed partial record GatewayAdministrativeOperationIntent
{
    [BaseField("admin-intent.namespace-id")] public required string NamespaceId { get; init; }
    [BaseField("admin-intent.operation")] public required GatewayAdministrativeOperationKind Operation { get; init; }
    [BaseField("admin-intent.actor-id")] public required string ActorId { get; init; }
    [BaseField("admin-intent.subject-digest")] public required string SubjectDigest { get; init; }
    [BaseField("admin-intent.expected-generation")] public long? ExpectedGeneration { get; init; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeObservations, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
public sealed partial record GatewayAdministrativeOperationObservation
{
    private byte[] _resultJson = [];
    [BaseField("admin-observation.intent-id")] public required string IntentId { get; init; }
    [BaseField("admin-observation.kind")] public required GatewayAdministrativeObservationKind Kind { get; init; }
    [BaseField("admin-observation.result-code")] public required string ResultCode { get; init; }
    [BaseField("admin-observation.provider-generation")] public long? ProviderGeneration { get; init; }
    [BaseField("admin-observation.result-json")] public required byte[] ResultJson { get => [.. _resultJson]; init => _resultJson = value is null ? throw new ArgumentNullException(nameof(value)) : [.. value]; }
}

[BaseCollection(GatewayAuthoritySchema.AdministrativeCompletions, typeof(GatewayManagementJsonContext), MutationMode = BaseCollectionMutationMode.AppendOnly)]
public sealed partial record GatewayAdministrativeOperationCompletion
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
[JsonSerializable(typeof(GatewayDesiredState))]
[JsonSerializable(typeof(GatewayNodeDeliveryAuthorityState))]
[JsonSerializable(typeof(GatewayActivationIntent))]
[JsonSerializable(typeof(GatewayDeliveryOutboxItem))]
[JsonSerializable(typeof(GatewayNodeActivationOutcome))]
[JsonSerializable(typeof(GatewayCommandReceipt))]
[JsonSerializable(typeof(GatewayAdministrativeOperationIntent))]
[JsonSerializable(typeof(GatewayAdministrativeOperationObservation))]
[JsonSerializable(typeof(GatewayAdministrativeOperationCompletion))]
public sealed partial class GatewayManagementJsonContext : JsonSerializerContext;
