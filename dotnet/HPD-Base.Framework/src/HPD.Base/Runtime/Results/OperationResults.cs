
namespace HPD.Base;

public static class OperationResults
{
    public static OperationResult<T> Ok<T>(T value) => new() { Status = OperationStatus.Ok, Value = value };
    public static OperationResult<T> Created<T>(T value) => new() { Status = OperationStatus.Created, Value = value };
    public static OperationResult<T> Updated<T>(T value) => new() { Status = OperationStatus.Updated, Value = value };
    public static OperationResult<T> Deleted<T>(T value) => new() { Status = OperationStatus.Deleted, Value = value };
    public static OperationResult NoContent() => new() { Status = OperationStatus.NoContent };
    public static OperationResult<T> NotFound<T>(BaseError error) => Failure<T>(OperationStatus.NotFound, error);
    public static OperationResult<T> Conflict<T>(BaseError error) => Failure<T>(OperationStatus.Conflict, error);
    public static OperationResult<T> ValidationFailed<T>(BaseError error) => Failure<T>(OperationStatus.ValidationFailed, error);
    public static OperationResult<T> PolicyDenied<T>(BaseError error) => Failure<T>(OperationStatus.PolicyDenied, error);
    public static OperationResult<T> Unauthorized<T>(BaseError error) => Failure<T>(OperationStatus.Unauthorized, error);
    public static OperationResult<T> Unsupported<T>(BaseError error) => Failure<T>(OperationStatus.Unsupported, error);
    public static OperationResult<T> CapabilityUnavailable<T>(BaseError error) => Failure<T>(OperationStatus.CapabilityUnavailable, error);
    public static OperationResult<T> StoreError<T>(BaseError error) => Failure<T>(OperationStatus.StoreError, error);

    private static OperationResult<T> Failure<T>(OperationStatus status, BaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new OperationResult<T> { Status = status, Error = error };
    }
}
