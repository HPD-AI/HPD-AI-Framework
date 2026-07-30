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

        foreach (var feature in features.Where(feature => feature.Constraints?.StoreRead is not null))
        {
            var operations = feature.Constraints!.StoreRead!.Operations ?? [];
            Assert.All(operations, operation =>
            {
                Assert.True(operation switch
                {
                    "list" => Capabilities.Read.List,
                    "get" => Capabilities.Read.Get,
                    _ => false
                }, $"Runtime descriptor claimed unsupported read operation '{operation}'.");
            });
        }

        foreach (var feature in features.Where(feature => feature.Constraints?.StoreMutation is not null))
        {
            var operations = feature.Constraints!.StoreMutation!.Operations ?? [];
            Assert.All(operations, operation =>
            {
                Assert.True(operation switch
                {
                    "create" => Capabilities.Mutation.Create,
                    "patch" => Capabilities.Mutation.Patch,
                    "replace" => Capabilities.Mutation.Replace,
                    "delete" => Capabilities.Mutation.Delete,
                    _ => false
                }, $"Runtime descriptor claimed unsupported mutation operation '{operation}'.");
            });
        }
    }
}
