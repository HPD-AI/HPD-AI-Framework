namespace HPD.Base.Tests.Volatile.Registration;

public sealed class AddHPDBaseVolatileStoreTests
{
    [Fact]
    public void RegistersSameSingletonForStoreInterfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseVolatileStore(options => options.StoreId = "primary");
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<VolatileRecordStore>();

        provider.GetRequiredService<IRecordStore>().Should().BeSameAs(concrete);
        provider.GetRequiredService<IRecordMutationStore>().Should().BeSameAs(concrete);
        provider.GetRequiredService<IAtomicRecordStore>().Should().BeSameAs(concrete);
        provider.GetRequiredService<IStreamingRecordStore>().Should().BeSameAs(concrete);
        concrete.Capabilities.StoreId.Should().Be("primary");
    }

    [Fact]
    public void ExplicitRegistryExtensionAddsConfiguredRegistration()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        services.AddHPDBaseVolatileStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
        });
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IRecordStoreRegistry>();
        registry.AddHPDBaseVolatileStore(provider);

        registry.GetStore("primary").Should().BeSameAs(provider.GetRequiredService<VolatileRecordStore>());
        registry.GetStoreForCollection("items").Should().BeSameAs(provider.GetRequiredService<VolatileRecordStore>());
    }
}
