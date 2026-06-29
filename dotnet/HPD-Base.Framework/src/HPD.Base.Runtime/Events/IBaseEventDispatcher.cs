using HPD.Base.Events;
using HPD.Base.Results;

namespace HPD.Base.Runtime.Events;

public interface IBaseEventDispatcher
{
    ValueTask<OperationResult<EventReference[]>> DispatchMutationAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
