using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;
using HPD.Environment.Runtime;

namespace HPD.Environment.Local.Tests;

public sealed class LocalEndpointPublicationProviderConformanceTests
    : EndpointPublicationProviderConformanceTests
{
    protected override async ValueTask<
        EndpointPublicationProviderConformanceFixture>
        CreateEndpointFixtureAsync()
    {
        var probe = new EndpointConformanceEngineProbe();
        var module = new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe);
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(module);
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(
                new RuntimeHostSpec
                {
                    PreferredProvider =
                        LocalEnvironmentProviderDescriptor.ProviderId,
                    Platform =
                        LocalEnvironmentProviderDescriptor
                            .CurrentPlatform(),
                });
        await runtime.EnsureEngineControlPlaneAsync(
            new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootful,
                ImageStore = EngineImageStoreMode.EngineLocal,
                Host = Ref(host.Metadata),
                EndpointPolicy = new SensitiveEndpointPolicy
                {
                    Kind = SensitiveEndpointKind.EngineSocket,
                    AuthorityClass =
                        SensitiveAuthorityClass.RootfulEngineControl,
                    RequireAudit = true,
                },
            });
        var metadata = new ResourceMetadata<PublishedEndpoint>
        {
            Id = new ResourceId<PublishedEndpoint>(
                "endpoint-conformance"),
            Kind = new ResourceKind("PublishedEndpoint"),
            Scope = host.Metadata.Scope,
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
        PublishedEndpointSpec spec = EndpointSpec(
            Ref(host.Metadata),
            targetPort: 9);
        return new(
            registry.EndpointPublicationProviders.Single(),
            metadata,
            spec,
            EndpointSpec(
                Ref(host.Metadata),
                targetPort: 10));
    }

    private static PublishedEndpointSpec EndpointSpec(
        ResourceRef<RuntimeHost> host,
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
                Loopback()),
            ExposurePolicy = new EndpointExposurePolicy
            {
                Scope = EndpointExposureScope.HostLocal,
                AllowEphemeralPort = true,
            },
            AuthorizationPolicy =
                new EndpointAuthorizationPolicy
                {
                    RequireLoopbackClient = true,
                },
            RoutingHost = host,
        };

    private static IpAddressValue Loopback() =>
        new(
            NetworkAddressFamily.IPv4,
            0,
            0x7f000001);

    private static ResourceRef<TResource> Ref<TResource>(
        ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker =>
        new(metadata.Id, metadata.Scope, metadata.Generation);

    private sealed class EndpointConformanceEngineProbe :
        ILocalEngineProbe
    {
        public ValueTask<LocalEngineObservation> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new LocalEngineObservation(
                    "/test/docker.sock",
                    "28.0.0",
                    "1.48",
                    "linux",
                    "arm64",
                    "sha256:endpoint-conformance",
                    IsRootless: false));
        }
    }
}
