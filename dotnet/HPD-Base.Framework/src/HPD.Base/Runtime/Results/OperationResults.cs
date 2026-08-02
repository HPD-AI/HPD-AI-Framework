
namespace HPD.Base;

/// <summary>Represents a operation results.</summary>
public static class OperationResults
{
    /// <summary>Executes the ok operation.</summary>
    public static OperationResult<T> Ok<T>(T value) => new() { Status = OperationStatus.Ok, Value = value };
    /// <summary>Executes the created operation.</summary>
    public static OperationResult<T> Created<T>(T value) => new() { Status = OperationStatus.Created, Value = value };
    /// <summary>Executes the updated operation.</summary>
    public static OperationResult<T> Updated<T>(T value) => new() { Status = OperationStatus.Updated, Value = value };
    /// <summary>Executes the deleted operation.</summary>
    public static OperationResult<T> Deleted<T>(T value) => new() { Status = OperationStatus.Deleted, Value = value };
    /// <summary>Executes the no content operation.</summary>
    public static OperationResult NoContent() => new() { Status = OperationStatus.NoContent };
    /// <summary>Executes the not found operation.</summary>
    public static OperationResult<T> NotFound<T>(BaseError error) => Failure<T>(OperationStatus.NotFound, error);
    /// <summary>Executes the conflict operation.</summary>
    public static OperationResult<T> Conflict<T>(BaseError error) => Failure<T>(OperationStatus.Conflict, error);
    /// <summary>Executes the validation failed operation.</summary>
    public static OperationResult<T> ValidationFailed<T>(BaseError error) => Failure<T>(OperationStatus.ValidationFailed, error);
    /// <summary>Executes the policy denied operation.</summary>
    public static OperationResult<T> PolicyDenied<T>(BaseError error) => Failure<T>(OperationStatus.PolicyDenied, error);
    /// <summary>Executes the unauthorized operation.</summary>
    public static OperationResult<T> Unauthorized<T>(BaseError error) => Failure<T>(OperationStatus.Unauthorized, error);
    /// <summary>Executes the unsupported operation.</summary>
    public static OperationResult<T> Unsupported<T>(BaseError error) => Failure<T>(OperationStatus.Unsupported, error);
    /// <summary>Executes the capability unavailable operation.</summary>
    public static OperationResult<T> CapabilityUnavailable<T>(BaseError error) => Failure<T>(OperationStatus.CapabilityUnavailable, error);
    /// <summary>Executes the store error operation.</summary>
    public static OperationResult<T> StoreError<T>(BaseError error) => Failure<T>(OperationStatus.StoreError, error);

    private static OperationResult<T> Failure<T>(OperationStatus status, BaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T> { Status = status, Error = error };
    }
}
