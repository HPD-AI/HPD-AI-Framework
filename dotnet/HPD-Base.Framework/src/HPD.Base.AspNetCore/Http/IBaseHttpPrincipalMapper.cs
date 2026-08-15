using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Allows host or future auth packages to replace HTTP principal mapping.
/// </summary>
public interface IBaseHttpPrincipalMapper
{
    /// <summary>
    /// Maps the current HTTP request to a BASE principal for the exact endpoint.
    /// </summary>
    ValueTask<PrincipalContext> MapAsync(
        HttpContext httpContext,
        HPDBaseEndpointDescriptor endpoint,
        CancellationToken cancellationToken = default);
}
