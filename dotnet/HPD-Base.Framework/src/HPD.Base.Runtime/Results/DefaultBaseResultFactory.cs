using HPD.Base.Results;

namespace HPD.Base.Runtime.Results;

internal sealed class DefaultBaseResultFactory : IBaseResultFactory
{
    public OperationResult<T> Success<T>(OperationStatus status, T value)
    {
        if (!status.IsSuccess())
        {
            throw new ArgumentException("Success results require a success status.", nameof(status));
        }

        return new OperationResult<T> { Status = status, Value = value };
    }

    public OperationResult<T> Failure<T>(OperationStatus status, BaseError error)
    {
        if (!status.RequiresError())
        {
            throw new ArgumentException("Failure results require a failure status.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T> { Status = status, Error = error };
    }

    public OperationResult Failure(OperationStatus status, BaseError error)
    {
        if (!status.RequiresError())
        {
            throw new ArgumentException("Failure results require a failure status.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult { Status = status, Error = error };
    }
}
