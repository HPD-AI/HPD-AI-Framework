using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Auth.ControlPlane;

internal sealed class ControlPlaneStartupValidator(
    ControlPlaneRegistry registry,
    HPDControlPlaneOptions controlPlaneOptions,
    IOptions<AuthorizationOptions> authorization,
    IOptions<RequestTimeoutOptions> requestTimeouts,
    IServiceProvider services,
    IAuthorizationPolicyProvider authorizationPolicies,
    IEnumerable<ControlPlaneOpenApiRegistration> openApiRegistrations,
    IEnumerable<EndpointDataSource> endpointDataSources) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.ValidatePolicies(authorization.Value);
        var authenticationSchemes = services.GetService<IAuthenticationSchemeProvider>()
            ?? throw new InvalidOperationException("hpd.auth.controlPlane.scheme.missing");
        foreach (var profile in registry.Profiles)
        {
            if (await authenticationSchemes.GetSchemeAsync(profile.AuthenticationScheme) is null)
                throw new InvalidOperationException("hpd.auth.controlPlane.scheme.missing");
            if (profile.RequestTimeoutPolicy is { } timeout &&
                !requestTimeouts.Value.Policies.ContainsKey(timeout))
                throw new InvalidOperationException("hpd.auth.controlPlane.timeoutPolicy.missing");
        }
        if (controlPlaneOptions.StrictOpenApiValidation &&
            registry.Profiles.Any(static profile => profile.OpenApiSecurityScheme is not null) &&
            !openApiRegistrations.Any())
            throw new InvalidOperationException("hpd.auth.controlPlane.profile.invalid");

        foreach (var endpoint in endpointDataSources.SelectMany(static source => source.Endpoints))
        {
            var profiles = endpoint.Metadata.GetOrderedMetadata<ControlPlaneEndpointMetadata>();
            if (profiles.Count == 0)
                continue;
            if (profiles.Count != 1)
                throw new InvalidOperationException("hpd.auth.controlPlane.profile.invalid");
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                throw new InvalidOperationException("hpd.auth.controlPlane.endpoint.anonymous");
            var capabilities = endpoint.Metadata.GetOrderedMetadata<ControlPlaneCapabilityMetadata>();
            if (capabilities.Count == 0)
                throw new InvalidOperationException("hpd.auth.controlPlane.capability.unmapped");
            if (capabilities.Count != 1)
                throw new InvalidOperationException("hpd.auth.controlPlane.capability.duplicate");

            var profile = registry.GetProfile(profiles[0].Profile);
            var combined = await AuthorizationPolicy.CombineAsync(
                authorizationPolicies,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
            if (combined is null || combined.AuthenticationSchemes.Count != 1 ||
                !string.Equals(combined.AuthenticationSchemes[0], profile.AuthenticationScheme, StringComparison.Ordinal))
                throw new InvalidOperationException("hpd.auth.controlPlane.policy.schemeConflict");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
