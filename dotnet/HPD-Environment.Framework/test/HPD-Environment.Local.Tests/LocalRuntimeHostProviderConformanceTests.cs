using HPD.Environment.Contracts;
using HPD.Environment.ProviderConformance;
using HPD.Environment.Runtime;

namespace HPD.Environment.Local.Tests;

public sealed class LocalRuntimeHostProviderConformanceTests
    : RuntimeHostProviderConformanceTests
{
    protected override RuntimeHostProviderConformanceFixture CreateFixture()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            }));

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
            new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
    }
}
