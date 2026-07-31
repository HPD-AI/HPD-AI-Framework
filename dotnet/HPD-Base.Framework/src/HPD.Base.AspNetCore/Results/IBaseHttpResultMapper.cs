using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Maps HPD.BASE operation results to ASP.NET Core HTTP results.
/// </summary>
public interface IBaseHttpResultMapper
{
    /// <summary>
    /// Maps a typed operation result to an HTTP result.
    /// </summary>
    IResult ToHttpResult<T>(
        OperationResult<T> result,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext);

    /// <summary>
    /// Maps an untyped operation result to an HTTP result.
    /// </summary>
    IResult ToHttpResult(
        OperationResult result,
        HttpContext httpContext,
        HPDBaseHttpResultMappingContext mappingContext);
}
