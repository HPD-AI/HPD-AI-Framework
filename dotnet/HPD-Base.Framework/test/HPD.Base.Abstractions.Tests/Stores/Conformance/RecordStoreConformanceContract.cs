using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Abstractions.Tests.Stores.Conformance;

public abstract class RecordStoreConformanceContract
{
    protected abstract IRecordStore CreateStore();

    protected virtual CollectionDefinition CreateCollection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    [Fact]
    public void StoreAdvertisesCapabilities()
    {
        var store = CreateStore();

        Assert.NotNull(store.Capabilities);
        Assert.False(string.IsNullOrWhiteSpace(store.Capabilities.StoreId));
    }
}
