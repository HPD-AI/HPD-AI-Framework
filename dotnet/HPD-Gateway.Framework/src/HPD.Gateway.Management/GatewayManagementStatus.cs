using HPD.Base;

namespace HPD.Gateway.Management;

public sealed record GatewayManagementStatusSnapshot(
    bool AuthorityReady,
    GatewayAuthorityDurability? Durability,
    int PendingDeliveryCount,
    int IndeterminateDeliveryCount,
    bool ServingReadinessAffected,
    string Code,
    GatewayNodeOutcomeKind? LatestNodeOutcome,
    string? LatestNodeActivationIntentId,
    bool NodeAttemptStarted);

public interface IGatewayManagementStatusReader
{
    ValueTask<GatewayManagementStatusSnapshot> GetCurrentAsync(
        string namespaceId, string targetNodeId, CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementStatusReader(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    GatewayManagementRuntimeOptions options) : IGatewayManagementStatusReader
{
    public async ValueTask<GatewayManagementStatusSnapshot> GetCurrentAsync(
        string namespaceId, string targetNodeId, CancellationToken cancellationToken = default)
    {
        GatewayAuthorityCapabilitySnapshot capabilities;
        try { capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return new(false, null, 0, 0, false, "management.authority.unavailable", null, null, false);
        }
        BaseSession session = sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hpd.gateway.management.status",
            AuthSource = GatewayManagementBasePolicy.TrustedSource,
        }, value => value.Mode = OperationMode.System);
        BaseResult<BaseRecord<GatewayDeliveryOutboxItem>[]> result = await session
            .Collection(GatewayDeliveryOutboxItem.Collection).Query()
            .Where(GatewayDeliveryOutboxItem.Fields.NamespaceId, namespaceId)
            .Where(GatewayDeliveryOutboxItem.Fields.TargetNodeId, targetNodeId)
            .Take(options.MaximumTargets).ToArrayAsync(options.MaximumTargets, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetValue(out BaseRecord<GatewayDeliveryOutboxItem>[]? items))
            return new(true, capabilities.Durability, 0, 0, false, "management.outbox.not-observed", null, null, false);
        BaseRecord<GatewayDeliveryOutboxItem>[] observedItems = items!
            .Where(item => StringComparer.Ordinal.Equals(item.Value.NamespaceId, namespaceId) &&
                StringComparer.Ordinal.Equals(item.Value.TargetNodeId, targetNodeId))
            .ToArray();
        int indeterminate = observedItems.Count(static item =>
            item.Value.State == GatewayDeliveryState.OutcomePersistencePending);
        int pending = observedItems.Count(static item => item.Value.State is
            GatewayDeliveryState.Immediate or GatewayDeliveryState.Claimed or
            GatewayDeliveryState.RetryScheduled or GatewayDeliveryState.OutcomePersistencePending);
        BaseResult<BaseRecord<GatewayNodeActivationOutcome>[]> outcomeResult = await session
            .Collection(GatewayNodeActivationOutcome.Collection).Query()
            .Where(GatewayNodeActivationOutcome.Fields.NamespaceId, namespaceId)
            .Where(GatewayNodeActivationOutcome.Fields.TargetNodeId, targetNodeId)
            .OrderByDescending(GatewayNodeActivationOutcome.Fields.AuthorityVersion)
            .Take(1).ToArrayAsync(1, cancellationToken)
            .ConfigureAwait(false);
        BaseRecord<GatewayNodeActivationOutcome>? latestOutcomeRecord = outcomeResult.TryGetValue(
            out BaseRecord<GatewayNodeActivationOutcome>[]? outcomes)
                ? outcomes!
                    .Where(item => StringComparer.Ordinal.Equals(item.Value.NamespaceId, namespaceId) &&
                        StringComparer.Ordinal.Equals(item.Value.TargetNodeId, targetNodeId))
                    .FirstOrDefault()
                : null;
        GatewayNodeOutcomeKind? latestOutcome = latestOutcomeRecord?.Value.Kind;
        BaseRecord<GatewayDeliveryOutboxItem>? latestPending = observedItems
            .Where(static item => item.Value.PendingOutcomeKind is not null)
            .OrderByDescending(static item => item.UpdatedAt ?? item.CreatedAt)
            .FirstOrDefault();
        latestOutcome ??= latestPending?.Value.PendingOutcomeKind;
        return new(
            true, capabilities.Durability, pending, indeterminate, false,
            indeterminate > 0 ? "management.delivery.indeterminate" :
            pending > 0 ? "management.delivery.pending" : "management.ready",
            latestOutcome,
            latestOutcomeRecord?.Value.ActivationIntentId ?? latestPending?.Value.ActivationIntentId,
            observedItems.Any(static item => item.Value.AttemptCount > 0));
    }
}
