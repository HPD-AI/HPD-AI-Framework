
namespace HPD.Base;

public interface IBaseResultNormalizer
{
    OperationResult<T> NormalizeStoreResult<T>(
        OperationResult<T> result,
        OperationContext operation);
}
