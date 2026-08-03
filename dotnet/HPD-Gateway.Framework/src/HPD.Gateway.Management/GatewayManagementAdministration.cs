using System.Security.Cryptography;
using System.Text;
using HPD.Base;

namespace HPD.Gateway.Management;

public sealed record GatewayAdministrativeResult(
    string OperationId,
    GatewayAdministrativeCompletionState State,
    string Code,
    long? ProviderGeneration = null);

public interface IGatewayManagementAdministration
{
    bool ManagedRestoreSupported { get; }
    ValueTask<GatewayAdministrativeResult> CreateBackupAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        Stream destination,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayAdministrativeResult> PurgeAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        string collectionId,
        IReadOnlyList<string> recordIds,
        long? expectedGeneration,
        CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementAdministration(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    IHPDBaseAdministration administration,
    TimeProvider timeProvider) : IGatewayManagementAdministration
{
    public bool ManagedRestoreSupported => false;

    public async ValueTask<GatewayAdministrativeResult> CreateBackupAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        GatewayAuthorityCapabilitySnapshot capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.BackupSupported)
            return new("", GatewayAdministrativeCompletionState.IndeterminatePending, "management.backup.unsupported");
        string operationId = await PersistIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Backup,
            $"store:{capabilities.StoreId}", null, cancellationToken).ConfigureAwait(false);
        BaseResult<BaseBackupManifest> executed = await administration.CreateBackupAsync(destination, new BaseBackupRequest
        {
            StoreId = capabilities.StoreId,
            Principal = Principal(),
        }, cancellationToken).ConfigureAwait(false);
        return await PersistObservation(
            namespaceId, idempotencyKey, operationId, executed.Status,
            executed is BaseFailure<BaseBackupManifest> failure ? failure.Error.Code : "base.admin.backup.succeeded",
            null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<GatewayAdministrativeResult> PurgeAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        string collectionId,
        IReadOnlyList<string> recordIds,
        long? expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        GatewayAuthorityCapabilitySnapshot capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.PurgeSupported)
            return new("", GatewayAdministrativeCompletionState.IndeterminatePending, "management.purge.unsupported");
        if (recordIds is null or { Count: < 1 or > 256 })
            throw new ArgumentOutOfRangeException(nameof(recordIds));
        if (collectionId is not (GatewayAuthoritySchema.AcceptedRevisions
            or GatewayAuthoritySchema.ValidationRecords
            or GatewayAuthoritySchema.AdministrativeAudit
            or GatewayAuthoritySchema.NodeOutcomes
            or GatewayAuthoritySchema.CommandReceipts))
            throw new ArgumentException("The collection is not Gateway purge-enabled.", nameof(collectionId));
        RecordId[] ids = recordIds.Select(RecordId.Create).ToArray();
        if (collectionId == GatewayAuthoritySchema.AcceptedRevisions)
            await RejectDesiredRevisionPurge(ids, cancellationToken).ConfigureAwait(false);

        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            collectionId + "\n" + string.Join('\n', recordIds.Order(StringComparer.Ordinal)))));
        string operationId = await PersistIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Purge,
            digest, expectedGeneration, cancellationToken).ConfigureAwait(false);
        BaseResult<BasePurgeResult> executed = await administration.PurgeAsync(new BasePurgeRequest
        {
            CollectionId = collectionId,
            RecordIds = ids,
            Principal = Principal(),
            ReasonCode = "gateway.retention",
            AuditReference = operationId,
            EvaluatedAt = timeProvider.GetUtcNow(),
            ExpectedPurgeGeneration = expectedGeneration,
        }, cancellationToken).ConfigureAwait(false);
        long? generation = executed is BaseSuccess<BasePurgeResult> success ? success.Value.PurgeGeneration : null;
        return await PersistObservation(
            namespaceId, idempotencyKey, operationId, executed.Status,
            executed is BaseFailure<BasePurgeResult> failure ? failure.Error.Code : "base.admin.purge.succeeded",
            generation, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RejectDesiredRevisionPurge(RecordId[] ids, CancellationToken cancellationToken)
    {
        BaseRecord<GatewayDesiredState>[] desired = (await Session(null)
            .Collection(GatewayDesiredState.Collection).Query().Take(4_096).ToArrayAsync(4_096, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        HashSet<string> protectedIds = desired.Select(static item => item.Value.RevisionId).ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => protectedIds.Contains(id.Value)))
            throw new InvalidOperationException("A revision selected by desired state cannot be purged.");
    }

    private async ValueTask<string> PersistIntent(
        string namespaceId, string key, GatewayManagementActor actor,
        GatewayAdministrativeOperationKind operation, string subjectDigest,
        long? expectedGeneration, CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.CommandFact(
            "admin-intent", namespaceId, operation.ToString().ToLowerInvariant(), key, "v1");
        var value = new GatewayAdministrativeOperationIntent
        {
            NamespaceId = namespaceId,
            Operation = operation,
            ActorId = actor.ActorId,
            SubjectDigest = subjectDigest,
            ExpectedGeneration = expectedGeneration,
        };
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationIntent>> result = await Session(namespaceId)
            .Collection(GatewayAdministrativeOperationIntent.Collection)
            .EnsureAsync(id, value, cancellationToken).ConfigureAwait(false);
        result.RequireValue();
        return id.Value;
    }

    private async ValueTask<GatewayAdministrativeResult> PersistObservation(
        string namespaceId, string key, string intentId, OperationStatus status,
        string code, long? generation, CancellationToken cancellationToken)
    {
        GatewayAdministrativeObservationKind kind = status == OperationStatus.Ok
            ? GatewayAdministrativeObservationKind.Succeeded
            : code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase)
                ? GatewayAdministrativeObservationKind.Indeterminate
                : GatewayAdministrativeObservationKind.Failed;
        RecordId observationId = GatewayAuthorityRecordIds.CommandFact(
            "admin-observation", namespaceId, "observe", key, intentId, kind.ToString(), code, "v1");
        var observation = new GatewayAdministrativeOperationObservation
        {
            IntentId = intentId,
            Kind = kind,
            ResultCode = code,
            ProviderGeneration = generation,
            ResultJson = Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}"),
        };
        BaseSession session = Session(namespaceId);
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationObservation>> observed = await session
            .Collection(GatewayAdministrativeOperationObservation.Collection)
            .EnsureAsync(observationId, observation, cancellationToken).ConfigureAwait(false);
        if (!observed.TryGetValue(out _))
            return new(intentId, GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending,
                "management.admin.observation-pending", generation);

        GatewayAdministrativeCompletionState state = kind == GatewayAdministrativeObservationKind.Succeeded
            ? GatewayAdministrativeCompletionState.Completed
            : GatewayAdministrativeCompletionState.IndeterminatePending;
        RecordId completionId = GatewayAuthorityRecordIds.CommandFact(
            "admin-completion", namespaceId, "complete", key, intentId, observationId.Value, "v1");
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationCompletion>> completed = await session
            .Collection(GatewayAdministrativeOperationCompletion.Collection)
            .EnsureAsync(completionId, new GatewayAdministrativeOperationCompletion
            {
                IntentId = intentId,
                ObservationId = observationId.Value,
                State = state,
            }, cancellationToken).ConfigureAwait(false);
        return completed.TryGetValue(out _)
            ? new(intentId, state, code, generation)
            : new(intentId, GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending,
                "management.admin.completion-pending", generation);
    }

    private BaseSession Session(string? namespaceId) => sessions.For(Principal(), value =>
    {
        value.Mode = OperationMode.System;
        value.TenantId = namespaceId;
    });

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectId = "hpd.gateway.management.administration",
        AuthSource = GatewayManagementBasePolicy.TrustedSource,
    };
}
