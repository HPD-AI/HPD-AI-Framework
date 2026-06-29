namespace HPD.Base.StoreConformance.Crud;

/// <summary>
/// Minimal reusable CRUD contract checks shared by store providers.
/// </summary>
public abstract class RecordStoreCrudConformanceTests
{
    private readonly StoreConformanceFixture _fixture;

    protected RecordStoreCrudConformanceTests(StoreConformanceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void StoreAdvertisesAStableStoreId()
    {
        var store = _fixture.CreateStore();

        Assert.False(string.IsNullOrWhiteSpace(store.Capabilities.StoreId));
        Assert.Equal(store.Capabilities.StoreId, store.Capabilities.StoreId);
    }
}
