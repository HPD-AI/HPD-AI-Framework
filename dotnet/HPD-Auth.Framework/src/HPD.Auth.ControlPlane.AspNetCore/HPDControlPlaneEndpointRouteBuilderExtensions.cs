using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.ControlPlane;

/// <summary>Applies the validated control-plane convention to ASP.NET endpoints.</summary>
public static class HPDControlPlaneEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapHPDControlPlaneGroup(
        this IEndpointRouteBuilder endpoints,
        string prefix,
        string profileName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        var registry = endpoints.ServiceProvider.GetRequiredService<ControlPlaneRegistry>();
        var profile = registry.GetProfile(profileName);
        var profilePolicy = new AuthorizationPolicyBuilder(profile.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

        var group = endpoints.MapGroup(prefix)
            .WithMetadata(new ControlPlaneEndpointMetadata(profile.Name))
            .RequireAuthorization(profilePolicy);

        if (profile.RateLimitPolicy is not null)
            group.RequireRateLimiting(profile.RateLimitPolicy);
        if (profile.RequestTimeoutPolicy is not null)
            group.WithRequestTimeout(profile.RequestTimeoutPolicy);

        return group;
    }

    public static TBuilder RequireHPDControlPlaneCapability<TBuilder>(
        this TBuilder builder,
        IEndpointRouteBuilder endpoints,
        string capability)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetRequiredService<ControlPlaneRegistry>();
        var policy = registry.GetAuthorizationPolicy(capability);

        builder.Add(endpointBuilder =>
        {
            if (endpointBuilder.Metadata.OfType<IAllowAnonymous>().Any())
                throw new InvalidOperationException("A control-plane endpoint cannot allow anonymous access.");

            endpointBuilder.Metadata.Add(new ControlPlaneCapabilityMetadata(capability));
            endpointBuilder.Metadata.Add(new AuthorizeAttribute(policy));
        });
        return builder;
    }
}
