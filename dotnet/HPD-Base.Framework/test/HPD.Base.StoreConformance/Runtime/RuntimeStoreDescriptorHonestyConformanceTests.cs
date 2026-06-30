using HPD.Base.Descriptors;
using HPD.Base.Runtime.Descriptors;

namespace HPD.Base.StoreConformance.Runtime;

public abstract class RuntimeStoreDescriptorHonestyConformanceTests<TFixture> : RuntimeStoreRegistrationConformanceTests<TFixture>
    where TFixture : IRuntimeStoreConformanceFixture, new()
{
    [Fact]
    public async Task RuntimeDescriptorSnapshotIsValidAndDoesNotOverclaimStoreCapabilities()
    {
        var services = await Fixture.CreateRuntimeServicesAsync();
        var registry = Required<IBaseDescriptorRegistry>(services);

        var snapshot = await registry.RebuildAsync();

        Assert.True(snapshot.Validation.Succeeded);
        var features = snapshot.Capabilities.Families
            .SelectMany(family => family.Features ?? [])
            .Where(feature => feature.Status == CapabilityStatus.Available)
            .ToArray();

        if (features.Any(feature => feature.Constraints?.StoreStreaming is not null))
        {
            Assert.True(Capabilities.Streaming?.Supported == true);
        }

        foreach (var feature in features.Where(feature => feature.Constraints?.StoreRevision is not null))
        {
            Assert.True(Capabilities.Revision?.Supported == true);
            var revision = feature.Constraints!.StoreRevision!;
            if (revision.Patch)
            {
                Assert.True(Capabilities.Revision!.Patch);
            }

            if (revision.Delete)
            {
                Assert.True(Capabilities.Revision!.Delete);
                Assert.True(Capabilities.Revision.Guarantee is RevisionGuarantee.Store or RevisionGuarantee.Native);
            }
        }

        foreach (var feature in features.Where(feature => feature.Constraints?.StoreCrud is not null))
        {
            var operations = feature.Constraints!.StoreCrud!.Operations ?? [];
            Assert.All(operations, operation =>
            {
                Assert.True(operation switch
                {
                    "list" => Capabilities.Crud.List,
                    "get" => Capabilities.Crud.Get,
                    "create" => Capabilities.Crud.Create,
                    "patch" => Capabilities.Crud.Patch,
                    "replace" => Capabilities.Crud.Replace,
                    "delete" => Capabilities.Crud.Delete,
                    _ => false
                }, $"Runtime descriptor claimed unsupported CRUD operation '{operation}'.");
            });
        }
    }
}
