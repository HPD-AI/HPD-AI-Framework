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
        CancellationToken cancellationToken = default);
}
