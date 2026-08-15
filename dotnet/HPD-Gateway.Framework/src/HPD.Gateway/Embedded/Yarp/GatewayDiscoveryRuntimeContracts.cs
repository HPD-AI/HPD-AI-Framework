using System.Collections.Immutable;
using System.Net;
using Microsoft.Extensions.Primitives;

namespace HPD.Gateway;

internal abstract record GatewayDiscoveryEndpoint;

internal sealed record GatewayUriDiscoveryEndpoint(Uri Address, string? HostName = null) : GatewayDiscoveryEndpoint;

internal sealed record GatewayDnsDiscoveryEndpoint(string Host, int Port, string? HostName = null) : GatewayDiscoveryEndpoint;

internal sealed record GatewayIpDiscoveryEndpoint(IPAddress Address, int Port, string? HostName = null) : GatewayDiscoveryEndpoint;

internal sealed record GatewayDiscoveryRequest(
    DiscoveryProfileId Profile,
    ServiceDiscoveryName Service,
    ServiceDiscoveryEndpointName? Endpoint,
    ImmutableArray<ServiceDiscoveryScheme> Schemes,
    string? TlsServerName);

internal sealed record GatewayDiscoveryResult(
    IEnumerable<GatewayDiscoveryEndpoint> Endpoints,
    IChangeToken? ChangeToken = null);

internal interface IGatewayDiscoveryRuntimeProfile
{
    DiscoveryProfileCapability Capability { get; }

    ValueTask<GatewayDiscoveryResult> ResolveAsync(
        GatewayDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}
