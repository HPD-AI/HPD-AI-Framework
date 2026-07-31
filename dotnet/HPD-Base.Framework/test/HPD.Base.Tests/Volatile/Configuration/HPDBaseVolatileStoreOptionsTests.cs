namespace HPD.Base.Tests.Volatile.Configuration;

public sealed class HPDBaseVolatileStoreOptionsTests
{
    [Fact]
    public void DefaultsMatchContract()
    {
        var options = new HPDBaseVolatileStoreOptions();

        options.StoreId.Should().Be(HPDBaseVolatileDefaults.DefaultStoreId);
        options.ModuleId.Should().Be(HPDBaseVolatileDefaults.DefaultModuleId);
        options.ModuleName.Should().Be(HPDBaseVolatileDefaults.DefaultModuleName);
        options.StoreVersion.Should().Be(HPDBaseVolatileDefaults.DefaultStoreVersion);
        options.CollectionIds.Should().BeEmpty();
        options.DefaultPageSize.Should().Be(100);
        options.MaxPageSize.Should().Be(1_000);
        options.AllowClientRequestedIds.Should().BeTrue();
        options.EnableStreamingCapability.Should().BeTrue();
    }

    [Fact]
    public void CollectionIdsAndCollectionsMustAgreeWhenBothConfigured()
    {
        var options = new HPDBaseVolatileStoreOptions
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

        var act = () => new VolatileRecordStore(options);

        act.Should().Throw<ArgumentException>();
    }
}
