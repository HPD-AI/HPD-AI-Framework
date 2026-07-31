using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

internal static class BaseHttpStatusCodeMapper
{
    public static int ToStatusCode(OperationStatus status) =>
        status switch
        {
            OperationStatus.Ok => StatusCodes.Status200OK,
            OperationStatus.Created => StatusCodes.Status201Created,
            OperationStatus.Updated => StatusCodes.Status200OK,
            OperationStatus.Deleted => StatusCodes.Status200OK,
            OperationStatus.NoContent => StatusCodes.Status204NoContent,
            OperationStatus.NotFound => StatusCodes.Status404NotFound,
            OperationStatus.Conflict => StatusCodes.Status409Conflict,
            OperationStatus.ValidationFailed => StatusCodes.Status400BadRequest,
            OperationStatus.PolicyDenied => StatusCodes.Status403Forbidden,
            OperationStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            OperationStatus.Unsupported => StatusCodes.Status400BadRequest,
            OperationStatus.CapabilityUnavailable => StatusCodes.Status424FailedDependency,
            OperationStatus.StoreError => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
}
