using HPD.Environment.AppleVirtualization.Networks;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;

namespace HPD.Environment.AppleVirtualization.Tests;

public sealed class AppleEndpointPublicationProviderConformanceTests
    : EndpointPublicationProviderConformanceTests
{
    protected override ValueTask<
        EndpointPublicationProviderConformanceFixture>
        CreateEndpointFixtureAsync()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var provider =
            new AppleVirtualizationEndpointPublicationProvider(
                new AppleVirtualizationProviderStateLedger(),
                helper);
        ResourceMetadata<PublishedEndpoint> metadata =
            AppleVirtualizationContractFixtures
                .Metadata<PublishedEndpoint>(
                    "endpoint-conformance",
                    "published-endpoint");
        PublishedEndpointSpec spec = EndpointSpec(8080);
        return ValueTask.FromResult(
            new EndpointPublicationProviderConformanceFixture(
                provider,
                metadata,
                spec,
                EndpointSpec(8081),
                prepareEnsure: invocation =>
                {
                    if (invocation == 0)
                        helper.EnqueueResponse(
                            EndpointResponse(
                                metadata.Id.Value));
                },
                prepareRelease: invocation =>
                {
                    if (invocation == 0)
                        helper.EnqueueResponse(
                            EndpointResponse(
                                metadata.Id.Value,
                                PublishedEndpointPhase.Released));
                },
                observedMutationCount: () =>
                    helper.Requests.Count));
    }

    private static PublishedEndpointSpec EndpointSpec(
        ushort targetPort) =>
        new()
        {
            Listener = new EndpointListenerSpec(
                EndpointListenerKind.HostAddress,
                NetworkTransport.Tcp,
                Loopback(),
                Ports: null,
                Socket: null),
            Target = new EndpointRouteTarget(
                EndpointTargetKind.NetworkAddress,
                Membership: null,
                Unit: null,
                Process: null,
                ServiceName: null,
                NetworkTransport.Tcp,
                new NetworkPort(targetPort),
                SocketPath: null,
                new IpAddressValue(
                    NetworkAddressFamily.IPv4,
                    0,
                    0x0a000002)),
            ExposurePolicy = new EndpointExposurePolicy
            {
                Scope = EndpointExposureScope.HostLocal,
                AllowEphemeralPort = true,
            },
        };

    private static IpAddressValue Loopback() =>
        new(
            NetworkAddressFamily.IPv4,
            0,
            0x7f000001);

    private static AppleVirtualizationHelperEnvelope EndpointResponse(
        string endpointId,
        PublishedEndpointPhase phase =
            PublishedEndpointPhase.Bound) =>
        AppleVirtualizationHelperEnvelope.Request(
                AppleVirtualizationHelperOperation.EndpointPublish,
                "endpoint-conformance-response",
                sequenceNumber: 1,
                AppleVirtualizationHelperProtocol
                    .EndpointPublicationResponseSchema)
            .ToResponse(sequenceNumber: 2) with
        {
            PayloadSchema =
                AppleVirtualizationHelperProtocol
                    .EndpointPublicationResponseSchema,
            EndpointPublicationResponse =
                new AppleVirtualizationEndpointPublicationResponse
                {
                    EndpointId = endpointId,
                    EndpointPhase = phase,
                    ListenerKind =
                        EndpointListenerKind.HostAddress,
                    Transport = NetworkTransport.Tcp,
                    ExposureScope =
                        EndpointExposureScope.HostLocal,
                    BoundAddress =
                        phase ==
                            PublishedEndpointPhase.Released
                            ? null
                            : "127.0.0.1",
                    BoundPort =
                        phase ==
                            PublishedEndpointPhase.Released
                            ? null
                            : (ushort)8080,
                    HpdOwned =
                        phase !=
                            PublishedEndpointPhase.Released,
                    RouteHealthy =
                        phase !=
                            PublishedEndpointPhase.Released,
                    ResolvedAddress = "10.0.0.2",
                    ResolvedPort = 8080,
                },
        };
}
