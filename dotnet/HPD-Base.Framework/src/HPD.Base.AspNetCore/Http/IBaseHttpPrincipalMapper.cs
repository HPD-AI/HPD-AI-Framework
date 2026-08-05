using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore;

/// <summary>
/// Allows host or future auth packages to replace HTTP principal mapping.
/// </summary>
public interface IBaseHttpPrincipalMapper
{
    /// <summary>
    /// Attempts to map the current HTTP request to a BASE principal.
    /// </summary>
    ValueTask<PrincipalContext?> TryMapAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}
