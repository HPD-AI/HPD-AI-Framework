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

        var limits = snapshot.Capabilities.Families
            .Single(family => family.FamilyId == "base.realtime")
            .Limits!
            .Select(limit => limit.Name)
            .ToArray();

        limits.Should().Contain(
            "outboundCapacity",
            "receiveIdleTimeoutSeconds",
            "sendTimeoutSeconds",
            "maxJoinsPerSecond");
        limits.Should().NotContain(
            "heartbeatIntervalSeconds",
            "heartbeatTimeoutSeconds",
            "maxEventsPerSecond");

        snapshot.Manifest.DtoContracts!
            .Where(dto => dto.Id.StartsWith("base.realtime", StringComparison.Ordinal))
            .Select(dto => dto.Id)
            .Should().NotContain(
                "base.realtime.subscribeRequest",
                "base.realtime.snapshotOptions",
                "base.realtime.connectionDescriptor",
                "base.realtime.channelDescriptor");
    }

    [Fact]
    public async Task DescriptorAdvertisesConfiguredDurabilityAsProviderConditional()
    {
        await using var provider = await TestServices.CreateAsync(
            configureRealtime: options =>
                options.CursorProtectionKey = "test-only-cursor-signing-key-32-bytes-minimum");
        var snapshot = provider.GetRequiredService<IBaseDescriptorRegistry>().Current;
        var family = snapshot.Capabilities.Families
            .Single(item => item.FamilyId == "base.realtime");
        var durable = family.Features!
            .Single(item => item.FeatureId == BaseRealtimeFeatureIds.DurableReplay);
        var extensions = durable.Constraints!.Realtime!.Extensions!;

        durable.Status.Should().Be(CapabilityStatus.Available);
        extensions["durable"].GetBoolean().Should().BeTrue();
        extensions["replayable"].GetBoolean().Should().BeTrue();
        extensions["resumable"].GetBoolean().Should().BeTrue();
        extensions["durableRequiresTransactionalJournal"].GetBoolean().Should().BeTrue();
        family.Limits!.Select(limit => limit.Name).Should().Contain(
            "replayBatchSize",
            "cursorLifetimeSeconds");
    }
}
