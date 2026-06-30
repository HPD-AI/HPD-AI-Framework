using HPD.Auth.Core.Interfaces;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Auth.HPDAuth;
using HPD.Base.Auth.HPDAuth.AspNetCore.Configuration;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Http;

/// <summary>
/// Maps ASP.NET Core HPD.Auth principals into BASE principal contexts.
/// </summary>
public sealed class HPDAuthBaseHttpPrincipalMapper : IBaseHttpPrincipalMapper
{
    private readonly HPDAuthBaseSubjectMapper _subjectMapper;
    private readonly HPDBaseHPDAuthAspNetCoreOptions _options;
    private readonly IEnumerable<IHPDAuthBaseHttpPrincipalEnricher> _enrichers;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseHttpPrincipalMapper"/> class.
    /// </summary>
    /// <param name="subjectMapper">The HPD.Auth subject mapper.</param>
    /// <param name="options">ASP.NET adapter options.</param>
    /// <param name="enrichers">Optional principal enrichers.</param>
    public HPDAuthBaseHttpPrincipalMapper(
        HPDAuthBaseSubjectMapper subjectMapper,
        IOptions<HPDBaseHPDAuthAspNetCoreOptions> options,
        IEnumerable<IHPDAuthBaseHttpPrincipalEnricher> enrichers)
    {
        _subjectMapper = subjectMapper;
        _options = options.Value;
        _enrichers = enrichers;
    }

    /// <inheritdoc />
    public async ValueTask<PrincipalContext?> TryMapAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantIdFallback = _options.UseTenantContextFallback
            ? httpContext.RequestServices.GetService<ITenantContext>()?.InstanceId.ToString()
            : null;

        var principal = _subjectMapper.Map(httpContext.User, tenantIdFallback);
        foreach (var enricher in _enrichers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            principal = await enricher.EnrichAsync(httpContext, principal, cancellationToken).ConfigureAwait(false);
        }

        return principal;
    }
}
