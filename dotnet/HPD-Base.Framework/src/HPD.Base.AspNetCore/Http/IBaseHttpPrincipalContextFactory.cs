using HPD.Base.AspNetCore.EndpointMapping;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore.Http;

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
        HPDBaseEndpointKind endpointKind,
        CancellationToken cancellationToken = default);
}
