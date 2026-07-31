
namespace HPD.Base;

internal sealed class BaseLiveQueryCommittedMutationObserver(
    IBaseDependencyInvalidationMapper invalidations,
    DefaultBaseLiveQueryCoordinator coordinator) : IBaseCommittedMutationObserver
{
    public async ValueTask ObserveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var invalidation = await invalidations.MapAsync(mutation, cancellationToken)
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            await coordinator.InvalidateAsync(invalidation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            coordinator.FailAll(
                BaseLiveQueryErrorCodes.InvalidationFailed,
                "Live-query invalidation could not be produced safely.");
            throw new BaseLiveQueryInvalidationObserverException();
        }
    }
}

internal sealed class BaseLiveQueryInvalidationObserverException()
    : Exception("Live-query invalidation failed.")
{
}
