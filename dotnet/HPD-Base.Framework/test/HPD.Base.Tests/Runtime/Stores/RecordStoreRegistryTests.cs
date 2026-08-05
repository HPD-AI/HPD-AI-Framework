using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Stores;

public sealed class RecordStoreRegistryTests
{
    [Fact]
    public void ResolvesStoresByIdAndCollection()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IRecordStoreRegistry>();
        var store = new FakeRecordStore("primary");

        registry.Add(new RecordStoreRegistration
        {
            StoreId = "primary",
            Store = store,
            CollectionIds = ["items"]
        });

        Assert.Same(store, registry.GetStore("primary"));
        Assert.Same(store, registry.GetStoreForCollection("items"));
        Assert.Single(registry.GetRegistrations());
    }
}
