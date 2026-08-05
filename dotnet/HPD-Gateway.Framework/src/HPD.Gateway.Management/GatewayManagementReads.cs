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

public interface IGatewayManagementReader
{
    ValueTask<GatewayManagedRecord<GatewayDesiredState>?> GetDesiredAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default);
    ValueTask<GatewayManagedRecord<GatewayCommandReceipt>?> FindByIdempotencyAsync(
        string namespaceId,
        string operation,
        string idempotencyKey,
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
}

internal sealed class GatewayManagementReader(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    GatewayManagementOptions options) : IGatewayManagementReader
{
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
