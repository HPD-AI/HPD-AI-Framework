
namespace HPD.Base;

/// <summary>Represents a operation result extensions.</summary>
public static class OperationResultExtensions
{
    /// <summary>Executes the is success operation.</summary>
    public static bool IsSuccess(this OperationStatus status) =>
        status is OperationStatus.Ok
            or OperationStatus.Created
            or OperationStatus.Updated
            or OperationStatus.Deleted
            or OperationStatus.NoContent;

    /// <summary>Executes the is success operation.</summary>
    public static bool IsSuccess<T>(this OperationResult<T> result) => result.Status.IsSuccess();

    /// <summary>Executes the is success operation.</summary>
    public static bool IsSuccess(this OperationResult result) => result.Status.IsSuccess();

    /// <summary>Executes the requires error operation.</summary>
    public static bool RequiresError(this OperationStatus status) => !status.IsSuccess();
}
