namespace HPD.Base.Tests.InMemory.Configuration;

public sealed class HPDBaseInMemoryStoreOptionsTests
{
    [Fact]
    public void DefaultsMatchContract()
    {
        var options = new HPDBaseInMemoryStoreOptions();

        options.StoreId.Should().Be(HPDBaseInMemoryDefaults.DefaultStoreId);
        options.ModuleId.Should().Be(HPDBaseInMemoryDefaults.DefaultModuleId);
        options.ModuleName.Should().Be(HPDBaseInMemoryDefaults.DefaultModuleName);
        options.StoreVersion.Should().Be(HPDBaseInMemoryDefaults.DefaultStoreVersion);
        options.CollectionIds.Should().BeEmpty();
        options.DefaultPageSize.Should().Be(100);
        options.MaxPageSize.Should().Be(1_000);
        options.AllowClientRequestedIds.Should().BeTrue();
        options.EnableStreamingCapability.Should().BeTrue();
    }

    [Fact]
    public void CollectionIdsAndCollectionsMustAgreeWhenBothConfigured()
    {
        var options = new HPDBaseInMemoryStoreOptions
        {
            CollectionIds = ["items"],
            Collections = [new CollectionDefinition
            {
                Id = "other",
                Name = "other",
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve
            }]
        };

        var act = () => new InMemoryRecordStore(options);

        act.Should().Throw<ArgumentException>();
    }
}
