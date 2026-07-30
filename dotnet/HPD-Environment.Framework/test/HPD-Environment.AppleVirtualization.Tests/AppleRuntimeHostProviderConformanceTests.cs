using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;
using HPD.Environment.Runtime;

namespace HPD.Environment.AppleVirtualization.Tests;

public sealed class AppleRuntimeHostProviderConformanceTests
    : RuntimeHostProviderConformanceTests
{
    protected override RuntimeHostProviderConformanceFixture CreateFixture()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new AppleVirtualizationProviderModule(
            new AppleVirtualizationProviderOptions
            {
                HelperTransportMode =
                    AppleVirtualizationHelperTransportMode.InMemoryFake,
                FeatureGates = new AppleVirtualizationProviderFeatureGates
                {
                    EnableInMemoryFakeHelper = true,
                },
            },
            helper,
            new AppleVirtualizationProviderStateLedger(),
            hostPlatformOverride: new PlatformSpec("macos", "arm64")));

        return new RuntimeHostProviderConformanceFixture(
            registry.RuntimeHostProviders.Single(),
            new ResourceMetadata<RuntimeHost>
            {
                Id = new ResourceId<RuntimeHost>("conformance-host"),
                Kind = new ResourceKind("RuntimeHost"),
                Scope = new ResourceScope("conformance"),
                Generation = new ResourceGeneration(7),
                SchemaVersion = new SchemaVersion("1"),
            },
            AppleVirtualizationContractFixtures.RuntimeHostSpec() with
            {
                PreferredProvider =
                    AppleVirtualizationProviderDescriptor.ProviderId,
            },
            prepareEnsure: () => EnqueueReadyHostFlow(helper),
            prepareStop: () => helper.EnqueueResponse(HostResponse(
                AppleVirtualizationHelperOperation.HostRequestStop,
                RuntimeHostPhase.Stopped,
                ResourcePhase.Ready)),
            prepareDelete: () => helper.EnqueueResponse(HostResponse(
                AppleVirtualizationHelperOperation.HostDelete,
                RuntimeHostPhase.Deleted,
                ResourcePhase.Deleted)),
            observedMutationCount: () => helper.Requests.Count);
    }

    private static void EnqueueReadyHostFlow(
        FakeAppleVirtualizationHelperClient helper)
    {
        helper.EnqueueResponse(HostResponse(
            AppleVirtualizationHelperOperation.HostEnsure,
            RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(
            AppleVirtualizationHelperOperation.HostStart,
            RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(
            AppleVirtualizationHelperOperation.HostStatus,
            RuntimeHostPhase.Running,
            ResourcePhase.Ready));
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation =
                AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema =
                AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema,
            GuestAgentReadinessProbeResponse =
                new AppleVirtualizationGuestAgentReadinessProbeResponse
                {
                    HostId = "conformance-host",
                    State =
                        AppleVirtualizationGuestAgentReadinessState.Ready,
                    VerifiedReady = true,
                    TransportConnected = true,
                    ProtocolVersion =
                        AppleVirtualizationHelperProtocol.CurrentVersion,
                    AgentVersion = "conformance",
                    GuestBootId = "conformance-boot",
                    GuestBootGeneration = 1,
                    GuestAgentGeneration = 1,
                    Capabilities = new AppleVirtualizationGuestAgentCapabilities
                    {
                        ProjectionMount = true,
                        ProcessStart = true,
                        ProcessReadOutput = true,
                    },
                },
        });
    }

    private static AppleVirtualizationHelperEnvelope HostResponse(
        AppleVirtualizationHelperOperation operation,
        RuntimeHostPhase phase,
        ResourcePhase? resourcePhase = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "conformance-response",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema =
                AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "conformance-host",
                HostPhase = phase,
                Phase = resourcePhase ?? ResourcePhase.Ready,
                GuestControlReachable =
                    phase is RuntimeHostPhase.Running or RuntimeHostPhase.Ready,
            },
        };
}
