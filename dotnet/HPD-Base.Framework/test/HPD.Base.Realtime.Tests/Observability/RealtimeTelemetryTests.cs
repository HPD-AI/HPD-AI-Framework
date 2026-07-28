using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Realtime.Tests.Observability;

public sealed class RealtimeTelemetryTests
{
    [Fact]
    public async Task FeedOpenAndProjectionTelemetryDoNotLeakRealtimeIdentityMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.Realtime);
        using var metrics = new MeterCollector(HPDBaseMeterNames.Realtime);
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();

        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "channel-secret",
            Principal = TestServices.Principal("tenant-secret"),
            Operation = TestServices.Operation(tenantId: "tenant-secret") with { CorrelationId = "corr-secret" },
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                TenantId = "tenant-secret",
                RecordId = "record-secret",
                IncludeSnapshots = true
            }
        });

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items", recordId: "record-secret", tenantId: "tenant-secret") with
        {
            EventId = "event-secret",
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("record-secret"),
                Payload = TestServices.Payload(("title", "payload-secret")),
                Metadata = new RecordMetadata()
            }
        });
        _ = await ReadOneAsync(opened.Value!.Items);
        var stats = provider.GetRequiredService<BaseRealtimeStats>();
        stats.RecordStreamOpenFailure();
        stats.RecordJoinRateRejection();
        stats.RecordSlowConsumerTermination();

        opened.Succeeded.Should().BeTrue();
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.RealtimeChannelJoin);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeChannelsOpened);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeEventsProjected);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeStreamOpenFailures);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeJoinRateRejections);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeSlowConsumerTerminations);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.RealtimeJoinDuration);

        var forbidden = new[] { "channel-secret", "tenant-secret", "record-secret", "event-secret", "payload-secret", "corr-secret" };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task FeedOpenWorksWithoutConfiguredTelemetryListeners()
    {
        await using var provider = await TestServices.CreateAsync();

        var opened = await provider.GetRequiredService<IBaseRealtimeFeedSource>().OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "channel-secret",
            Principal = TestServices.Principal("tenant-secret"),
            Operation = TestServices.Operation(tenantId: "tenant-secret"),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                TenantId = "tenant-secret"
            }
        });

        opened.Succeeded.Should().BeTrue();
        opened.Value.Should().NotBeNull();
    }

    private static async Task<BaseRealtimeEvent> ReadOneAsync(IAsyncEnumerable<BaseRealtimeEvent> items)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var enumerator = items.GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        return enumerator.Current;
    }

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

}
