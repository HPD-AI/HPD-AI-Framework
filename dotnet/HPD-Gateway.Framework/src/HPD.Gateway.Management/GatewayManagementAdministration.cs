using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    ValueTask<int> ReconcilePendingAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementAdministration(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    IHPDBaseAdministration administration,
    GatewayManagementOptions options,
    TimeProvider timeProvider) : IGatewayManagementAdministration
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingObservation> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _administration = new(1, 1);
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
        PreparedIntent prepared = await PrepareIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Backup,
            $"store:{capabilities.StoreId}", null, null, null, cancellationToken).ConfigureAwait(false);
        if (!prepared.Created)
            return await ResolveExisting(prepared, cancellationToken).ConfigureAwait(false);
        BaseResult<BaseBackupManifest> executed = await administration.CreateBackupAsync(destination, new BaseBackupRequest
        {
            StoreId = capabilities.StoreId,
            Principal = Principal(),
        }, cancellationToken).ConfigureAwait(false);
        return await RecordProviderResult(
            namespaceId, prepared.Id, executed.Status,
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
            recordIds.Order(StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!prepared.Created)
            return await ResolveOrRecoverPurge(prepared, cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayPurgeAuthorityState> fence = await ClaimPurgeFence(
            collectionId, prepared.Id, expectedGeneration, cancellationToken).ConfigureAwait(false);
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
            if (intent.Value.Operation != GatewayAdministrativeOperationKind.Purge ||
                observedIntents.Contains(intent.Id.Value)) continue;
            GatewayAdministrativeResult recovered = await ResolveOrRecoverPurge(
                new PreparedIntent(intent.Id.Value, intent.Value.NamespaceId, intent, false),
                cancellationToken).ConfigureAwait(false);
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
            ExpectedGeneration = expectedGeneration,
            PurgeCollectionId = purgeCollectionId,
            PurgeRecordIdsJson = purgeRecordIds is null ? null : JsonSerializer.SerializeToUtf8Bytes(
                purgeRecordIds, GatewayManagementJsonContext.Default.StringArray),
        };
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationIntent>> result = await Session(namespaceId)
            .Collection(GatewayAdministrativeOperationIntent.Collection)
            .EnsureAsync(id, value, cancellationToken).ConfigureAwait(false);
        BaseEnsureResult<GatewayAdministrativeOperationIntent> ensured = result.RequireValue();
        if (!StringComparer.Ordinal.Equals(ensured.Record.Value.NamespaceId, namespaceId) ||
            ensured.Record.Value.Operation != operation ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.ActorId, actor.ActorId) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.AuthenticationScheme, actor.AuthenticationScheme) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.AuthorizationPolicy, actor.AuthorizationPolicy) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.SubjectDigest, subjectDigest) ||
            ensured.Record.Value.ExpectedGeneration != expectedGeneration ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.PurgeCollectionId, purgeCollectionId) ||
            !BytesEqual(ensured.Record.Value.PurgeRecordIdsJson,
                purgeRecordIds is null ? null : JsonSerializer.SerializeToUtf8Bytes(
                    purgeRecordIds, GatewayManagementJsonContext.Default.StringArray)))
            throw new InvalidOperationException("The administrative idempotency key was reused with different semantics.");
        return new(id.Value, namespaceId, ensured.Record, ensured.Outcome == BaseEnsureOutcome.Created);
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
        return await PersistCompletion(
            prepared.NamespaceId, prepared.Id,
            observation.Id.Value, observation.Value.Kind, observation.Value.ResultCode,
            observation.Value.ProviderGeneration, cancellationToken).ConfigureAwait(false);
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
                prepared.Record.Value.PurgeCollectionId!, prepared.Id,
                observation.Value.Kind == GatewayAdministrativeObservationKind.Succeeded
                    ? observation.Value.ProviderGeneration
                    : null,
                cancellationToken).ConfigureAwait(false);
            return await PersistCompletion(
                prepared.NamespaceId, prepared.Id, observation.Id.Value,
                observation.Value.Kind, observation.Value.ResultCode,
                observation.Value.ProviderGeneration, cancellationToken).ConfigureAwait(false);
        }

        string collectionId = prepared.Record.Value.PurgeCollectionId
            ?? throw new InvalidOperationException("The durable purge intent is incomplete.");
        BaseRecord<GatewayPurgeAuthorityState>? existingFence = await GetPurgeFence(collectionId, cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayPurgeAuthorityState> fence = existingFence?.Value.PendingIntentId is null
            ? await ClaimPurgeFence(collectionId, prepared.Id, prepared.Record.Value.ExpectedGeneration, cancellationToken).ConfigureAwait(false)
            : existingFence;
        if (!StringComparer.Ordinal.Equals(fence.Value.PendingIntentId, prepared.Id))
            return new(prepared.Id, GatewayAdministrativeCompletionState.IndeterminatePending,
                "management.admin.purge-fence-conflict");
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
        GatewayAdministrativeOperationIntent intent = prepared.Record.Value;
        string[] ids = JsonSerializer.Deserialize(
            intent.PurgeRecordIdsJson!, GatewayManagementJsonContext.Default.StringArray)
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
            ResultJson = Encoding.UTF8.GetBytes($"{{\"code\":\"{code}\"}}"),
        };
        BaseSession session = Session(namespaceId);
        BaseResult<BaseEnsureResult<GatewayAdministrativeOperationObservation>> observed = await session
            .Collection(GatewayAdministrativeOperationObservation.Collection)
            .EnsureAsync(observationId, observation, cancellationToken).ConfigureAwait(false);
        if (!observed.TryGetValue(out _))
            return new(intentId, GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending,
                "management.admin.observation-pending", generation);

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
        BaseRecord<T>[] records = (await collection.Query().Take(maximum + 1)
            .ToArrayAsync(maximum + 1, cancellationToken).ConfigureAwait(false)).RequireValue();
        if (records.Length > maximum)
            throw new InvalidOperationException("The bounded administration reconciliation graph was exceeded.");
        return records;
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

    private sealed record PreparedIntent(
        string Id,
        string NamespaceId,
        BaseRecord<GatewayAdministrativeOperationIntent> Record,
        bool Created);
    private sealed record PendingObservation(
        string NamespaceId, string IntentId,
        OperationStatus Status, string Code, long? Generation);
}
