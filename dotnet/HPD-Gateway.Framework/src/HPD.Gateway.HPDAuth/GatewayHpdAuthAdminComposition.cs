using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;
using HPD.Gateway.Admin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Gateway.HPDAuth;

public static class GatewayHpdAuthAdminServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayAdminHpdAuth(
        this IServiceCollection services,
        string controlPlaneProfile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneProfile);
        services.AddSingleton(new GatewayHpdAuthAdminOptions(controlPlaneProfile));
        services.TryAddSingleton<GatewayHpdAuthAdminBridge>();
        services.Replace(ServiceDescriptor.Singleton<IGatewayAdminActorProjector>(static provider =>
            provider.GetRequiredService<GatewayHpdAuthAdminBridge>()));
        services.Replace(ServiceDescriptor.Singleton<IGatewayAdminSecurityMetadataProvider>(static provider =>
            provider.GetRequiredService<GatewayHpdAuthAdminBridge>()));
        return services;
    }
}

internal sealed record GatewayHpdAuthAdminOptions(string Profile);

internal sealed class GatewayHpdAuthAdminBridge(
    GatewayHpdAuthAdminOptions options,
    IAuthenticatedActorProjector actors,
    IAuthCorrelationContext correlation,
    ControlPlaneRegistry registry) : IGatewayAdminActorProjector, IGatewayAdminSecurityMetadataProvider
{
    public async ValueTask<GatewayAdminRequestAttribution> ProjectAsync(
        HttpContext context, string capability, CancellationToken cancellationToken = default)
    {
        AuthenticatedActorProjection projection = await actors.ProjectAsync(
            context, options.Profile, cancellationToken).ConfigureAwait(false);
        string policy = registry.GetAuthorizationPolicy(capability);
        return new GatewayAdminRequestAttribution(
            new string(projection.ActorId.AsSpan()),
            new string(projection.AuthenticationProfile.AsSpan()),
            new string(policy.AsSpan()),
            correlation.RequireGatewayCorrelation(),
            projection.TenantId is null ? null : new string(projection.TenantId.AsSpan()));
    }

    public void ApplyGroup(IEndpointConventionBuilder group) =>
        group.WithMetadata(new ControlPlaneEndpointMetadata(options.Profile));

    public void ApplyEndpoint(IEndpointConventionBuilder endpoint, string capability) =>
        endpoint.WithMetadata(new ControlPlaneCapabilityMetadata(capability));
}
