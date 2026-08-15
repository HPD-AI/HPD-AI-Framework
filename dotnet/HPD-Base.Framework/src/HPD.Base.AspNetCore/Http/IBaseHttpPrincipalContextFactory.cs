using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Creates HPD.BASE principal contexts from ASP.NET Core HTTP requests.
/// </summary>
public interface IBaseHttpPrincipalContextFactory
{
    /// <summary>
    /// Creates a principal context for the current request.
    /// </summary>
    ValueTask<PrincipalContext> CreateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
