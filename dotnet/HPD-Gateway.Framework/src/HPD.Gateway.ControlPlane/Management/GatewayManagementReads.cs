using System.Collections.Immutable;
using HPD.Base;

namespace HPD.Gateway.ControlPlane;

public sealed record GatewayManagedRecord<T>(
    string Id,
    T Value,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record GatewayManagedPage<T>(
    ImmutableArray<GatewayManagedRecord<T>> Items,
    string? ContinuationToken,
    bool HasMore);

public sealed record GatewayDesiredProjection(
    string TargetNodeId,
    string NamespaceId,
    string ActivationIntentId,
    string RevisionId,
    string CandidateId,
    string DesiredStateToken,
    DateTimeOffset? ObservedAt);

public enum GatewayAdministrativeOperationReadState
{
    IntentRecorded,
    Completed,
    Failed,
    ExecutionSucceededCompletionPending,
    IndeterminatePending,
}

public sealed record GatewayAdministrativeOperationReadProjection(
    string OperationId,
    GatewayAdministrativeOperationKind Operation,
    GatewayAdministrativeOperationReadState State,
    string Code,
    string? ArtifactReference,
    DateTimeOffset? ObservedAt);

internal interface IGatewayManagementReader
{
    ValueTask<bool> OwnsTargetAsync(
        string namespaceId,
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayDesiredState>?> GetDesiredAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayDesiredProjection?> GetDesiredProjectionAsync(
        string namespaceId,
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> FindByIdempotencyAsync(
        string namespaceId,
        string targetNodeId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> GetOperationAsync(
        string namespaceId,
        string operationId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayAdministrativeOperationReadProjection?> GetAdministrativeOperationAsync(
        string namespaceId,
        string operationId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayAcceptedRevision>?> GetRevisionAsync(
        string namespaceId,
        string targetNodeId,
        string revisionId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayValidationRecord>?> GetValidationAsync(
        string namespaceId,
        string targetNodeId,
        string validationId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedPage<GatewayAcceptedRevision>> ListRevisionsAsync(
        string namespaceId,
        string targetNodeId,
        int maximum,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedPage<GatewayAdministrativeAuditRecord>> ListAuditAsync(
        string namespaceId,
        int maximum,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedPage<GatewayActivationIntent>> ListActivationsAsync(
        string namespaceId,
        string targetNodeId,
        int maximum,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedPage<GatewayNodeActivationOutcome>> ListOutcomesAsync(
        string namespaceId,
        string targetNodeId,
        int maximum,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementReader(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    GatewayManagementRuntimeOptions options) : IGatewayManagementReader
{
    public async ValueTask<bool> OwnsTargetAsync(
        string namespaceId, string targetNodeId, CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(targetNodeId, nameof(targetNodeId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<GatewayTargetOwnership>> result = await Session(namespaceId)
            .Collection(GatewayTargetOwnership.Collection)
            .GetAsync(GatewayAuthorityRecordIds.TargetOwnership(options.ManagementAuthorityId, targetNodeId), cancellationToken)
            .ConfigureAwait(false);
        return result.TryGetValue(out BaseRecord<GatewayTargetOwnership>? record) &&
            StringComparer.Ordinal.Equals(record!.Value.NamespaceId, namespaceId) &&
            StringComparer.Ordinal.Equals(record.Value.TargetNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(record.Value.ManagementAuthorityId, options.ManagementAuthorityId);
    }
    public async ValueTask<GatewayDesiredProjection?> GetDesiredProjectionAsync(
        string namespaceId, string targetNodeId, CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        GatewayManagedRecord<GatewayDesiredState>? desired = await GetDesiredAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        if (desired is null || !StringComparer.Ordinal.Equals(desired.Value.NamespaceId, namespaceId)) return null;
        return new(targetNodeId, namespaceId, desired.Value.ActivationIntentId, desired.Value.RevisionId,
            desired.Value.CandidateId, GatewayDesiredStateTokens.Create(desired.Value, options), desired.UpdatedAt ?? desired.CreatedAt);
    }
    public async ValueTask<GatewayManagedRecord<GatewayDesiredState>?> GetDesiredAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<GatewayDesiredState>> result = await Session(null)
            .Collection(GatewayDesiredState.Collection)
            .GetAsync(GatewayAuthorityRecordIds.DesiredState(options.ManagementAuthorityId, targetNodeId), cancellationToken)
            .ConfigureAwait(false);
        return result.TryGetValue(out var record) ? Project(record!) : null;
    }

    public async ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> FindByIdempotencyAsync(
        string namespaceId, string targetNodeId, string operation, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(targetNodeId, nameof(targetNodeId));
        Validate(operation, nameof(operation));
        Validate(idempotencyKey, nameof(idempotencyKey));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayCommandReceipt>[] records = (await Session(namespaceId)
            .Collection(GatewayCommandReceipt.Collection).Query()
            .Where(GatewayCommandReceipt.Fields.NamespaceId, namespaceId)
            .Where(GatewayCommandReceipt.Fields.TargetNodeId, targetNodeId)
            .Where(GatewayCommandReceipt.Fields.Operation, operation)
            .Where(GatewayCommandReceipt.Fields.IdempotencyKey, idempotencyKey)
            .Take(2).ToArrayAsync(2, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayCommandReceipt>? match = records.SingleOrDefault(record =>
            StringComparer.Ordinal.Equals(record.Value.NamespaceId, namespaceId) &&
            StringComparer.Ordinal.Equals(record.Value.TargetNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(record.Value.Operation, operation) &&
            StringComparer.Ordinal.Equals(record.Value.IdempotencyKey, idempotencyKey));
        return match is null ? null : Project(match);
    }

    public async ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> GetOperationAsync(
        string namespaceId, string operationId,
        CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(operationId, nameof(operationId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayCommandReceipt>[] records = (await Session(namespaceId)
            .Collection(GatewayCommandReceipt.Collection).Query()
            .Where(GatewayCommandReceipt.Fields.NamespaceId, namespaceId)
            .Where(GatewayCommandReceipt.Fields.StableOperationId, operationId)
            .Take(2).ToArrayAsync(2, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayCommandReceipt>? match = records.SingleOrDefault(record =>
            StringComparer.Ordinal.Equals(record.Value.NamespaceId, namespaceId) &&
            StringComparer.Ordinal.Equals(record.Value.StableOperationId, operationId));
        return match is null ? null : Project(match);
    }

    public async ValueTask<GatewayAdministrativeOperationReadProjection?> GetAdministrativeOperationAsync(
        string namespaceId, string operationId,
        CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(operationId, nameof(operationId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseSession session = Session(namespaceId);
        BaseResult<BaseRecord<GatewayAdministrativeOperationIntent>> intentResult = await session
            .Collection(GatewayAdministrativeOperationIntent.Collection)
            .GetAsync(RecordId.Create(operationId), cancellationToken).ConfigureAwait(false);
        if (!intentResult.TryGetValue(out BaseRecord<GatewayAdministrativeOperationIntent>? intent)) return null;
        if (!StringComparer.Ordinal.Equals(intent!.Value.NamespaceId, namespaceId)) return null;
        BaseRecord<GatewayAdministrativeExecutionState> execution = (await session
            .Collection(GatewayAdministrativeExecutionState.Collection)
            .GetAsync(GatewayAuthorityRecordIds.AdministrativeExecution(namespaceId, operationId), cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        ValidateAdministrativeExecution(execution, operationId);

        BaseRecord<GatewayAdministrativeOperationObservation>[] observations = (await session
            .Collection(GatewayAdministrativeOperationObservation.Collection).Query()
            .Where(GatewayAdministrativeOperationObservation.Fields.IntentId, operationId)
            .Take(2).ToArrayAsync(2, cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayAdministrativeOperationCompletion>[] completions = (await session
            .Collection(GatewayAdministrativeOperationCompletion.Collection).Query()
            .Where(GatewayAdministrativeOperationCompletion.Fields.IntentId, operationId)
            .Take(2).ToArrayAsync(2, cancellationToken).ConfigureAwait(false)).RequireValue();
        if (observations.Length > 1 || completions.Length > 1)
            throw new InvalidOperationException("The administrative operation graph is not unique.");
        BaseRecord<GatewayAdministrativeOperationObservation>? observation = observations.SingleOrDefault();
        BaseRecord<GatewayAdministrativeOperationCompletion>? completion = completions.SingleOrDefault();
        string? artifactReference = null;
        BaseResult<BaseRecord<GatewayAdministrativeArtifactObservation>> artifact = await session
            .Collection(GatewayAdministrativeArtifactObservation.Collection)
            .GetAsync(GatewayAuthorityRecordIds.AdministrativeArtifact(namespaceId, operationId), cancellationToken)
            .ConfigureAwait(false);
        if (artifact.TryGetValue(out BaseRecord<GatewayAdministrativeArtifactObservation>? artifactRecord))
        {
            if (intent.Value.Operation != GatewayAdministrativeOperationKind.Backup ||
                !StringComparer.Ordinal.Equals(artifactRecord!.Value.IntentId, operationId) ||
                !StringComparer.Ordinal.Equals(artifactRecord.Value.SinkName, intent.Value.BackupSinkName))
                throw new InvalidOperationException("The administrative artifact graph is inconsistent.");
            artifactReference = artifactRecord.Value.PublicReference;
        }

        if (execution.Value.Phase is GatewayAdministrativeExecutionPhase.Unclaimed or GatewayAdministrativeExecutionPhase.ClaimedPreBoundary)
        {
            if (observation is not null || completion is not null || artifactReference is not null)
                throw new InvalidOperationException("Pre-boundary administration has impossible outcome records.");
            return new(operationId, intent.Value.Operation, GatewayAdministrativeOperationReadState.IntentRecorded,
                "management.admin.intent-recorded", null, null);
        }
        if (execution.Value.Phase == GatewayAdministrativeExecutionPhase.BoundaryCrossed)
        {
            if (observation is not null || completion is not null)
                throw new InvalidOperationException("Boundary-crossed administration has a half-published outcome.");
            return new(operationId, intent.Value.Operation, GatewayAdministrativeOperationReadState.IndeterminatePending,
                "management.admin.execution-indeterminate", artifactReference, null);
        }
        if (observation is null || !StringComparer.Ordinal.Equals(execution.Value.ObservationId, observation.Id.Value))
            throw new InvalidOperationException("Observed administration is missing its exact observation.");
        if (completion is not null && !StringComparer.Ordinal.Equals(completion.Value.ObservationId, observation.Id.Value))
            throw new InvalidOperationException("Administrative completion references another observation.");
        GatewayAdministrativeOperationReadState state = observation.Value.Kind switch
        {
            GatewayAdministrativeObservationKind.Succeeded when completion is null => GatewayAdministrativeOperationReadState.ExecutionSucceededCompletionPending,
            GatewayAdministrativeObservationKind.Succeeded when completion!.Value.State == GatewayAdministrativeCompletionState.Completed => GatewayAdministrativeOperationReadState.Completed,
            GatewayAdministrativeObservationKind.Failed when completion is null ||
                completion.Value.State == GatewayAdministrativeCompletionState.Failed => GatewayAdministrativeOperationReadState.Failed,
            GatewayAdministrativeObservationKind.Indeterminate when completion is null ||
                completion.Value.State == GatewayAdministrativeCompletionState.IndeterminatePending => GatewayAdministrativeOperationReadState.IndeterminatePending,
            _ => throw new InvalidOperationException("The administrative completion state is inconsistent."),
        };
        return new(operationId, intent.Value.Operation, state, observation.Value.ResultCode,
            artifactReference, observation.CreatedAt);
    }

    public ValueTask<GatewayManagedRecord<GatewayAcceptedRevision>?> GetRevisionAsync(
        string namespaceId, string targetNodeId, string revisionId, CancellationToken cancellationToken = default) =>
        GetTarget(namespaceId, targetNodeId, revisionId, Session(namespaceId).Collection(GatewayAcceptedRevision.Collection),
            static value => value.NamespaceId, static value => value.TargetNodeId, cancellationToken);

    public ValueTask<GatewayManagedRecord<GatewayValidationRecord>?> GetValidationAsync(
        string namespaceId, string targetNodeId, string validationId, CancellationToken cancellationToken = default) =>
        GetTarget(namespaceId, targetNodeId, validationId, Session(namespaceId).Collection(GatewayValidationRecord.Collection),
            static value => value.NamespaceId, static value => value.TargetNodeId, cancellationToken);

    public ValueTask<GatewayManagedPage<GatewayAcceptedRevision>> ListRevisionsAsync(
        string namespaceId, string targetNodeId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        return Page(namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayAcceptedRevision.Collection).Query()
                .Where(GatewayAcceptedRevision.Fields.NamespaceId, namespaceId)
                .Where(GatewayAcceptedRevision.Fields.TargetNodeId, targetNodeId),
            static value => value.NamespaceId, cancellationToken);
    }

    public ValueTask<GatewayManagedPage<GatewayAdministrativeAuditRecord>> ListAuditAsync(
        string namespaceId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default) =>
        Page(
            namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayAdministrativeAuditRecord.Collection).Query()
                .Where(GatewayAdministrativeAuditRecord.Fields.NamespaceId, namespaceId),
            static value => value.NamespaceId,
            cancellationToken);

    public ValueTask<GatewayManagedPage<GatewayActivationIntent>> ListActivationsAsync(
        string namespaceId, string targetNodeId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        return Page(namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayActivationIntent.Collection).Query()
                .Where(GatewayActivationIntent.Fields.NamespaceId, namespaceId)
                .Where(GatewayActivationIntent.Fields.TargetNodeId, targetNodeId),
            value => StringComparer.Ordinal.Equals(value.TargetNodeId, targetNodeId) ? value.NamespaceId : string.Empty,
            cancellationToken);
    }

    public ValueTask<GatewayManagedPage<GatewayNodeActivationOutcome>> ListOutcomesAsync(
        string namespaceId, string targetNodeId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        return Page(namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayNodeActivationOutcome.Collection).Query()
                .Where(GatewayNodeActivationOutcome.Fields.NamespaceId, namespaceId)
                .Where(GatewayNodeActivationOutcome.Fields.TargetNodeId, targetNodeId),
            value => StringComparer.Ordinal.Equals(value.TargetNodeId, targetNodeId) ? value.NamespaceId : string.Empty,
            cancellationToken);
    }

    private async ValueTask<GatewayManagedRecord<T>?> Get<T>(
        string namespaceId, string recordId, BaseCollectionSession<T> collection,
        Func<T, string> namespaceSelector, CancellationToken cancellationToken)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(recordId, nameof(recordId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<T>> result = await collection.GetAsync(RecordId.Create(recordId), cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out BaseRecord<T>? record) &&
            StringComparer.Ordinal.Equals(namespaceSelector(record!.Value), namespaceId)
                ? Project(record)
                : null;
    }

    private async ValueTask<GatewayManagedRecord<T>?> GetTarget<T>(
        string namespaceId, string targetNodeId, string recordId, BaseCollectionSession<T> collection,
        Func<T, string> namespaceSelector, Func<T, string> targetSelector, CancellationToken cancellationToken)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(targetNodeId, nameof(targetNodeId));
        Validate(recordId, nameof(recordId));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseResult<BaseRecord<T>> result = await collection.GetAsync(RecordId.Create(recordId), cancellationToken).ConfigureAwait(false);
        return result.TryGetValue(out BaseRecord<T>? record) &&
            StringComparer.Ordinal.Equals(namespaceSelector(record!.Value), namespaceId) &&
            StringComparer.Ordinal.Equals(targetSelector(record.Value), targetNodeId)
                ? Project(record)
                : null;
    }

    private async ValueTask<GatewayManagedPage<T>> Page<T>(
        string namespaceId,
        int maximum,
        string? continuation,
        BaseQuery<T> query,
        Func<T, string> namespaceSelector,
        CancellationToken cancellationToken)
    {
        Validate(namespaceId, nameof(namespaceId));
        if (maximum is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximum));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseQuery<T> bounded = query.Take(maximum);
        if (continuation is not null) bounded = bounded.ContinueFrom(continuation);
        BasePage<BaseRecord<T>> page = (await bounded.PageAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseRecord<T>[] visible = page.Items
            .Where(record => StringComparer.Ordinal.Equals(namespaceSelector(record.Value), namespaceId))
            .ToArray();
        return new(
            visible.Select(Project).ToImmutableArray(),
            page.Page.NextCursor,
            page.Page.HasMore);
    }

    private BaseSession Session(string? namespaceId) => sessions.For(new PrincipalContext
    {
        AuthenticationState = PrincipalAuthenticationState.System,
        SubjectId = "hpd.gateway.management.reader",
        AuthSource = GatewayManagementBasePolicy.TrustedSource,
    }, value =>
    {
        value.Mode = OperationMode.System;
        value.TenantId = namespaceId;
    });

    private static GatewayManagedRecord<T> Project<T>(BaseRecord<T> value) =>
        new(value.Id.Value, value.Value, value.CreatedAt, value.UpdatedAt);

    private static void ValidateAdministrativeExecution(
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

    private static void Validate(string value, string parameter)
    {
        if (!GatewayAuthorityRecordIds.IsCanonicalComponent(value))
            throw new ArgumentException("Management read identity is invalid.", parameter);
    }
}
