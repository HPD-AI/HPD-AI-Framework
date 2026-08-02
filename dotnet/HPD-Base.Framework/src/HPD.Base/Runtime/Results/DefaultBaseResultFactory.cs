
namespace HPD.Base;

internal sealed class DefaultBaseResultFactory : IBaseResultFactory
{
    /// <summary>Executes the success operation.</summary>
    public OperationResult<T> Success<T>(OperationStatus status, T value)
    {
        if (!status.IsSuccess())
        {
            throw new ArgumentException("Success results require a success status.", nameof(status));
        }

        return new OperationResult<T> { Status = status, Value = value };
    }

    /// <summary>Executes the failure operation.</summary>
    public OperationResult<T> Failure<T>(OperationStatus status, BaseError error)
    {
        if (!status.RequiresError())
        {
            throw new ArgumentException("Failure results require a failure status.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T> { Status = status, Error = error };
    }

    /// <summary>Executes the failure operation.</summary>
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
