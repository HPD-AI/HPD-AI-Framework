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

    [Fact]
    public async Task Runtime_reset_removes_only_provider_owned_disposable_allocations()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-reset-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(new LocalEnvironmentProviderModule(
                new LocalEnvironmentProviderOptions
                {
                    EngineSocketPath = "/test/docker.sock",
                    WorkloadStateRoot = root,
                }));
            IRuntimeHostProvider hostProvider =
                registry.RuntimeHostProviders.Single();
            IRuntimeHostResetProvider resetProvider =
                registry.RuntimeHostResetProviders.Single();
            var metadata = new ResourceMetadata<RuntimeHost>
            {
                Id = new ResourceId<RuntimeHost>("reset-host"),
                Kind = new ResourceKind("RuntimeHost"),
                Scope = new ResourceScope("reset"),
                Generation = new ResourceGeneration(1),
                SchemaVersion = new SchemaVersion("1"),
            };
            var spec = new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            };
            RuntimeHostStatus ready = await hostProvider.EnsureAsync(
                metadata,
                spec,
                observed: null);
            RuntimeHostStatus stopped = await hostProvider.StopAsync(
                ready.Handle!.Value,
                StopPolicy.Default);
            string allocation = Path.Combine(root, "allocations", "owned");
            string durable = Path.Combine(root, "storage", "volumes", "keep");
            Directory.CreateDirectory(allocation);
            Directory.CreateDirectory(durable);
            await File.WriteAllTextAsync(
                Path.Combine(allocation, "cache"),
                "disposable");
            await File.WriteAllTextAsync(
                Path.Combine(durable, "data"),
                "durable");

            RuntimeHostResetResult result = await resetProvider.ResetAsync(
                stopped.Handle!.Value,
                new RuntimeHostResetRequest(
                    RuntimeHostResetScope.RuntimeState,
                    RetainResourceIdentity: true,
                    RetainUserData: true));

            Assert.Equal(metadata.Id, result.Host.Id);
            Assert.False(Directory.Exists(allocation));
            Assert.Equal(
                "durable",
                await File.ReadAllTextAsync(
                    Path.Combine(durable, "data")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
