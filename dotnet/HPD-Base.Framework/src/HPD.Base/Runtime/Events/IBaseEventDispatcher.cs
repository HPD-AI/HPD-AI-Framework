
namespace HPD.Base;

/// <summary>
/// Dispatches BASE events produced by the runtime operation pipeline.
/// </summary>
public interface IBaseEventDispatcher
{
    /// <summary>Dispatches a committed mutation event and returns result references.</summary>
    ValueTask<OperationResult<EventReference[]>> DispatchMutationAsync(
        BaseEvent @event,
        EventDeliveryGuarantee committedGuarantee,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Observes a record mutation after its store transaction has committed.
/// Implementations cannot roll the mutation back and must not expose payload data in failures.
/// </summary>
public interface IBaseCommittedMutationObserver
{
    /// <summary>Observes one committed record mutation before the runtime operation returns.</summary>
    ValueTask ObserveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Observes a destructive restore after the provider has committed and Runtime has
/// validated the resulting store authority.
/// </summary>
/// <remarks>
/// Implementations cannot roll the restore back. They receive no backup payload or
/// provider exception and must make repeated observation idempotent.
/// </remarks>
public interface IBaseCommittedRestoreObserver
{
    /// <summary>Observes one validated successful restore before the administration call returns.</summary>
    /// <param name="restore">The deeply owned installed restore authority.</param>
    /// <param name="cancellationToken">The bounded post-commit observation lifetime.</param>
    ValueTask ObserveAsync(
        BaseRestoreResult restore,
        CancellationToken cancellationToken = default);
}
