using HPD.Base.Results;

namespace HPD.Base.Runtime.Results;

public static class OperationResultExtensions
{
    public static bool IsSuccess(this OperationStatus status) =>
        status is OperationStatus.Ok
            or OperationStatus.Created
            or OperationStatus.Updated
            or OperationStatus.Deleted
            or OperationStatus.NoContent;

    public static bool IsSuccess<T>(this OperationResult<T> result) => result.Status.IsSuccess();

    public static bool IsSuccess(this OperationResult result) => result.Status.IsSuccess();

    public static bool RequiresError(this OperationStatus status) => !status.IsSuccess();
}
