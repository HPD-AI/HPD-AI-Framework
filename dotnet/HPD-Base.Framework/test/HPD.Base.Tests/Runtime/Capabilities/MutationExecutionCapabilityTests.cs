using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Capabilities;

public sealed class MutationExecutionCapabilityTests
{
    [Fact]
    public async Task RuntimeAdvertisesExactL30MutationExecutionVocabularyWithoutAStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime();

        await using var provider = services.BuildServiceProvider();
        var snapshot = await provider
            .GetRequiredService<IBaseDescriptorRegistry>()
            .RebuildAsync();

        var family = snapshot.Capabilities.Families
            .Single(candidate => candidate.FamilyId == BaseCapabilityFamilies.Batch);

        family.OwnerModuleId.Should().BeNull();
        family.Status.Should().Be(CapabilityStatus.Available);
        family.Features.Should().NotBeNull();
        family.Features!.Select(feature => feature.FeatureId).Should().Equal(
            BaseFeatureIds.RecordsBatch,
            BaseFeatureIds.RecordsUpsert,
            BaseFeatureIds.BatchOrderedIndependent,
            BaseFeatureIds.BatchOrderedStopOnFailure,
            BaseFeatureIds.BatchAtomic,
            BaseFeatureIds.BatchPartialResults);
        family.Features.Should().OnlyContain(feature =>
            feature.Status == CapabilityStatus.Available
            && feature.Scope == CapabilityScope.Runtime
            && feature.Visibility == VisibilityLevel.Public);
        snapshot.Validation.Succeeded.Should().BeTrue();
    }
}
