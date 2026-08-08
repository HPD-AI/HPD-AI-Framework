using HPD.Auth.Core.Interfaces;
using HPD.Auth.ControlPlane;
using HPD.Base.AspNetCore;
using HPD.Base.Auth;
using HPD.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Auth;

/// <summary>
/// Maps ASP.NET Core HPD.Auth principals into BASE principal contexts.
/// </summary>
internal sealed class HPDBaseAuthHttpPrincipalMapper : IBaseHttpPrincipalMapper
{
    private readonly HPDBaseAuthSubjectProjector _subjectMapper;
    private readonly HPDBaseAuthOptions _options;
    private readonly IEnumerable<IHPDBaseAuthPrincipalEnricher> _enrichers;
    private readonly ILogger<HPDBaseAuthHttpPrincipalMapper> _logger;
    private readonly DefaultBaseHttpPrincipalMapper _genericMapper;
    private readonly IAuthenticatedActorProjector _actorProjector;

    /// <summary>
    /// Initializes a new instance of the <see cref="HPDBaseAuthHttpPrincipalMapper"/> class.
    /// </summary>
    /// <param name="subjectMapper">The HPD.Auth subject mapper.</param>
    /// <param name="options">ASP.NET adapter options.</param>
    /// <param name="enrichers">Optional principal enrichers.</param>
    /// <param name="logger">The principal mapper logger.</param>
    public HPDBaseAuthHttpPrincipalMapper(
        HPDBaseAuthSubjectProjector subjectMapper,
        IOptions<HPDBaseAuthOptions> options,
        IEnumerable<IHPDBaseAuthPrincipalEnricher> enrichers,
        DefaultBaseHttpPrincipalMapper genericMapper,
        IAuthenticatedActorProjector actorProjector,
        ILogger<HPDBaseAuthHttpPrincipalMapper> logger)
    {
        _subjectMapper = subjectMapper;
        _options = options.Value;
        _enrichers = enrichers;
        _genericMapper = genericMapper;
        _actorProjector = actorProjector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<PrincipalContext> MapAsync(
        HttpContext httpContext,
        HPDBaseEndpointDescriptor endpoint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await MapCoreAsync(httpContext, endpoint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HPDBaseAuthProjectionException) { throw; }
        catch
        {
            throw new HPDBaseAuthProjectionException("base.auth.actor.projectionFailed", StatusCodes.Status403Forbidden);
        }
    }

    private async ValueTask<PrincipalContext> MapCoreAsync(
        HttpContext httpContext,
        HPDBaseEndpointDescriptor endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();

        if (endpoint.Audience != HPDBaseEndpointAudience.ControlPlane)
            return await _genericMapper.MapAsync(httpContext, endpoint, cancellationToken).ConfigureAwait(false);

        ControlPlaneEndpointMetadata metadata = httpContext.GetEndpoint()?.Metadata
            .GetOrderedMetadata<ControlPlaneEndpointMetadata>().SingleOrDefault()
            ?? throw new HPDBaseAuthProjectionException("base.auth.actor.projectionFailed", StatusCodes.Status403Forbidden);
        AuthenticatedActorProjection actor;
        try
        {
            actor = await _actorProjector.ProjectAsync(httpContext, metadata.Profile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw new HPDBaseAuthProjectionException("base.auth.actor.projectionFailed", StatusCodes.Status403Forbidden); }

        var enrichers = _enrichers.ToArray();
        return await HPDBaseHPDAuthAspNetCoreTelemetry.TraceMapAsync(
            enrichers.Length,
            async () =>
            {
                string? tenantIdFallback = null;

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

                if (principal.SubjectId is not null && !string.Equals(principal.SubjectId, actor.ActorId, StringComparison.Ordinal))
                    throw new HPDBaseAuthProjectionException("base.auth.actor.subjectMismatch", StatusCodes.Status403Forbidden);
                if (principal.CurrentTenantId is not null && actor.TenantId is not null &&
                    !string.Equals(principal.CurrentTenantId, actor.TenantId, StringComparison.Ordinal))
                    throw new HPDBaseAuthProjectionException("base.auth.actor.tenantMismatch", StatusCodes.Status403Forbidden);
                return principal with
                {
                    SubjectId = new string(actor.ActorId.AsSpan()),
                    CurrentTenantId = actor.TenantId is null ? principal.CurrentTenantId : new string(actor.TenantId.AsSpan()),
                    AuthSource = new string(actor.AuthenticationProfile.AsSpan())
                };
            }).ConfigureAwait(false);
    }
}
