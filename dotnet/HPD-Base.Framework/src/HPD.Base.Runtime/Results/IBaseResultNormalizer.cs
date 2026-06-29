using HPD.Base.Results;
using HPD.Base.Runtime;

namespace HPD.Base.Runtime.Results;

public interface IBaseResultNormalizer
{
    OperationResult<T> NormalizeStoreResult<T>(
        OperationResult<T> result,
        OperationContext operation);
}
