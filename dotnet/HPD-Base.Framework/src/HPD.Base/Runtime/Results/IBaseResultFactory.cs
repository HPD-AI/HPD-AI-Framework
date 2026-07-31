
namespace HPD.Base;

public interface IBaseResultFactory
{
    OperationResult<T> Success<T>(OperationStatus status, T value);
    OperationResult<T> Failure<T>(OperationStatus status, BaseError error);
    OperationResult Failure(OperationStatus status, BaseError error);
}
