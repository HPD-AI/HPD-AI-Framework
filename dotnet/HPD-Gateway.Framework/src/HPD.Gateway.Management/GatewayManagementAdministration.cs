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
    ValueTask<int> ReconcilePendingAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementAdministration(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    IHPDBaseAdministration administration,
    TimeProvider timeProvider) : IGatewayManagementAdministration
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingObservation> _pending = new(StringComparer.Ordinal);
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
            $"store:{capabilities.StoreId}", null, cancellationToken).ConfigureAwait(false);
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
        await EnforceRetentionClosure(collectionId, ids, cancellationToken).ConfigureAwait(false);

        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            collectionId + "\n" + string.Join('\n', recordIds.Order(StringComparer.Ordinal)))));
        PreparedIntent prepared = await PrepareIntent(
            namespaceId, idempotencyKey, actor, GatewayAdministrativeOperationKind.Purge,
            digest, expectedGeneration, cancellationToken).ConfigureAwait(false);
        if (!prepared.Created)
            return await ResolveExisting(prepared, cancellationToken).ConfigureAwait(false);
        BaseResult<BasePurgeResult> executed = await administration.PurgeAsync(new BasePurgeRequest
        {
            CollectionId = collectionId,
            RecordIds = ids,
            Principal = Principal(),
            ReasonCode = "gateway.retention",
            AuditReference = prepared.Id,
            EvaluatedAt = timeProvider.GetUtcNow(),
            ExpectedPurgeGeneration = expectedGeneration,
        }, cancellationToken).ConfigureAwait(false);
        long? generation = executed is BaseSuccess<BasePurgeResult> success ? success.Value.PurgeGeneration : null;
        return await RecordProviderResult(
            namespaceId, prepared.Id, executed.Status,
            executed is BaseFailure<BasePurgeResult> failure ? failure.Error.Code : "base.admin.purge.succeeded",
            generation, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReconcilePendingAsync(CancellationToken cancellationToken = default)
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
            if (result.State != GatewayAdministrativeCompletionState.ExecutionSucceededCompletionPending) completed++;
        }
        return completed;
    }

    private async ValueTask<PreparedIntent> PrepareIntent(
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
        BaseEnsureResult<GatewayAdministrativeOperationIntent> ensured = result.RequireValue();
        if (!StringComparer.Ordinal.Equals(ensured.Record.Value.NamespaceId, namespaceId) ||
            ensured.Record.Value.Operation != operation ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.ActorId, actor.ActorId) ||
            !StringComparer.Ordinal.Equals(ensured.Record.Value.SubjectDigest, subjectDigest) ||
            ensured.Record.Value.ExpectedGeneration != expectedGeneration)
            throw new InvalidOperationException("The administrative idempotency key was reused with different semantics.");
        return new(id.Value, namespaceId, ensured.Outcome == BaseEnsureOutcome.Created);
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
        GatewayAdministrativeCompletionState state = kind == GatewayAdministrativeObservationKind.Succeeded
            ? GatewayAdministrativeCompletionState.Completed
            : GatewayAdministrativeCompletionState.IndeterminatePending;
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
        BaseRecord<GatewayCommandReceipt>[] receipts = await All(session.Collection(GatewayCommandReceipt.Collection), cancellationToken).ConfigureAwait(false);
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
        var protectedReceipts = receipts.Where(value => protectedRevisions.Contains(value.Value.StableOperationId) || protectedIntents.Contains(value.Value.StableOperationId))
            .Select(static value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        var protectedAudits = audits.Where(value => protectedRevisions.Contains(value.Value.SubjectId) || protectedIntents.Contains(value.Value.SubjectId))
            .Select(static value => value.Id.Value).ToHashSet(StringComparer.Ordinal);
        HashSet<string> protectedForCollection = collectionId switch
        {
            GatewayAuthoritySchema.AcceptedRevisions => protectedRevisions,
            GatewayAuthoritySchema.ValidationRecords => protectedValidations,
            GatewayAuthoritySchema.AdministrativeAudit => protectedAudits,
            GatewayAuthoritySchema.NodeOutcomes => protectedOutcomes,
            GatewayAuthoritySchema.CommandReceipts => protectedReceipts,
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

    private sealed record PreparedIntent(string Id, string NamespaceId, bool Created);
    private sealed record PendingObservation(
        string NamespaceId, string IntentId,
        OperationStatus Status, string Code, long? Generation);
}
