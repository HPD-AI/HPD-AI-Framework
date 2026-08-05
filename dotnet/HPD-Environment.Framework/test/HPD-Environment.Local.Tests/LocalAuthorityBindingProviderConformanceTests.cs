using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;
using HPD.Environment.Runtime;

namespace HPD.Environment.Local.Tests;

public sealed class LocalAuthorityBindingProviderConformanceTests
    : AuthorityBindingProviderConformanceTests
{
    protected override async ValueTask<
        AuthorityBindingProviderConformanceFixture>
        CreateAuthorityFixtureAsync()
    {
        var probe = new ConformanceEngineProbe();
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
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(
                new EngineControlPlaneSpec
                {
                    Kind =
                        EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode =
                        EngineAuthorityMode.Rootful,
                    ImageStore =
                        EngineImageStoreMode.EngineLocal,
                    Host = Ref(host.Metadata),
                    EndpointPolicy = new SensitiveEndpointPolicy
                    {
                        Kind =
                            SensitiveEndpointKind.EngineSocket,
                        AuthorityClass =
                            SensitiveAuthorityClass
                                .RootfulEngineControl,
                        Redaction =
                            SensitiveRedactionLevel
                                .RedactIdentifiers,
                        RequireAudit = true,
                    },
                });
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey(
                            "authority-conformance"),
                });
        EngineAuthorityBindingPlan plan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath = new UnixSocketPath(
                        "/run/hpd/engine/docker.sock"),
                    Provenance = new SensitiveProvenance(
                        "conformance",
                        "authority lifecycle"),
                });
        Assert.True(plan.Accepted);
        Assert.NotNull(plan.Spec);
        var metadata = new ResourceMetadata<AuthorityBinding>
        {
            Id = new ResourceId<AuthorityBinding>(
                "authority-conformance"),
            Kind = new ResourceKind("AuthorityBinding"),
            Scope = host.Metadata.Scope,
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };
        return new(
            registry.AuthorityBindingProviders.Single(),
            metadata,
            plan.Spec,
            plan.Spec with
            {
                AuditLabel =
                    "conflicting-authority-conformance",
            },
            plan.Spec with
            {
                Policy = plan.Spec.Policy with
                {
                    Lease = plan.Spec.Policy.Lease with
                    {
                        ExpiresAfter = TimeSpan.Zero,
                    },
                },
            },
            advancePastExpiry: null,
            observedMutationCount: () => module
                .GetAuthorityAuditEvents(metadata.Id.Value)
                .Length);
    }

    private static ResourceRef<TResource> Ref<TResource>(
        ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker =>
        new(metadata.Id, metadata.Scope, metadata.Generation);

    private sealed class ConformanceEngineProbe :
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
                    "sha256:conformance-engine",
                    IsRootless: false));
        }
    }
}
