using System.Collections.Immutable;
using HPD.Base;

namespace HPD.Gateway.Management;

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

public interface IGatewayManagementReader
{
    ValueTask<GatewayManagedRecord<GatewayDesiredState>?> GetDesiredAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayDesiredProjection?> GetDesiredProjectionAsync(
        string namespaceId,
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> FindByIdempotencyAsync(
        string namespaceId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> GetOperationAsync(
        string namespaceId,
        string operationId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayAcceptedRevision>?> GetRevisionAsync(
        string namespaceId,
        string revisionId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayValidationRecord>?> GetValidationAsync(
        string namespaceId,
        string validationId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedPage<GatewayAcceptedRevision>> ListRevisionsAsync(
        string namespaceId,
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
    GatewayManagementOptions options) : IGatewayManagementReader
{
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
        string namespaceId, string operation, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(namespaceId, nameof(namespaceId));
        Validate(operation, nameof(operation));
        Validate(idempotencyKey, nameof(idempotencyKey));
        await authority.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BaseRecord<GatewayCommandReceipt>[] records = (await Session(namespaceId)
            .Collection(GatewayCommandReceipt.Collection).Query().Take(256).ToArrayAsync(256, cancellationToken)
            .ConfigureAwait(false)).RequireValue();
        BaseRecord<GatewayCommandReceipt>? match = records.SingleOrDefault(record =>
            StringComparer.Ordinal.Equals(record.Value.NamespaceId, namespaceId) &&
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

    public ValueTask<GatewayManagedRecord<GatewayAcceptedRevision>?> GetRevisionAsync(
        string namespaceId, string revisionId, CancellationToken cancellationToken = default) =>
        Get(namespaceId, revisionId, Session(namespaceId).Collection(GatewayAcceptedRevision.Collection),
            static value => value.NamespaceId, cancellationToken);

    public ValueTask<GatewayManagedRecord<GatewayValidationRecord>?> GetValidationAsync(
        string namespaceId, string validationId, CancellationToken cancellationToken = default) =>
        Get(namespaceId, validationId, Session(namespaceId).Collection(GatewayValidationRecord.Collection),
            static value => value.NamespaceId, cancellationToken);

    public ValueTask<GatewayManagedPage<GatewayAcceptedRevision>> ListRevisionsAsync(
        string namespaceId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default) =>
        Page(
            namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayAcceptedRevision.Collection).Query(),
            static value => value.NamespaceId,
            cancellationToken);

    public ValueTask<GatewayManagedPage<GatewayAdministrativeAuditRecord>> ListAuditAsync(
        string namespaceId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default) =>
        Page(
            namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayAdministrativeAuditRecord.Collection).Query(),
            static value => value.NamespaceId,
            cancellationToken);

    public ValueTask<GatewayManagedPage<GatewayActivationIntent>> ListActivationsAsync(
        string namespaceId, string targetNodeId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        return Page(namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayActivationIntent.Collection).Query(),
            value => StringComparer.Ordinal.Equals(value.TargetNodeId, targetNodeId) ? value.NamespaceId : string.Empty,
            cancellationToken);
    }

    public ValueTask<GatewayManagedPage<GatewayNodeActivationOutcome>> ListOutcomesAsync(
        string namespaceId, string targetNodeId, int maximum, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        Validate(targetNodeId, nameof(targetNodeId));
        return Page(namespaceId, maximum, continuationToken,
            Session(namespaceId).Collection(GatewayNodeActivationOutcome.Collection).Query(),
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

    private static void Validate(string value, string parameter)
    {
        if (!GatewayAuthorityRecordIds.IsCanonicalComponent(value))
            throw new ArgumentException("Management read identity is invalid.", parameter);
    }
}
