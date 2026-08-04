using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HPD.Auth.ControlPlane;

internal sealed class ControlPlaneStartupValidator(
    ControlPlaneRegistry registry,
    IOptions<AuthorizationOptions> authorization,
    IEnumerable<EndpointDataSource> endpointDataSources) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        registry.ValidatePolicies(authorization.Value);
        foreach (var endpoint in endpointDataSources.SelectMany(static source => source.Endpoints))
        {
            if (endpoint.Metadata.GetMetadata<ControlPlaneEndpointMetadata>() is null)
                continue;
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                throw new InvalidOperationException("A control-plane endpoint cannot allow anonymous access.");
            if (endpoint.Metadata.GetMetadata<ControlPlaneCapabilityMetadata>() is null)
                throw new InvalidOperationException("A control-plane endpoint must declare one capability.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
