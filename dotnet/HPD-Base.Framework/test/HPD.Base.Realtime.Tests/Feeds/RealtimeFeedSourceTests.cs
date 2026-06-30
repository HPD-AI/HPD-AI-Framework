namespace HPD.Base.Realtime.Tests.Feeds;

public sealed class RealtimeFeedSourceTests
{
    [Fact]
    public async Task OpensRecordMutationStreamAndProjectsMatchingEvents()
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal("tenant-a"),
            Operation = TestServices.Operation(tenantId: "tenant-a"),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                TenantId = "tenant-a",
                IncludeSnapshots = true
            }
        });

        opened.Succeeded.Should().BeTrue();
        opened.Value!.Descriptor.Replayable.Should().BeFalse();
        opened.Value.Descriptor.Resumable.Should().BeFalse();
        opened.Value.Descriptor.DeliveryGuarantee.Should().Be(AsyncStreamDeliveryGuarantee.AtMostOnce);

        var publisher = provider.GetRequiredService<IEventPublisher>();
        await publisher.EmitAsync(TestServices.Event(collectionId: "other", tenantId: "tenant-a"));
        await publisher.EmitAsync(TestServices.Event(collectionId: "items", tenantId: "tenant-a"));

        var realtimeEvent = await ReadOneAsync(opened.Value.Items);
        realtimeEvent.Resource.CollectionId.Should().Be("items");
        realtimeEvent.After.Should().NotBeNull();
        realtimeEvent.Before.Should().BeNull();
    }

    [Fact]
    public async Task TenantMismatchIsSkippedPerEvent()
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal("tenant-a"),
            Operation = TestServices.Operation(tenantId: "tenant-a"),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items"
            }
        }, cts.Token);

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items", tenantId: "tenant-b"));

        var enumerator = opened.Value!.Items.GetAsyncEnumerator(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        provider.GetRequiredService<BaseRealtimeStats>().PolicySkips.Should().Be(1);
    }

    [Fact]
    public async Task SnapshotProjectionRedactsSubscriberUnsafeFields()
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal(),
            Operation = TestServices.Operation(),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                IncludeSnapshots = true
            }
        });

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items") with
        {
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = TestServices.Payload(("title", "hello"), ("secret", "shh")),
                Metadata = new RecordMetadata()
            }
        });

        var realtimeEvent = await ReadOneAsync(opened.Value!.Items);
        realtimeEvent.After!.Payload!.Json.EnumerateObject().Select(property => property.Name)
            .Should().Equal("title");
        realtimeEvent.After.Redacted.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeSnapshotRequiresAdminEvenWhenRequested()
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal(),
            Operation = TestServices.Operation(),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                IncludeSnapshots = true,
                IncludeBefore = true
            }
        });

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items") with
        {
            Before = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = TestServices.Payload(("title", "old")),
                Metadata = new RecordMetadata()
            }
        });

        var realtimeEvent = await ReadOneAsync(opened.Value!.Items);
        realtimeEvent.Before.Should().BeNull();
    }

    [Fact]
    public async Task AdminCanReceiveBeforeSnapshotWhenExplicitlyRequested()
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal(state: PrincipalAuthenticationState.Admin),
            Operation = TestServices.Operation(),
            Join = new BaseRealtimeChannelJoinRequest
            {
                Kind = BaseRealtimeChannelKinds.RecordChanges,
                CollectionId = "items",
                IncludeSnapshots = true,
                IncludeBefore = true
            }
        });

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items") with
        {
            Before = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = TestServices.Payload(("title", "old")),
                Metadata = new RecordMetadata()
            }
        });

        var realtimeEvent = await ReadOneAsync(opened.Value!.Items);
        realtimeEvent.Before.Should().NotBeNull();
    }

    private static async Task<BaseRealtimeEvent> ReadOneAsync(IAsyncEnumerable<BaseRealtimeEvent> items)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var enumerator = items.GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        return enumerator.Current;
    }
}
