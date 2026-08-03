using HPD.Base;

namespace HPD.Gateway.Management;

public sealed record GatewayManagementStatusSnapshot(
    bool AuthorityReady,
    GatewayAuthorityDurability? Durability,
    int PendingDeliveryCount,
    int IndeterminateDeliveryCount,
    bool ServingReadinessAffected,
    string Code);

public interface IGatewayManagementStatusReader
{
    ValueTask<GatewayManagementStatusSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);
}

internal sealed class GatewayManagementStatusReader(
    IGatewayAuthorityRuntime authority,
    IBaseSessionFactory sessions,
    GatewayManagementOptions options) : IGatewayManagementStatusReader
{
    public async ValueTask<GatewayManagementStatusSnapshot> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        GatewayAuthorityCapabilitySnapshot capabilities;
        try { capabilities = await authority.InitializeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            return new(false, null, 0, 0, false, "management.authority.unavailable");
        }
        BaseSession session = sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "hpd.gateway.management.status",
            AuthSource = GatewayManagementBasePolicy.TrustedSource,
        }, value => value.Mode = OperationMode.System);
        BaseResult<BaseRecord<GatewayDeliveryOutboxItem>[]> result = await session
            .Collection(GatewayDeliveryOutboxItem.Collection).Query()
            .Take(options.MaximumTargets).ToArrayAsync(options.MaximumTargets, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetValue(out BaseRecord<GatewayDeliveryOutboxItem>[]? items))
            return new(true, capabilities.Durability, 0, 0, false, "management.outbox.not-observed");
        BaseRecord<GatewayDeliveryOutboxItem>[] observedItems = items!;
        int indeterminate = observedItems.Count(static item =>
            item.Value.State == GatewayDeliveryState.OutcomePersistencePending);
        int pending = observedItems.Count(static item => item.Value.State is
            GatewayDeliveryState.Immediate or GatewayDeliveryState.Claimed or
            GatewayDeliveryState.RetryScheduled or GatewayDeliveryState.OutcomePersistencePending);
        return new(
            true, capabilities.Durability, pending, indeterminate, false,
            indeterminate > 0 ? "management.delivery.indeterminate" :
            pending > 0 ? "management.delivery.pending" : "management.ready");
    }
}
