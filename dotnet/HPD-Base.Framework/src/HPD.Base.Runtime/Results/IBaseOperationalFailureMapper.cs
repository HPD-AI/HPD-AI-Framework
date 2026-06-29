using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Results;

public interface IBaseOperationalFailureMapper
{
    bool TryMap(Exception exception, OperationContext operation, out BaseError error, out OperationStatus status);
}
