namespace HPD.Base.Realtime.Tests.Descriptors;

public sealed class RealtimeDescriptorTests
{
    [Fact]
    public async Task DescriptorAdvertisesWebSocketEventFeedsWithoutReplayOrLiveQuery()
    {
        await using var provider = await TestServices.CreateAsync();
        var snapshot = provider.GetRequiredService<IBaseDescriptorRegistry>().Current;

        snapshot.Manifest.Modules.Should().Contain(module => module.Id == BaseRealtimeModuleIds.Module && module.Kind == BaseModuleKind.Realtime);
        snapshot.Capabilities.Families.Should().Contain(family => family.FamilyId == "base.realtime");

        var feature = snapshot.Capabilities.Families
            .Single(family => family.FamilyId == "base.realtime")
            .Features!
            .Single(item => item.FeatureId == BaseRealtimeFeatureIds.RecordChanges);

        feature.Constraints!.Realtime!.Subscribe.Should().BeTrue();
        feature.Constraints.Realtime.Extensions!["replayable"].GetBoolean().Should().BeFalse();
        feature.Constraints.Realtime.Extensions!["resumable"].GetBoolean().Should().BeFalse();
        feature.Constraints.Realtime.Extensions!["liveQuery"].GetBoolean().Should().BeFalse();
        feature.RouteRefs.Should().Contain(BaseRealtimeRouteIds.WebSocket);
    }
}
