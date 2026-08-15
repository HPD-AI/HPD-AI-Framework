using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.Auth;

/// <summary>
/// Enriches a BASE principal mapped from an HPD.Auth-backed ASP.NET Core request.
/// </summary>
internal interface IHPDBaseAuthPrincipalEnricher
{
    /// <summary>
    /// Enriches the supplied BASE principal with safe host facts.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="principal">The principal produced by the adapter mapper.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The enriched principal.</returns>
    ValueTask<PrincipalContext> EnrichAsync(
        HttpContext httpContext,
        PrincipalContext principal,
        CancellationToken cancellationToken = default);
}
