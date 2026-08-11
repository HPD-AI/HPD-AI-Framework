using HPD.Auth.ControlPlane;
using HPD.Auth.Core.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Gateway.ControlPlane.HPDAuth;

public static class GatewayHpdAuthAdminServiceCollectionExtensions
{
    public static GatewayControlPlaneBuilder AddHpdAuth(
        this GatewayControlPlaneBuilder builder,
        string controlPlaneProfile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneProfile);
        if (builder.Registration.AdminOptions is null)
            throw new InvalidOperationException("Configure the Gateway Admin API before HPD.Auth translation.");
        if (builder.Registration.HpdAuthConfigured)
            throw new InvalidOperationException("HPD.Auth translation is already configured.");

        builder.Services.AddSingleton(new GatewayHpdAuthAdminOptions(controlPlaneProfile));
        builder.Services.Replace(ServiceDescriptor.Scoped<IGatewayAdminActorProjector, GatewayHpdAuthAdminActorProjector>());
        builder.Services.Replace(ServiceDescriptor.Singleton<IGatewayAdminSecurityMetadataProvider, GatewayHpdAuthAdminSecurityMetadataProvider>());
        builder.Registration.HpdAuthConfigured = true;
        return builder;
    }
}

internal sealed record GatewayHpdAuthAdminOptions(string Profile);

internal sealed class GatewayHpdAuthAdminActorProjector(
    GatewayHpdAuthAdminOptions options,
    IAuthenticatedActorProjector actors,
    IAuthCorrelationContext correlation,
    ControlPlaneRegistry registry) : IGatewayAdminActorProjector
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
}

internal sealed class GatewayHpdAuthAdminSecurityMetadataProvider(
    GatewayHpdAuthAdminOptions options,
    ControlPlaneRegistry registry) : IGatewayAdminSecurityMetadataProvider
{
    public void Validate(GatewayAdminApiOptions endpointOptions)
    {
        ControlPlaneProfile profile = registry.GetProfile(options.Profile);
        if (!StringComparer.Ordinal.Equals(endpointOptions.AuthenticationScheme, profile.AuthenticationScheme) ||
            !StringComparer.Ordinal.Equals(endpointOptions.RateLimitPolicy, profile.RateLimitPolicy) ||
            !StringComparer.Ordinal.Equals(endpointOptions.RequestTimeoutPolicy, profile.RequestTimeoutPolicy) ||
            !StringComparer.Ordinal.Equals(endpointOptions.OpenApiSecurityScheme, profile.OpenApiSecurityScheme))
            throw new InvalidOperationException("The Gateway Admin endpoint options do not match the selected HPD.Auth control-plane profile.");
        foreach (string capability in GatewayAdminCapabilities.All)
            if (!StringComparer.Ordinal.Equals(endpointOptions.CapabilityPolicies[capability], registry.GetAuthorizationPolicy(capability)))
                throw new InvalidOperationException("The Gateway Admin capability mapping does not match HPD.Auth authority.");
    }

    public void ApplyGroup(IEndpointConventionBuilder group) =>
        group.WithMetadata(new ControlPlaneEndpointMetadata(options.Profile));

    public void ApplyEndpoint(IEndpointConventionBuilder endpoint, string capability) =>
        endpoint.WithMetadata(new ControlPlaneCapabilityMetadata(capability));
}
