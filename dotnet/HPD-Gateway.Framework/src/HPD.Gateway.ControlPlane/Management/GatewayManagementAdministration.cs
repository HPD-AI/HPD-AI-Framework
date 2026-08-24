using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Gateway.ControlPlane;

internal readonly record struct GatewayAdministrativeObservationCommitResolution(
    BaseRecordBatchOutcome? Outcome,
    string? FailureCode);

internal static class GatewayAdministrativeObservationReceiptResolver
{
    internal static async ValueTask<GatewayAdministrativeObservationCommitResolution> ResolveAsync(
        Func<CancellationToken, ValueTask<GatewayAdministrativeObservationCommitResolution>> commit,
        CancellationToken cancellationToken)
    {
        GatewayAdministrativeObservationCommitResolution result = await commit(cancellationToken).ConfigureAwait(false);
        return StringComparer.Ordinal.Equals(result.FailureCode, BaseMutationRequestErrorCodes.OutcomeUnknown)
            ? await commit(CancellationToken.None).ConfigureAwait(false)
            : result;
    }
}

public sealed record GatewayAdministrativeResult(
    string OperationId,
    GatewayAdministrativeCompletionState State,
    string Code,
    long? ProviderGeneration = null,
    string? ArtifactReference = null);

public sealed record GatewayBackupArtifact(string PublicReference, Stream Destination);

public interface IGatewayBackupSink
{
    string Name { get; }
    ValueTask<GatewayBackupArtifact> PrepareOrResolveAsync(
        string operationId,
        string? artifactLabel,
        CancellationToken cancellationToken = default);
}

public enum GatewayManagementPurgeCategory : byte
{
    RevisionContent,
    ValidationContent,
    ActivationOutcomeHistory,
    AuditHistory,
}

public interface IGatewayManagementAdministration
{
    bool ManagedRestoreSupported { get; }
    ValueTask<GatewayAdministrativeResult> CreateBackupAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        string sinkName,
        string? artifactLabel,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayAdministrativeResult> PurgeAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        string collectionId,
        IReadOnlyList<string> recordIds,
        long? expectedGeneration,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayAdministrativeResult> RequestPurgeAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        GatewayManagementPurgeCategory category,
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default);
    ValueTask<int> ReconcilePendingAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementAdministration(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    IHPDBaseAdministration administration,
    GatewayBackupSinkRegistry backupSinks,
    GatewayManagementRuntimeOptions options,
    TimeProvider timeProvider) : IGatewayManagementAdministration
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingObservation> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _administration = new(1, 1);
    public bool ManagedRestoreSupported => false;

    public ValueTask<GatewayAdministrativeResult> RequestPurgeAsync(
        string namespaceId, string idempotencyKey, GatewayManagementActor actor,
        GatewayManagementPurgeCategory category, IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        string collection = category switch
        {
            GatewayManagementPurgeCategory.RevisionContent => GatewayAuthoritySchema.AcceptedRevisions,
            GatewayManagementPurgeCategory.ValidationContent => GatewayAuthoritySchema.ValidationRecords,
            GatewayManagementPurgeCategory.ActivationOutcomeHistory => GatewayAuthoritySchema.NodeOutcomes,
            GatewayManagementPurgeCategory.AuditHistory => GatewayAuthoritySchema.AdministrativeAudit,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
        return PurgeAsync(namespaceId, idempotencyKey, actor, collection, resourceIds, null, cancellationToken);
    }

    public async ValueTask<GatewayAdministrativeResult> CreateBackupAsync(
        string namespaceId,
        string idempotencyKey,
        GatewayManagementActor actor,
        string sinkName,
        string? artifactLabel,
        CancellationToken cancellationToken = default)
    {
        await _administration.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        if (!ValidSinkName(sinkName) || artifactLabel is not null && !ValidArtifactLabel(artifactLabel))
            throw new ArgumentException("The backup sink identity is invalid.", nameof(sinkName));
        GatewayAuthorityCapabilitySnapshot capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.BackupSupported)
            return new("", GatewayAdministrativeCompletionState.IndeterminatePending, "management.backup.unsupported");
        PreparedIntent prepared = await PrepareIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Backup,
            $"store:{capabilities.StoreId}:sink:{sinkName}:label:{artifactLabel ?? ""}", null, null, null,
            sinkName, artifactLabel, cancellationToken).ConfigureAwait(false);
        if (prepared.Failure is not null) return prepared.Failure;
        if (!prepared.Created)
            return await ResolveOrRecoverBackup(prepared, cancellationToken).ConfigureAwait(false);
        if (!backupSinks.TryGet(sinkName, out IGatewayBackupSink? sink))
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.backup.sink-unavailable");
        BaseRecord<GatewayAdministrativeExecutionState> claimed = await ClaimPreBoundary(prepared, cancellationToken).ConfigureAwait(false);
        await CrossBoundary(prepared, claimed, cancellationToken).ConfigureAwait(false);
        return await ExecuteBackup(prepared, sink!, executeProvider: true, cancellationToken).ConfigureAwait(false);
        }
        finally { _administration.Release(); }
    }

    private async ValueTask<GatewayAdministrativeResult> ExecuteBackup(
        PreparedIntent prepared,
        IGatewayBackupSink sink,
        bool executeProvider,
        CancellationToken cancellationToken)
    {
        GatewayAuthorityCapabilitySnapshot capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        GatewayBackupArtifact artifact;
        try
        {
            artifact = await sink.PrepareOrResolveAsync(
                prepared.Id, prepared.Record!.Value.BackupArtifactLabel, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!Fatal(exception))
        {
            return await RecordProviderResult(prepared.NamespaceId, prepared.Id, OperationStatus.StoreError,
                "management.backup.sink-indeterminate", null, CancellationToken.None).ConfigureAwait(false);
        }
        if (artifact.Destination is null || !artifact.Destination.CanWrite || !ValidPublicReference(artifact.PublicReference))
        {
            artifact.Destination?.Dispose();
            return await RecordProviderResult(prepared.NamespaceId, prepared.Id, OperationStatus.ValidationFailed,
                "management.backup.sink-invalid", null, cancellationToken).ConfigureAwait(false);
        }
        await EnsureArtifact(prepared, sink.Name, artifact.PublicReference, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!executeProvider)
                return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                    "management.admin.execution-indeterminate", ArtifactReference: artifact.PublicReference);
            BaseResult<BaseBackupManifest> executed;
            try
            {
                executed = await administration.CreateBackupAsync(artifact.Destination, new BaseBackupRequest
                {
                    StoreId = capabilities.StoreId,
                    Principal = Principal(),
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!Fatal(exception))
            {
                return await RecordProviderResult(prepared.NamespaceId, prepared.Id, OperationStatus.StoreError,
                    "management.backup.provider-indeterminate", null, CancellationToken.None).ConfigureAwait(false);
            }
            GatewayAdministrativeResult result = await RecordProviderResult(
                prepared.NamespaceId, prepared.Id, executed.Status,
                executed is BaseFailure<BaseBackupManifest> failure ? failure.Error.Code : "base.admin.backup.succeeded",
                null, cancellationToken).ConfigureAwait(false);
            return result with { ArtifactReference = artifact.PublicReference };
        }
        finally
        {
            try { await artifact.Destination.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (!Fatal(exception)) { }
        }
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
        await _administration.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        GatewayAuthorityCapabilitySnapshot capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.PurgeSupported)
            return new("", GatewayAdministrativeCompletionState.IndeterminatePending, "management.purge.unsupported");
        if (recordIds is null or { Count: < 1 or > 256 })
            throw new ArgumentOutOfRangeException(nameof(recordIds));
        if (collectionId is not (GatewayAuthoritySchema.AcceptedRevisions
            or GatewayAuthoritySchema.ValidationRecords
            or GatewayAuthoritySchema.AdministrativeAudit
            or GatewayAuthoritySchema.NodeOutcomes))
            throw new ArgumentException("The collection is not Gateway purge-enabled.", nameof(collectionId));
        RecordId[] ids = recordIds.Select(RecordId.Create).ToArray();
        await EnforceRetentionClosure(collectionId, ids, cancellationToken).ConfigureAwait(false);
        await ValidatePurgeGeneration(collectionId, expectedGeneration, cancellationToken).ConfigureAwait(false);

        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            collectionId + "\n" + string.Join('\n', recordIds.Order(StringComparer.Ordinal)))));
        PreparedIntent prepared = await PrepareIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Purge,
            digest, expectedGeneration, collectionId,
            recordIds.Order(StringComparer.Ordinal).ToArray(), null, null, cancellationToken).ConfigureAwait(false);
        if (prepared.Failure is not null) return prepared.Failure;
        if (!prepared.Created)
            return await ResolveOrRecoverPurge(prepared, cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayPurgeAuthorityState> fence = await ClaimPurgeFence(
            collectionId, prepared.Id, expectedGeneration, cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAdministrativeExecutionState> claimed = await ClaimPreBoundary(prepared, cancellationToken).ConfigureAwait(false);
        await CrossBoundary(prepared, claimed, cancellationToken).ConfigureAwait(false);
        return await ExecutePurge(prepared, fence, cancellationToken).ConfigureAwait(false);
        }
        finally { _administration.Release(); }
    }

    public async ValueTask<int> ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _administration.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var completed = 0;
        foreach (PendingObservation pending in _pending.Values.ToArray())
        {
            GatewayAdministrativeResult result = await PersistObservation(
                pending.NamespaceId, pending.IntentId,
                pending.Status, pending.Code, pending.Generation, cancellationToken).ConfigureAwait(false);
            if (result.State != GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending)
            {
                _pending.TryRemove(pending.IntentId, out _);
                completed++;
            }
        }

        BaseRecord<GatewayAdministrativeOperationObservation>[] observations = await All(
            Session(null).Collection(GatewayAdministrativeOperationObservation.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAdministrativeOperationCompletion>[] completions = await All(
            Session(null).Collection(GatewayAdministrativeOperationCompletion.Collection), cancellationToken).ConfigureAwait(false);
        HashSet<string> completedObservations = completions.Select(static value => value.Value.ObservationId).ToHashSet(StringComparer.Ordinal);
        foreach (BaseRecord<GatewayAdministrativeOperationObservation> observation in observations)
        {
            if (completedObservations.Contains(observation.Id.Value)) continue;
            BaseRecord<GatewayAdministrativeOperationIntent> intent = (await Session(null)
                .Collection(GatewayAdministrativeOperationIntent.Collection)
                .GetAsync(RecordId.Create(observation.Value.IntentId), cancellationToken).ConfigureAwait(false)).RequireValue();
            GatewayAdministrativeResult result = await PersistCompletion(
                intent.Value.NamespaceId, observation.Value.IntentId,
                observation.Id.Value, observation.Value.Kind, observation.Value.ResultCode,
                observation.Value.ProviderGeneration, cancellationToken).ConfigureAwait(false);
            if (intent.Value.Operation == GatewayAdministrativeOperationKind.Purge)
                await FinalizePurgeFence(
                    intent.Value.PurgeCollectionId!, observation.Value.IntentId,
                    observation.Value.Kind == GatewayAdministrativeObservationKind.Succeeded
                        ? observation.Value.ProviderGeneration
                        : null,
                    cancellationToken).ConfigureAwait(false);
            if (result.State != GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending) completed++;
        }
        HashSet<string> observedIntents = observations.Select(static value => value.Value.IntentId)
            .ToHashSet(StringComparer.Ordinal);
        BaseRecord<GatewayAdministrativeOperationIntent>[] intents = await All(
            Session(null).Collection(GatewayAdministrativeOperationIntent.Collection), cancellationToken).ConfigureAwait(false);
        foreach (BaseRecord<GatewayAdministrativeOperationIntent> intent in intents)
        {
            if (observedIntents.Contains(intent.Id.Value)) continue;
            BaseRecord<GatewayAdministrativeExecutionState> execution = (await Session(intent.Value.NamespaceId)
                .Collection(GatewayAdministrativeExecutionState.Collection)
                .GetAsync(ExecutionId(intent.Value.NamespaceId, intent.Id.Value), cancellationToken)
                .ConfigureAwait(false)).RequireValue();
            var prepared = new PreparedIntent(intent.Id.Value, intent.Value.NamespaceId, intent, execution, false, null);
            GatewayAdministrativeResult recovered;
            if (intent.Value.Operation == GatewayAdministrativeOperationKind.Purge)
                recovered = await ResolveOrRecoverPurge(prepared, cancellationToken).ConfigureAwait(false);
            else if (intent.Value.Operation == GatewayAdministrativeOperationKind.Backup)
                recovered = await ResolveOrRecoverBackup(prepared, cancellationToken).ConfigureAwait(false);
            else continue;
            if (recovered.State == GatewayAdministrativeCompletionState.Completed) completed++;
        }
        return completed;
        }
        finally { _administration.Release(); }
    }

    private async ValueTask<PreparedIntent> PrepareIntent(
        string namespaceId, string key, GatewayManagementActor actor,
        GatewayAdministrativeOperationKind operation, string subjectDigest,
        long? expectedGeneration, string? purgeCollectionId, string[]? purgeRecordIds,
        string? backupSinkName, string? backupArtifactLabel,
        CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.CommandFact(
            "admin-intent", namespaceId, operation.ToString().ToLowerInvariant(), key, "v1");
        var value = new GatewayAdministrativeOperationIntent
        {
            NamespaceId = namespaceId,
            Operation = operation,
            ActorId = actor.ActorId,
            AuthenticationScheme = actor.AuthenticationScheme,
            AuthorizationPolicy = actor.AuthorizationPolicy,
            SubjectDigest = subjectDigest,
            BackupSinkName = backupSinkName,
            BackupArtifactLabel = backupArtifactLabel,
            ExpectedGeneration = expectedGeneration,
            PurgeCollectionId = purgeCollectionId,
            PurgeRecordIdsJson = purgeRecordIds is null ? null : BaseBinary.From(JsonSerializer.SerializeToUtf8Bytes(
                purgeRecordIds, GatewayManagementJsonContext.Default.StringArray)),
        };
        RecordId executionId = ExecutionId(namespaceId, id.Value);
        var execution = new GatewayAdministrativeExecutionState
        {
            IntentId = id.Value,
            Phase = GatewayAdministrativeExecutionPhase.Unclaimed,
            StateRevision = 0,
        };
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            "gateway.management.admin-intent.v2", namespaceId, operation.ToString(), actor.ActorId,
            actor.AuthenticationScheme, actor.AuthorizationPolicy, subjectDigest,
            expectedGeneration?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            purgeCollectionId ?? string.Empty,
            Convert.ToBase64String(value.PurgeRecordIdsJson?.ToArray() ?? []))));
        BaseSession session = Session(namespaceId);
        BaseBatchBuilder batch = session.Atomic(BaseMutationRequestIdentity.Create(
            $"gateway-administration:{namespaceId}", "gateway.create-administrative-intent",
            id.Value, BaseMutationRequestFingerprint.Create(fingerprint)));
        batch.Create(GatewayAdministrativeOperationIntent.Collection, id, value);
        batch.Create(GatewayAdministrativeExecutionState.Collection, executionId, execution);
        BaseResult<BaseBatchResult> committed = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (committed is BaseFailure<BaseBatchResult> failure)
        {
            if (failure.Status == OperationStatus.Conflict)
                throw new InvalidOperationException("The administrative idempotency key was reused with different semantics.");
            return new(id.Value, namespaceId, null, null, false,
                new(id.Value, GatewayAdministrativeCompletionState.IndeterminatePending, failure.Error.Code));
        }
        BaseBatchResult batchResult = ((BaseSuccess<BaseBatchResult>)committed).Value;
        if (batchResult.Outcome != BaseRecordBatchOutcome.Committed)
        {
            if (batchResult.Error?.Category == ErrorCategory.Conflict)
                throw new InvalidOperationException("The administrative idempotency key was reused with different semantics.");
            return new(id.Value, namespaceId, null, null, false,
                new(id.Value, GatewayAdministrativeCompletionState.IndeterminatePending,
                    batchResult.Error?.Code ?? "management.admin.intent-rolled-back"));
        }
        BaseRecord<GatewayAdministrativeOperationIntent> ensured = (await session
            .Collection(GatewayAdministrativeOperationIntent.Collection).GetAsync(id, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayAdministrativeExecutionState> ensuredExecution = (await session
            .Collection(GatewayAdministrativeExecutionState.Collection).GetAsync(executionId, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        if (!StringComparer.Ordinal.Equals(ensured.Value.NamespaceId, namespaceId) ||
            ensured.Value.Operation != operation ||
            !StringComparer.Ordinal.Equals(ensured.Value.ActorId, actor.ActorId) ||
            !StringComparer.Ordinal.Equals(ensured.Value.AuthenticationScheme, actor.AuthenticationScheme) ||
            !StringComparer.Ordinal.Equals(ensured.Value.AuthorizationPolicy, actor.AuthorizationPolicy) ||
            !StringComparer.Ordinal.Equals(ensured.Value.SubjectDigest, subjectDigest) ||
            !StringComparer.Ordinal.Equals(ensured.Value.BackupSinkName, backupSinkName) ||
            !StringComparer.Ordinal.Equals(ensured.Value.BackupArtifactLabel, backupArtifactLabel) ||
            ensured.Value.ExpectedGeneration != expectedGeneration ||
            !StringComparer.Ordinal.Equals(ensured.Value.PurgeCollectionId, purgeCollectionId) ||
            !OptionalBytesEqual(ensured.Value.PurgeRecordIdsJson?.ToArray(),
                purgeRecordIds is null ? null : JsonSerializer.SerializeToUtf8Bytes(
                    purgeRecordIds, GatewayManagementJsonContext.Default.StringArray)))
            throw new InvalidOperationException("The administrative idempotency key was reused with different semantics.");
        ValidateExecution(ensuredExecution, id.Value);
        return new(id.Value, namespaceId, ensured, ensuredExecution,
            batchResult.RequestDisposition == BaseMutationRequestDisposition.Committed, null);
    }

    private async ValueTask<GatewayAdministrativeResult> ResolveExisting(
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayAdministrativeOperationObservation>[] observations = await All(
            Session(prepared.NamespaceId).Collection(GatewayAdministrativeOperationObservation.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAdministrativeOperationObservation>? observation = observations
            .FirstOrDefault(value => StringComparer.Ordinal.Equals(value.Value.IntentId, prepared.Id));
        if (observation is null)
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.admin.execution-indeterminate");
        GatewayAdministrativeResult result = await PersistCompletion(
            prepared.NamespaceId, prepared.Id,
            observation.Id.Value, observation.Value.Kind, observation.Value.ResultCode,
            observation.Value.ProviderGeneration, cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<GatewayAdministrativeArtifactObservation>> artifact = await Session(prepared.NamespaceId)
            .Collection(GatewayAdministrativeArtifactObservation.Collection)
            .GetAsync(GatewayAuthorityRecordIds.AdministrativeArtifact(prepared.NamespaceId, prepared.Id), cancellationToken)
            .ConfigureAwait(false);
        return artifact.TryGetValue(out BaseRecord<GatewayAdministrativeArtifactObservation>? record)
            ? result with { ArtifactReference = record!.Value.PublicReference }
            : result;
    }

    private async ValueTask EnsureArtifact(
        PreparedIntent prepared,
        string sinkName,
        string publicReference,
        CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.AdministrativeArtifact(prepared.NamespaceId, prepared.Id);
        BaseRecord<GatewayAdministrativeExecutionState> execution = (await Session(prepared.NamespaceId)
            .Collection(GatewayAdministrativeExecutionState.Collection)
            .GetAsync(GatewayAuthorityRecordIds.AdministrativeExecution(prepared.NamespaceId, prepared.Id), cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        ValidateExecution(execution, prepared.Id);
        DateTimeOffset observedAt = execution.Value.BoundaryCrossedAt
            ?? throw new InvalidOperationException("A backup artifact cannot precede the provider boundary.");
        var value = new GatewayAdministrativeArtifactObservation
        {
            IntentId = prepared.Id,
            SinkName = sinkName,
            PublicReference = publicReference,
            ObservedAt = observedAt,
        };
        BaseEnsureResult<GatewayAdministrativeArtifactObservation> ensured = (await Session(prepared.NamespaceId)
            .Collection(GatewayAdministrativeArtifactObservation.Collection)
            .EnsureAsync(id, value, cancellationToken).ConfigureAwait(false)).RequireValue();
        if (!StringComparer.Ordinal.Equals(ensured.Record.Value.IntentId, prepared.Id) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.SinkName, sinkName) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.PublicReference, publicReference))
            throw new InvalidOperationException("The backup sink resolved a different logical artifact.");
    }

    private async ValueTask<GatewayAdministrativeResult> ResolveOrRecoverBackup(
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayAdministrativeOperationObservation>[] observations = await All(
            Session(prepared.NamespaceId).Collection(GatewayAdministrativeOperationObservation.Collection), cancellationToken)
            .ConfigureAwait(false);
        if (observations.Any(value => StringComparer.Ordinal.Equals(value.Value.IntentId, prepared.Id)))
            return await ResolveExisting(prepared, cancellationToken).ConfigureAwait(false);

        BaseRecord<GatewayAdministrativeExecutionState> execution = prepared.Execution
            ?? throw new InvalidOperationException("The durable backup execution state is missing.");
        ValidateExecution(execution, prepared.Id);
        if (execution.Value.Phase == GatewayAdministrativeExecutionPhase.ClaimedPreBoundary &&
            execution.Value.LeaseExpiresAt > timeProvider.GetUtcNow())
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.admin.intent-recorded");
        string sinkName = prepared.Record!.Value.BackupSinkName!;
        if (!backupSinks.TryGet(sinkName, out IGatewayBackupSink? sink))
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.backup.sink-unavailable");
        bool safeToExecuteProvider = execution.Value.Phase is GatewayAdministrativeExecutionPhase.Unclaimed or GatewayAdministrativeExecutionPhase.ClaimedPreBoundary;
        if (safeToExecuteProvider)
        {
            BaseRecord<GatewayAdministrativeExecutionState> claimed = await ClaimPreBoundary(
                prepared with { Execution = execution }, cancellationToken).ConfigureAwait(false);
            execution = await CrossBoundary(prepared, claimed, cancellationToken).ConfigureAwait(false);
        }
        if (execution.Value.Phase != GatewayAdministrativeExecutionPhase.BoundaryCrossed)
            throw new InvalidOperationException("The unobserved backup execution state is invalid.");

        return await ExecuteBackup(prepared, sink!, safeToExecuteProvider, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<BaseRecord<GatewayAdministrativeExecutionState>> ClaimPreBoundary(
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayAdministrativeExecutionState> current = prepared.Execution
            ?? throw new InvalidOperationException("The administrative execution state is missing.");
        ValidateExecution(current, prepared.Id);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (current.Value.Phase == GatewayAdministrativeExecutionPhase.ClaimedPreBoundary &&
            current.Value.LeaseExpiresAt > now)
            throw new InvalidOperationException("The administrative operation already has a live pre-boundary claim.");
        if (current.Value.Phase != GatewayAdministrativeExecutionPhase.Unclaimed &&
            current.Value.Phase != GatewayAdministrativeExecutionPhase.ClaimedPreBoundary)
            throw new InvalidOperationException("The administrative operation is not claimable before its boundary.");
        string claimId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        GatewayAdministrativeExecutionState next = current.Value with
        {
            Phase = GatewayAdministrativeExecutionPhase.ClaimedPreBoundary,
            StateRevision = checked(current.Value.StateRevision + 1),
            ClaimId = claimId,
            LeaseExpiresAt = now + options.AdministrativeClaimLease,
            BoundaryCrossedAt = null,
            ObservationId = null,
        };
        return (await Session(prepared.NamespaceId).Collection(GatewayAdministrativeExecutionState.Collection)
            .ReplaceAsync(current.Id, next, current.Revision, cancellationToken).ConfigureAwait(false)).RequireValue();
    }

    private async ValueTask<BaseRecord<GatewayAdministrativeExecutionState>> CrossBoundary(
        PreparedIntent prepared,
        BaseRecord<GatewayAdministrativeExecutionState> claimed,
        CancellationToken cancellationToken)
    {
        ValidateExecution(claimed, prepared.Id);
        if (claimed.Value.Phase != GatewayAdministrativeExecutionPhase.ClaimedPreBoundary ||
            claimed.Value.ClaimId is null || claimed.Value.LeaseExpiresAt is null)
            throw new InvalidOperationException("The administrative pre-boundary claim is invalid.");
        GatewayAdministrativeExecutionState next = claimed.Value with
        {
            Phase = GatewayAdministrativeExecutionPhase.BoundaryCrossed,
            StateRevision = checked(claimed.Value.StateRevision + 1),
            BoundaryCrossedAt = timeProvider.GetUtcNow(),
            LeaseExpiresAt = null,
        };
        return (await Session(prepared.NamespaceId).Collection(GatewayAdministrativeExecutionState.Collection)
            .ReplaceAsync(claimed.Id, next, claimed.Revision, cancellationToken).ConfigureAwait(false)).RequireValue();
    }

    private async ValueTask<GatewayAdministrativeResult> ResolveOrRecoverPurge(
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayAdministrativeOperationObservation>[] observations = await All(
            Session(prepared.NamespaceId).Collection(GatewayAdministrativeOperationObservation.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAdministrativeOperationObservation>? observation = observations
            .FirstOrDefault(value => StringComparer.Ordinal.Equals(value.Value.IntentId, prepared.Id));
        if (observation is not null)
        {
            await FinalizePurgeFence(
                prepared.Record!.Value.PurgeCollectionId!, prepared.Id,
                observation.Value.Kind == GatewayAdministrativeObservationKind.Succeeded
                    ? observation.Value.ProviderGeneration
                    : null,
                cancellationToken).ConfigureAwait(false);
            return await PersistCompletion(
                prepared.NamespaceId, prepared.Id, observation.Id.Value,
                observation.Value.Kind, observation.Value.ResultCode,
                observation.Value.ProviderGeneration, cancellationToken).ConfigureAwait(false);
        }

        string collectionId = prepared.Record!.Value.PurgeCollectionId
            ?? throw new InvalidOperationException("The durable purge intent is incomplete.");
        BaseRecord<GatewayPurgeAuthorityState>? existingFence = await GetPurgeFence(collectionId, cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayPurgeAuthorityState> fence = existingFence?.Value.PendingIntentId is null
            ? await ClaimPurgeFence(collectionId, prepared.Id, prepared.Record!.Value.ExpectedGeneration, cancellationToken).ConfigureAwait(false)
            : existingFence;
        if (!StringComparer.Ordinal.Equals(fence.Value.PendingIntentId, prepared.Id))
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.admin.purge-fence-conflict");
        BaseRecord<GatewayAdministrativeExecutionState> execution = prepared.Execution
            ?? throw new InvalidOperationException("The durable purge execution state is missing.");
        ValidateExecution(execution, prepared.Id);
        if (execution.Value.Phase == GatewayAdministrativeExecutionPhase.ClaimedPreBoundary &&
            execution.Value.LeaseExpiresAt > timeProvider.GetUtcNow())
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.admin.intent-recorded");
        if (execution.Value.Phase is GatewayAdministrativeExecutionPhase.Unclaimed or GatewayAdministrativeExecutionPhase.ClaimedPreBoundary)
        {
            BaseRecord<GatewayAdministrativeExecutionState> claimed = await ClaimPreBoundary(
                prepared with { Execution = execution }, cancellationToken).ConfigureAwait(false);
            execution = await CrossBoundary(prepared, claimed, cancellationToken).ConfigureAwait(false);
        }
        if (execution.Value.Phase != GatewayAdministrativeExecutionPhase.BoundaryCrossed)
            throw new InvalidOperationException("The unobserved purge execution state is invalid.");
        return await ExecutePurge(prepared, fence, cancellationToken, recovering: true).ConfigureAwait(false);
    }

    private async ValueTask<BaseRecord<GatewayPurgeAuthorityState>> ClaimPurgeFence(
        string collectionId,
        string intentId,
        long? callerExpectedGeneration,
        CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.PurgeAuthority(options.ManagementAuthorityId, collectionId);
        BaseCollectionSession<GatewayPurgeAuthorityState> collection = Session(null)
            .Collection(GatewayPurgeAuthorityState.Collection);
        BaseEnsureResult<GatewayPurgeAuthorityState> ensured = (await collection.EnsureAsync(id, new GatewayPurgeAuthorityState
        {
            ManagementAuthorityId = options.ManagementAuthorityId,
            CollectionId = collectionId,
            ConfirmedGeneration = 0,
        }, cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayPurgeAuthorityState> current = ensured.Record;
        if (callerExpectedGeneration is { } expected && expected != current.Value.ConfirmedGeneration)
            throw new InvalidOperationException("The requested purge generation does not match Gateway's confirmed provider generation.");
        if (current.Value.PendingIntentId is { } pending)
        {
            if (!StringComparer.Ordinal.Equals(pending, intentId))
                throw new InvalidOperationException("Another purge remains unresolved for this collection.");
            return current;
        }
        return (await collection.ReplaceAsync(id, current.Value with
        {
            PendingIntentId = intentId,
        }, current.Revision, cancellationToken).ConfigureAwait(false)).RequireValue();
    }

    private async ValueTask ValidatePurgeGeneration(
        string collectionId,
        long? callerExpectedGeneration,
        CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.PurgeAuthority(options.ManagementAuthorityId, collectionId);
        BaseEnsureResult<GatewayPurgeAuthorityState> ensured = (await Session(null)
            .Collection(GatewayPurgeAuthorityState.Collection).EnsureAsync(id, new GatewayPurgeAuthorityState
            {
                ManagementAuthorityId = options.ManagementAuthorityId,
                CollectionId = collectionId,
                ConfirmedGeneration = 0,
            }, cancellationToken).ConfigureAwait(false)).RequireValue();
        if (ensured.Record.Value.PendingIntentId is not null)
            throw new InvalidOperationException("Another purge remains unresolved for this collection.");
        if (callerExpectedGeneration is { } expected && expected != ensured.Record.Value.ConfirmedGeneration)
            throw new InvalidOperationException("The requested purge generation does not match Gateway's confirmed provider generation.");
    }

    private async ValueTask<BaseRecord<GatewayPurgeAuthorityState>?> GetPurgeFence(
        string collectionId,
        CancellationToken cancellationToken)
    {
        RecordId id = GatewayAuthorityRecordIds.PurgeAuthority(options.ManagementAuthorityId, collectionId);
        BaseResult<BaseRecord<GatewayPurgeAuthorityState>> result = await Session(null)
            .Collection(GatewayPurgeAuthorityState.Collection).GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out BaseRecord<GatewayPurgeAuthorityState>? value) ? value : null;
    }

    private async ValueTask<GatewayAdministrativeResult> ExecutePurge(
        PreparedIntent prepared,
        BaseRecord<GatewayPurgeAuthorityState> fence,
        CancellationToken cancellationToken,
        bool recovering = false)
    {
        GatewayAdministrativeOperationIntent intent = prepared.Record!.Value;
        string[] ids = JsonSerializer.Deserialize(
            intent.PurgeRecordIdsJson!.ToArray(), GatewayManagementJsonContext.Default.StringArray)
            ?? throw new InvalidOperationException("The durable purge record set is invalid.");
        BaseResult<BasePurgeResult> executed = await administration.PurgeAsync(new BasePurgeRequest
        {
            CollectionId = intent.PurgeCollectionId!,
            RecordIds = ids.Select(RecordId.Create).ToArray(),
            Principal = Principal(),
            ReasonCode = "gateway.retention",
            AuditReference = prepared.Id,
            EvaluatedAt = timeProvider.GetUtcNow(),
            ExpectedPurgeGeneration = fence.Value.ConfirmedGeneration,
        }, cancellationToken).ConfigureAwait(false);

        if (executed is BaseFailure<BasePurgeResult> failure &&
            StringComparer.Ordinal.Equals(failure.Error.Code, BaseCollectionErrorCodes.PurgeGenerationConflict))
        {
            if (!recovering)
                return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                    "management.admin.purge-generation-indeterminate");
            long recoveredGeneration = checked(fence.Value.ConfirmedGeneration + 1);
            GatewayAdministrativeResult recovered = await PersistObservation(
                prepared.NamespaceId, prepared.Id, OperationStatus.Ok,
                "base.admin.purge.succeeded-recovered", recoveredGeneration, cancellationToken).ConfigureAwait(false);
            if (await HasObservation(prepared.Id, cancellationToken).ConfigureAwait(false))
                await FinalizePurgeFence(intent.PurgeCollectionId!, prepared.Id, recoveredGeneration, cancellationToken).ConfigureAwait(false);
            return recovered;
        }

        if (executed is BaseFailure<BasePurgeResult> failed &&
            failed.Error.Code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase))
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending, failed.Error.Code);

        long? generation = executed is BaseSuccess<BasePurgeResult> success ? success.Value.PurgeGeneration : null;
        GatewayAdministrativeResult result = await PersistObservation(
            prepared.NamespaceId, prepared.Id, executed.Status,
            executed is BaseFailure<BasePurgeResult> error ? error.Error.Code : "base.admin.purge.succeeded",
            generation, cancellationToken).ConfigureAwait(false);
        if (await HasObservation(prepared.Id, cancellationToken).ConfigureAwait(false))
            await FinalizePurgeFence(intent.PurgeCollectionId!, prepared.Id, generation, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<bool> HasObservation(string intentId, CancellationToken cancellationToken) =>
        (await All(Session(null).Collection(GatewayAdministrativeOperationObservation.Collection), cancellationToken)
            .ConfigureAwait(false)).Any(value => StringComparer.Ordinal.Equals(value.Value.IntentId, intentId));

    private async ValueTask FinalizePurgeFence(
        string collectionId,
        string intentId,
        long? confirmedGeneration,
        CancellationToken cancellationToken)
    {
        BaseRecord<GatewayPurgeAuthorityState>? current = await GetPurgeFence(collectionId, cancellationToken).ConfigureAwait(false);
        if (current is null || !StringComparer.Ordinal.Equals(current.Value.PendingIntentId, intentId)) return;
        await Session(null).Collection(GatewayPurgeAuthorityState.Collection)
            .ReplaceAsync(current.Id, current.Value with
            {
                ConfirmedGeneration = confirmedGeneration ?? current.Value.ConfirmedGeneration,
                PendingIntentId = null,
            }, current.Revision, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GatewayAdministrativeResult> RecordProviderResult(
        string namespaceId, string intentId, OperationStatus status,
        string code, long? generation, CancellationToken cancellationToken)
    {
        GatewayAdministrativeResult result = await PersistObservation(
            namespaceId, intentId, status, code, generation, cancellationToken).ConfigureAwait(false);
        if (result.State == GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending)
            _pending[intentId] = new(namespaceId, intentId, status, code, generation);
        return result;
    }

    private async ValueTask<GatewayAdministrativeResult> PersistObservation(
        string namespaceId, string intentId, OperationStatus status,
        string code, long? generation, CancellationToken cancellationToken)
    {
        GatewayAdministrativeObservationKind kind = status == OperationStatus.Ok
            ? GatewayAdministrativeObservationKind.Succeeded
            : code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase)
                ? GatewayAdministrativeObservationKind.Indeterminate
                : GatewayAdministrativeObservationKind.Failed;
        RecordId observationId = GatewayAuthorityRecordIds.CommandFact(
            "admin-observation", namespaceId, "observe", intentId, kind.ToString(), code, "v1");
        var observation = new GatewayAdministrativeOperationObservation
        {
            IntentId = intentId,
            Kind = kind,
            ResultCode = code,
            ProviderGeneration = generation,
            ResultJson = BaseBinary.From(Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}")),
        };
        BaseSession session = Session(namespaceId);
        RecordId executionId = ExecutionId(namespaceId, intentId);
        BaseRecord<GatewayAdministrativeExecutionState> execution = (await session
            .Collection(GatewayAdministrativeExecutionState.Collection).GetAsync(executionId, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        ValidateExecution(execution, intentId);
        if (execution.Value.Phase == GatewayAdministrativeExecutionPhase.Observed)
        {
            if (!StringComparer.Ordinal.Equals(execution.Value.ObservationId, observationId.Value))
                throw new InvalidOperationException("The administrative execution references another observation.");
            return await PersistCompletion(namespaceId, intentId, observationId.Value, kind, code, generation, cancellationToken)
                .ConfigureAwait(false);
        }
        if (execution.Value.Phase != GatewayAdministrativeExecutionPhase.BoundaryCrossed)
            throw new InvalidOperationException("Provider observation cannot be published before the administrative boundary.");
        GatewayAdministrativeExecutionState observedExecution = execution.Value with
        {
            Phase = GatewayAdministrativeExecutionPhase.Observed,
            StateRevision = checked(execution.Value.StateRevision + 1),
            ObservationId = observationId.Value,
            LeaseExpiresAt = null,
        };
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            "gateway.management.admin-observation.v2", intentId, kind.ToString(), code,
            generation?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            execution.Value.StateRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        BaseBatchBuilder BuildBatch()
        {
            BaseBatchBuilder value = session.Atomic(BaseMutationRequestIdentity.Create(
                $"gateway-administration:{namespaceId}", "gateway.publish-administrative-observation",
                observationId.Value, BaseMutationRequestFingerprint.Create(fingerprint)));
            value.Create(GatewayAdministrativeOperationObservation.Collection, observationId, observation);
            value.Replace(GatewayAdministrativeExecutionState.Collection, execution.Id, observedExecution, execution.Revision);
            return value;
        }

        GatewayAdministrativeObservationCommitResolution committed = await
            GatewayAdministrativeObservationReceiptResolver.ResolveAsync(async token =>
            {
                BaseResult<BaseBatchResult> result = await BuildBatch().CommitAsync(token).ConfigureAwait(false);
                return result is BaseFailure<BaseBatchResult> failure
                    ? new(null, failure.Error.Code)
                    : new(((BaseSuccess<BaseBatchResult>)result).Value.Outcome, null);
            }, cancellationToken).ConfigureAwait(false);
        if (committed.FailureCode is { } failureCode)
            return new(intentId, GatewayAdministrativeCompletionState.IndeterminatePending,
                failureCode, generation);
        if (committed.Outcome != BaseRecordBatchOutcome.Committed)
            return new(intentId, GatewayAdministrativeCompletionState.IndeterminatePending,
                committed.Outcome == BaseRecordBatchOutcome.RolledBack
                    ? "management.admin.observation-rolled-back"
                    : "management.admin.observation-indeterminate",
                generation);

        return await PersistCompletion(
            namespaceId, intentId, observationId.Value, kind, code, generation, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GatewayAdministrativeResult> PersistCompletion(
        string namespaceId, string intentId, string observationId,
        GatewayAdministrativeObservationKind kind, string code, long? generation,
        CancellationToken cancellationToken)
    {
        GatewayAdministrativeCompletionState state = kind switch
        {
            GatewayAdministrativeObservationKind.Succeeded => GatewayAdministrativeCompletionState.Completed,
            GatewayAdministrativeObservationKind.Failed => GatewayAdministrativeCompletionState.Failed,
            _ => GatewayAdministrativeCompletionState.IndeterminatePending,
        };
        RecordId completionId = GatewayAuthorityRecordIds.CommandFact(
            "admin-completion", namespaceId, "complete", intentId, observationId, "v1");
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationCompletion>> completed = await Session(namespaceId)
            .Collection(GatewayAdministrativeOperationCompletion.Collection)
            .EnsureAsync(completionId, new GatewayAdministrativeOperationCompletion
            {
                IntentId = intentId,
                ObservationId = observationId,
                State = state,
            }, cancellationToken).ConfigureAwait(false);
        return completed.TryGetValue(out _)
            ? new(intentId, state, code, generation)
            : new(intentId, GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending,
                "management.admin.completion-pending", generation);
    }

    private async ValueTask EnforceRetentionClosure(
        string collectionId,
        RecordId[] requested,
        CancellationToken cancellationToken)
    {
        BaseSession session = Session(null);
        BaseRecord<GatewayDesiredState>[] desired = await All(session.Collection(GatewayDesiredState.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAcceptedRevision>[] revisions = await All(session.Collection(GatewayAcceptedRevision.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayActivationIntent>[] intents = await All(session.Collection(GatewayActivationIntent.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayDeliveryOutboxItem>[] outbox = await All(session.Collection(GatewayDeliveryOutboxItem.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayNodeActivationOutcome>[] outcomes = await All(session.Collection(GatewayNodeActivationOutcome.Collection), cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayAdministrativeAuditRecord>[] audits = await All(session.Collection(GatewayAdministrativeAuditRecord.Collection), cancellationToken).ConfigureAwait(false);

        var protectedRevisions = desired.Select(static value => value.Value.RevisionId).ToHashSet(StringComparer.Ordinal);
        var protectedIntents = desired.Select(static value => value.Value.ActivationIntentId).ToHashSet(StringComparer.Ordinal);
        var protectedOutbox = outbox.Where(static value => value.Value.State is not (GatewayDeliveryState.Completed or GatewayDeliveryState.TerminalFailure))
            .Select(static value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        foreach (BaseRecord<GatewayDeliveryOutboxItem> item in outbox.Where(value => protectedOutbox.Contains(value.Id.Value)))
            protectedIntents.Add(item.Value.ActivationIntentId);

        bool changed;
        var protectedValidations = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            changed = false;
            foreach (BaseRecord<GatewayActivationIntent> intent in intents.Where(value => protectedIntents.Contains(value.Id.Value)))
                changed |= protectedRevisions.Add(intent.Value.RevisionId);
            foreach (BaseRecord<GatewayAcceptedRevision> revision in revisions.Where(value => protectedRevisions.Contains(value.Id.Value)))
            {
                changed |= protectedValidations.Add(revision.Value.ValidationId);
                if (revision.Value.ParentRevisionId is { } parent) changed |= protectedRevisions.Add(parent);
                if (revision.Value.DerivedFromRevisionId is { } derived) changed |= protectedRevisions.Add(derived);
            }
        } while (changed);

        var protectedOutcomes = outcomes.Where(value => protectedIntents.Contains(value.Value.ActivationIntentId))
            .Select(static value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        var protectedAudits = audits.Where(value => protectedRevisions.Contains(value.Value.SubjectId) || protectedIntents.Contains(value.Value.SubjectId))
            .Select(static value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        HashSet<string> protectedForCollection = collectionId switch
        {
            GatewayAuthoritySchema.AcceptedRevisions => protectedRevisions,
            GatewayAuthoritySchema.ValidationRecords => protectedValidations,
            GatewayAuthoritySchema.AdministrativeAudit => protectedAudits,
            GatewayAuthoritySchema.NodeOutcomes => protectedOutcomes,
            _ => throw new ArgumentException("The collection is not Gateway purge-enabled.", nameof(collectionId)),
        };
        if (requested.Any(id => protectedForCollection.Contains(id.Value)))
            throw new InvalidOperationException("The purge set intersects the retained Gateway reference closure.");
    }

    private static async ValueTask<BaseRecord<T>[]> All<T>(
        BaseCollectionSession<T> collection,
        CancellationToken cancellationToken)
    {
        const int maximum = 4_096;
        const int pageSize = 256;
        var records = new List<BaseRecord<T>>(maximum);
        string? continuation = null;
        while (true)
        {
            BaseQuery<T> query = collection.Query().Take(Math.Min(pageSize, maximum - records.Count));
            if (continuation is not null)
                query = query.ContinueFrom(continuation);
            BasePage<BaseRecord<T>> page = (await query.PageAsync(cancellationToken)
                .ConfigureAwait(false)).RequireValue();
            records.AddRange(page.Items);
            if (!page.Page.HasMore)
                return records.ToArray();
            if (records.Count >= maximum)
                throw new InvalidOperationException("The bounded administration reconciliation graph was exceeded.");
            continuation = page.Page.NextCursor ?? throw new InvalidOperationException(
                "Gateway administration pagination omitted its continuation token.");
        }
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

    private static bool BytesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    // HPD.Base projects a nullable binary field without a value as an empty array. For the
    // optional purge selection those representations have the same closed semantic meaning.
    private static bool OptionalBytesEqual(byte[]? left, byte[]? right) =>
        (left is null or { Length: 0 }) && (right is null or { Length: 0 }) || BytesEqual(left, right);

    private static bool Fatal(Exception exception) => exception is
        OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static bool ValidSinkName(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');

    private static bool ValidArtifactLabel(string value) => value.Length <= 128 &&
        value.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');

    private static bool ValidPublicReference(string? value) => value is { Length: > 0 and <= 256 } &&
        value.All(static character => character is >= '!' and <= '~');

    private static RecordId ExecutionId(string namespaceId, string intentId) =>
        GatewayAuthorityRecordIds.AdministrativeExecution(namespaceId, intentId);

    private static void ValidateExecution(
        BaseRecord<GatewayAdministrativeExecutionState> execution,
        string intentId)
    {
        GatewayAdministrativeExecutionState value = execution.Value;
        if (!StringComparer.Ordinal.Equals(value.IntentId, intentId) ||
            !Enum.IsDefined(value.Phase) || value.StateRevision < 0)
            throw new InvalidOperationException("The administrative execution state is invalid.");
        bool valid = value.Phase switch
        {
            GatewayAdministrativeExecutionPhase.Unclaimed => value is
                { StateRevision: 0, ClaimId: null, LeaseExpiresAt: null, BoundaryCrossedAt: null, ObservationId: null },
            GatewayAdministrativeExecutionPhase.ClaimedPreBoundary => value.ClaimId is not null &&
                value.LeaseExpiresAt is not null && value.BoundaryCrossedAt is null && value.ObservationId is null,
            GatewayAdministrativeExecutionPhase.BoundaryCrossed => value.ClaimId is not null &&
                value.BoundaryCrossedAt is not null && value.ObservationId is null,
            GatewayAdministrativeExecutionPhase.Observed => value.ClaimId is not null &&
                value.BoundaryCrossedAt is not null && value.LeaseExpiresAt is null && value.ObservationId is not null,
            _ => false,
        };
        if (!valid)
            throw new InvalidOperationException("The administrative execution state contains an impossible field combination.");
    }

    private sealed record PreparedIntent(
        string Id,
        string NamespaceId,
        BaseRecord<GatewayAdministrativeOperationIntent>? Record,
        BaseRecord<GatewayAdministrativeExecutionState>? Execution,
        bool Created,
        GatewayAdministrativeResult? Failure);
    private sealed record PendingObservation(
        string NamespaceId, string IntentId,
        OperationStatus Status, string Code, long? Generation);
}
