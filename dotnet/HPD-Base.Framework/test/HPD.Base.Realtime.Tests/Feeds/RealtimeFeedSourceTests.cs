namespace HPD.Base.Realtime.Tests.Feeds;

public sealed class RealtimeFeedSourceTests
{
    [Fact]
    public async Task DefaultInboxNeverBlocksAsyncMutationPublication()
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
                CollectionId = "items"
            }
        });

        opened.Value!.Descriptor.Backpressure.Should()
            .Be(AsyncStreamBackpressureMode.DropOldest);

        var publisher = provider.GetRequiredService<IEventPublisher>();
        for (var index = 0; index < 1025; index++)
        {
            var publication = publisher.EmitAsync(TestServices.Event(recordId: index.ToString()));
            publication.IsCompletedSuccessfully.Should().BeTrue();
            await publication;
        }
    }

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
    public async Task EventVisibilityCannotBeWidenedByTheJoinRequest()
    {
        await using var provider = await TestServices.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var opened = await provider.GetRequiredService<IBaseRealtimeFeedSource>().OpenAsync(
            new BaseRealtimeFeedRequest
            {
                Channel = "base:records:items",
                Principal = TestServices.Principal(),
                Operation = TestServices.Operation(),
                Join = new BaseRealtimeChannelJoinRequest
                {
                    Kind = BaseRealtimeChannelKinds.RecordChanges,
                    CollectionId = "items"
                }
            },
            cts.Token);

        await provider.GetRequiredService<IEventPublisher>().EmitAsync(
            TestServices.Event(collectionId: "items") with { Visibility = VisibilityLevel.Admin });

        await using var enumerator = opened.Value!.Items.GetAsyncEnumerator(cts.Token);
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

    [Theory]
    [InlineData(PrincipalAuthenticationState.Admin)]
    [InlineData(PrincipalAuthenticationState.System)]
    public async Task PrivilegedPrincipalReceivesIndependentlyRedactedSnapshotsWhenExplicitlyRequested(
        PrincipalAuthenticationState authenticationState)
    {
        await using var provider = await TestServices.CreateAsync();
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var opened = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "base:records:items",
            Principal = TestServices.Principal(state: authenticationState),
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
                Payload = TestServices.Payload(
                    ("title", "old"),
                    ("secret", "before-admin-visible"),
                    ("writeOnly", "before-forbidden")),
                Metadata = new RecordMetadata()
            },
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = TestServices.Payload(
                    ("title", "new"),
                    ("secret", "after-admin-visible"),
                    ("writeOnly", "after-forbidden")),
                Metadata = new RecordMetadata()
            }
        });

        var realtimeEvent = await ReadOneAsync(opened.Value!.Items);
        realtimeEvent.Before.Should().NotBeNull();
        realtimeEvent.After.Should().NotBeNull();
        realtimeEvent.Before!.Payload.Json.EnumerateObject().Select(property => property.Name)
            .Should().Equal("title", "secret");
        realtimeEvent.After!.Payload.Json.EnumerateObject().Select(property => property.Name)
            .Should().Equal("title", "secret");
        realtimeEvent.Before.Payload.Json.ToString().Should().NotContain("before-forbidden");
        realtimeEvent.After.Payload.Json.ToString().Should().NotContain("after-forbidden");
    }

    [Fact]
    public async Task ProjectionOmitsInternalDisclosureMetadataFromSerializedEvent()
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
                IncludeSnapshots = true
            }
        });

        using var extensionDocument = JsonDocument.Parse("\"extension-forbidden\"");
        await provider.GetRequiredService<IEventPublisher>().EmitAsync(TestServices.Event(collectionId: "items") with
        {
            TenantId = "tenant-forbidden",
            CorrelationId = "correlation-forbidden",
            CausationId = "causation-forbidden",
            ChangedFields = ["changed-field-forbidden"],
            Resource = new EventResource
            {
                Kind = EventResourceKind.Record,
                CollectionId = "items",
                RecordId = new RecordId("one"),
                ResourcePath = "resource-path-forbidden"
            },
            Principal = new EventPrincipalSummary
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "subject-forbidden",
                TenantId = "principal-tenant-forbidden",
                AuthSource = "auth-source-forbidden"
            },
            Extensions = new Dictionary<string, JsonElement>
            {
                ["extension-key-forbidden"] = extensionDocument.RootElement.Clone()
            },
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("one"),
                Payload = TestServices.Payload(("title", "safe")),
                Metadata = new RecordMetadata
                {
                    ETag = "etag-forbidden",
                    StoreId = "store-forbidden",
                    Tags = new Dictionary<string, string> { ["tag-key-forbidden"] = "tag-value-forbidden" }
                },
                IncludedFields = ["included-field-forbidden"]
            }
        });

        var realtimeEvent = await ReadOneAsync(opened.Value!.Items);
        var json = JsonSerializer.Serialize(
            realtimeEvent,
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeEvent);

        json.Should().NotContain("forbidden");
    }

    private static async Task<BaseRealtimeEvent> ReadOneAsync(IAsyncEnumerable<BaseRealtimeEvent> items)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var enumerator = items.GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        return enumerator.Current;
    }
}
