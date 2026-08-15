using HPD.Base.Tests.InMemory.TestDoubles;

namespace HPD.Base.Tests.InMemory.Runtime;

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

    [Fact]
    public async Task AdministrativePurgeDeletesAtomicallyAndAdvancesGenerationOnce()
    {
        using var provider = BuildRuntime(PurgeCollection());
        var runtime = provider.GetRequiredService<IBaseRecordRuntime>();
        var first = await runtime.CreateAsync("history", new RecordCreateRequest
        {
            RequestedId = new RecordId("first"),
            Payload = InMemoryTestData.Payload(("title", "first")),
        }, InMemoryTestData.Principal, InMemoryTestData.Operation(BaseOperationKind.Create) with { CollectionId = "history" });
        first.IsSuccess().Should().BeTrue();

        OperationResult<BasePurgeResult> purge = await provider.GetRequiredService<IBaseMutationCoordinator>().ExecutePurgeAsync(
            PurgeRequest([new RecordId("first"), new RecordId("missing")]),
            CancellationToken.None);

        purge.Status.Should().Be(OperationStatus.Ok);
        purge.Value.Should().BeEquivalentTo(new
        {
            CollectionId = "history",
            RequestedCount = 2,
            PurgedCount = 1,
            PurgeGeneration = 1L,
        });
        (await runtime.GetAsync("history", new RecordId("first"), InMemoryTestData.Principal,
            InMemoryTestData.Operation(BaseOperationKind.Get) with { CollectionId = "history", RecordId = "first" }))
            .Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task MissingOnlyPurgeStillAdvancesGenerationAndCasMismatchChangesNothing()
    {
        using var provider = BuildRuntime(PurgeCollection());
        IBaseMutationCoordinator coordinator = provider.GetRequiredService<IBaseMutationCoordinator>();
        (await coordinator.ExecutePurgeAsync(PurgeRequest([new RecordId("missing")]), CancellationToken.None))
            .Value!.PurgeGeneration.Should().Be(1);

        OperationResult<BasePurgeResult> conflict = await coordinator.ExecutePurgeAsync(
            PurgeRequest([new RecordId("still-missing")]) with { ExpectedPurgeGeneration = 0 },
            CancellationToken.None);
        conflict.Status.Should().Be(OperationStatus.Conflict);
        conflict.Error!.Code.Should().Be(BaseCollectionErrorCodes.PurgeGenerationConflict);
    }

    private static ServiceProvider BuildRuntime(CollectionDefinition? collection = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime()
            .UseTestPolicyAuthority(new AllowPolicyEvaluator())
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = [collection?.Id ?? "items"];
                options.Collections = [collection ?? InMemoryTestData.Collection()];
            });

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(provider);
        provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync().AsTask().GetAwaiter().GetResult();
        return provider;
    }

    private static CollectionDefinition PurgeCollection() => InMemoryTestData.Collection() with
    {
        Id = "history",
        MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
    };

    private static BasePurgeRequest PurgeRequest(RecordId[] ids) => new()
    {
        CollectionId = "history",
        RecordIds = ids,
        Principal = InMemoryTestData.Principal,
        ReasonCode = "retention-expired",
        AuditReference = "audit-1",
        EvaluatedAt = DateTimeOffset.UtcNow,
    };
}
