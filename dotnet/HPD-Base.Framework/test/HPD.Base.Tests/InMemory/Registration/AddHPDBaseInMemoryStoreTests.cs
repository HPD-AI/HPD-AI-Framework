namespace HPD.Base.Tests.InMemory.Registration;

public sealed class AddHPDBaseInMemoryStoreTests
{
    [Fact]
    public void RegistersSameSingletonForStoreInterfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseInMemoryStore(options => options.StoreId = "primary");
        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<InMemoryRecordStore>();

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
        services.AddHPDBaseInMemoryStore(options =>
        {
            options.StoreId = "primary";
            options.CollectionIds = ["items"];
        });
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IRecordStoreRegistry>();
        registry.AddHPDBaseInMemoryStore(provider);

        registry.GetStore("primary").Should().BeSameAs(provider.GetRequiredService<InMemoryRecordStore>());
        registry.GetStoreForCollection("items").Should().BeSameAs(provider.GetRequiredService<InMemoryRecordStore>());
    }
}
