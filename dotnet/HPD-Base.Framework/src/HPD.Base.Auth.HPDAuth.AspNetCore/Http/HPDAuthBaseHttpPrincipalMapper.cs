using HPD.Auth.Core.Interfaces;
using HPD.Base.AspNetCore.Http;
using HPD.Base.Auth.HPDAuth;
using HPD.Base.Auth.HPDAuth.AspNetCore.Configuration;
using HPD.Base.Auth.HPDAuth.AspNetCore.Observability;
using HPD.Base.Auth.HPDAuth.AspNetCore.Observability.Logging;
using HPD.Base.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<HPDAuthBaseHttpPrincipalMapper> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDAuthBaseHttpPrincipalMapper"/> class.
    /// </summary>
    /// <param name="subjectMapper">The HPD.Auth subject mapper.</param>
    /// <param name="options">ASP.NET adapter options.</param>
    /// <param name="enrichers">Optional principal enrichers.</param>
    /// <param name="logger">The principal mapper logger.</param>
    public HPDAuthBaseHttpPrincipalMapper(
        HPDAuthBaseSubjectMapper subjectMapper,
        IOptions<HPDBaseHPDAuthAspNetCoreOptions> options,
        IEnumerable<IHPDAuthBaseHttpPrincipalEnricher> enrichers,
        ILogger<HPDAuthBaseHttpPrincipalMapper> logger)
    {
        _subjectMapper = subjectMapper;
        _options = options.Value;
        _enrichers = enrichers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<PrincipalContext?> TryMapAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();

        var enrichers = _enrichers.ToArray();
        return await HPDBaseHPDAuthAspNetCoreTelemetry.TraceMapAsync(
            enrichers.Length,
            async () =>
            {
                var tenantIdFallback = _options.UseTenantContextFallback
                    ? httpContext.RequestServices.GetService<ITenantContext>()?.InstanceId.ToString()
                    : null;

                var principal = _subjectMapper.Map(httpContext.User, tenantIdFallback);
                if (enrichers.Length > 0)
                {
                    principal = await HPDBaseHPDAuthAspNetCoreTelemetry.TraceEnrichAsync(
                        principal,
                        enrichers.Length,
                        async () =>
                        {
                            foreach (var enricher in enrichers)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                try
                                {
                                    principal = await enricher.EnrichAsync(httpContext, principal, cancellationToken).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch
                                {
                                    HPDBaseHPDAuthAspNetCoreLog.PrincipalEnrichmentFailed(
                                        _logger,
                                        "dependency",
                                        "hpd.auth.base.principalEnrichmentFailed");
                                    throw;
                                }
                            }

                            return principal;
                        }).ConfigureAwait(false);
                }

                return principal;
            }).ConfigureAwait(false);
    }
}
