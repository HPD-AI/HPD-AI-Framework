namespace HPD.Base.StoreConformance.Descriptor;

public abstract class RecordStoreDescriptorConformanceTests<TFixture> : RecordStoreConformanceTestBase<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    [Fact]
    public async Task DescriptorBaselineIsStableAndCoherent()
    {
        var store = await CreateStoreAsync();
        var capabilities = store.Capabilities;

        Assert.NotNull(capabilities);
        Assert.False(string.IsNullOrWhiteSpace(capabilities.StoreId));
        Assert.False(string.IsNullOrWhiteSpace(capabilities.StoreKind));
        Assert.False(string.IsNullOrWhiteSpace(capabilities.StoreVersion));
        Assert.Equal(capabilities.StoreId, store.Capabilities.StoreId);
        Assert.NotNull(capabilities.Crud);
        Assert.NotNull(capabilities.Query);
        Assert.NotNull(capabilities.Query.Filter);
        Assert.NotNull(capabilities.Query.Sort);
        Assert.NotNull(capabilities.Query.Pagination);
        Assert.NotNull(capabilities.Query.Count);
        Assert.NotNull(capabilities.Query.Select);
        Assert.True(capabilities.Query.Pagination.DefaultLimit > 0);
        Assert.True(capabilities.Query.Pagination.MaxLimit > 0);
        Assert.True(capabilities.Query.Pagination.DefaultLimit <= capabilities.Query.Pagination.MaxLimit);

        if (capabilities.Revision is { } revision)
        {
            Assert.True(revision.Supported || (!revision.Patch && !revision.Delete && revision.Guarantee == RevisionGuarantee.None));
            Assert.True(revision.Supported || store is not IRevisionedRecordStore);
        }

        if (capabilities.Streaming is { } streaming)
        {
            Assert.True(streaming.MaxItems is null or > 0);
        }
    }
}
