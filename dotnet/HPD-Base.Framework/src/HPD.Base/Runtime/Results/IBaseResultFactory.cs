
namespace HPD.Base;

/// <summary>Defines the ibase result factory contract.</summary>
public interface IBaseResultFactory
{
    /// <summary>Executes the success operation.</summary>
    OperationResult<T> Success<T>(OperationStatus status, T value);
    /// <summary>Executes the failure operation.</summary>
    OperationResult<T> Failure<T>(OperationStatus status, BaseError error);
    /// <summary>Executes the failure operation.</summary>
    OperationResult Failure(OperationStatus status, BaseError error);
}
