using HPD.Base.Query;
using HPD.Base.Results;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore.QueryBinding;

/// <summary>
/// Binds HPD.BASE query inputs from ASP.NET Core HTTP requests.
/// </summary>
public interface IBaseHttpQueryBinder
{
    /// <summary>
    /// Binds a record list query from the current request query string.
    /// </summary>
    ValueTask<OperationResult<RecordQuery>> BindListQueryAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds manifest expansion tokens from the current request query string.
    /// </summary>
    OperationResult<string[]> BindManifestExpand(HttpContext httpContext);
}
