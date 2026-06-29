using HPD.Base.InMemory.Tests.TestDoubles;

namespace HPD.Base.InMemory.Tests.Runtime;

public sealed class RuntimeIntegrationTests
{
    [Fact]
    public async Task RuntimeCanUseInMemoryStoreForCrudAndExpectedRevisionDelete()
    {
        using var provider = BuildRuntime();
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();

        var create = await runtime.CreateAsync(
            "items",
            new RecordCreateRequest { Payload = InMemoryTestData.Payload(("title", "hello")) },
            InMemoryTestData.Principal,
            InMemoryTestData.Operation(BaseOperationKind.Create));
        create.Status.Should().Be(OperationStatus.Created);

        var patch = await runtime.PatchAsync(
            "items",
            create.Value!.Id,
            new RecordPatchRequest
            {
                Patch = InMemoryTestData.Patch("title", "patched"),
                ExpectedRevision = create.Value.Metadata.Revision
            },
            InMemoryTestData.Principal,
            InMemoryTestData.Operation(BaseOperationKind.Patch));
        patch.Status.Should().Be(OperationStatus.Updated);

        var delete = await runtime.DeleteAsync(
            "items",
            create.Value.Id,
            new RecordDeleteRequest
            {
                ExpectedRevision = patch.Value!.Metadata.Revision,
                ReturnPrevious = true
            },
            InMemoryTestData.Principal,
            InMemoryTestData.Operation(BaseOperationKind.Delete));

        delete.Status.Should().Be(OperationStatus.Deleted);
        delete.Value!.Previous.Should().NotBeNull();
    }

    [Fact]
    public async Task DescriptorContributionAddsModuleCapabilitiesAndCollection()
    {
        using var provider = BuildRuntime();

        var snapshot = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

        snapshot.Validation.Succeeded.Should().BeTrue();
        snapshot.Manifest.Modules.Should().Contain(module => module.Id == "hpd.base.inmemory");
        snapshot.Schema.Collections.Should().Contain(collection => collection.Id == "items");
        snapshot.Capabilities.Families.SelectMany(family => family.Features ?? [])
            .Should()
            .Contain(feature => feature.FeatureId == BaseFeatureIds.RecordsDelete);
    }

    private static ServiceProvider BuildRuntime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        services.AddHPDBaseRuntime()
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = ["items"];
                options.Collections = [InMemoryTestData.Collection()];
            });

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(provider);
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        return provider;
    }
}
