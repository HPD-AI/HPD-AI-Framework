using HPD.Base.Events;
using HPD.Base.Results;

namespace HPD.Base.Runtime.Events;

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
