namespace HPD.Base.Realtime.Tests.Feeds;

public sealed class RealtimeFeedSourceTests
{
    private const string CursorKey = "test-only-cursor-signing-key-32-bytes-minimum";

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

    [Fact]
    public async Task DurableChannelResumesAfterOpaqueCursorAndRedactsJournalPayload()
    {
        await using var provider = await TestServices.CreateAsync(
            configureRealtime: options =>
            {
                options.CursorSigningKey = CursorKey;
                options.Limits = options.Limits with { DurablePollIntervalMilliseconds = 1 };
            },
            journalEntries:
            [
                TestServices.JournalEntry(1, "one", "first"),
                TestServices.JournalEntry(2, "two", "second")
            ]);
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true,
            IncludeSnapshots = true
        };

        var initial = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "durable-items",
            Principal = TestServices.Principal(),
            Operation = TestServices.Operation(),
            Join = join
        });
        initial.Succeeded.Should().BeTrue();
        initial.Value!.Descriptor.Replayable.Should().BeTrue();
        initial.Value.Descriptor.Resumable.Should().BeTrue();
        initial.Value.Descriptor.DeliveryGuarantee.Should().Be(AsyncStreamDeliveryGuarantee.AtLeastOnce);
        initial.Value.Descriptor.Cursor.Should().NotBeNullOrWhiteSpace();

        var journal = (TestMutationJournalStore)provider
            .GetRequiredService<HPD.Base.Runtime.Stores.IRecordStoreRegistry>()
            .GetStoreForCollection("items")!;
        journal.Add(TestServices.JournalEntry(3, "three", "third"));

        var resumed = await feed.OpenAsync(new BaseRealtimeFeedRequest
        {
            Channel = "durable-items",
            Principal = TestServices.Principal(),
            Operation = TestServices.Operation(),
            Join = join with { ResumeCursor = initial.Value.Descriptor.Cursor }
        });
        var replayed = await ReadOneAsync(resumed.Value!.Items);

        replayed.EventId.Should().Be("event-3");
        replayed.Cursor.Should().NotBeNullOrWhiteSpace();
        replayed.After!.Payload!.Json.EnumerateObject().Select(property => property.Name)
            .Should().Equal("title");
    }

    [Fact]
    public async Task DurableCursorCannotMoveToAnotherScopeOrAcceptTampering()
    {
        await using var provider = await TestServices.CreateAsync(
            configureRealtime: options => options.CursorSigningKey = CursorKey,
            journalEntries: [TestServices.JournalEntry(1, "one", "first")]);
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true
        };
        var request = new BaseRealtimeFeedRequest
        {
            Channel = "durable-items",
            Principal = TestServices.Principal(),
            Operation = TestServices.Operation(),
            Join = join
        };
        var opened = await feed.OpenAsync(request);
        var cursor = opened.Value!.Descriptor.Cursor!;

        var mismatched = await feed.OpenAsync(request with
        {
            Join = join with { RecordId = "one", ResumeCursor = cursor }
        });
        mismatched.Succeeded.Should().BeFalse();
        mismatched.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CursorScopeMismatch);

        var tamperedCursor = cursor[..^1] + (cursor[^1] == 'A' ? "B" : "A");
        var tampered = await feed.OpenAsync(request with
        {
            Join = join with { ResumeCursor = tamperedCursor }
        });
        tampered.Succeeded.Should().BeFalse();
        tampered.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CursorInvalid);

        var otherTenant = await feed.OpenAsync(request with
        {
            Principal = TestServices.Principal("tenant-b"),
            Operation = TestServices.Operation(tenantId: "tenant-b"),
            Join = join with { ResumeCursor = cursor }
        });
        otherTenant.Succeeded.Should().BeFalse();
        otherTenant.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CursorScopeMismatch);
    }

    [Fact]
    public async Task DurableCursorOlderThanRetainedJournalReturnsExpiredError()
    {
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true
        };
        await using var originalProvider = await TestServices.CreateAsync(
            configureRealtime: options => options.CursorSigningKey = CursorKey,
            journalEntries: [TestServices.JournalEntry(1, "one", "first")]);
        var original = await originalProvider.GetRequiredService<IBaseRealtimeFeedSource>()
            .OpenAsync(Request(join));
        var cursor = original.Value!.Descriptor.Cursor!;

        await using var retainedProvider = await TestServices.CreateAsync(
            configureRealtime: options => options.CursorSigningKey = CursorKey,
            journalEntries: [TestServices.JournalEntry(3, "three", "third")]);
        var resumed = await retainedProvider.GetRequiredService<IBaseRealtimeFeedSource>()
            .OpenAsync(Request(join with { ResumeCursor = cursor }));

        resumed.Succeeded.Should().BeFalse();
        resumed.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CursorExpired);
    }

    [Fact]
    public async Task DurableRequestDoesNotSilentlyDowngradeWhenCapabilityIsIncomplete()
    {
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true
        };
        await using var noKeyProvider = await TestServices.CreateAsync(
            journalEntries: [TestServices.JournalEntry(1, "one", "first")]);
        var noKey = await noKeyProvider.GetRequiredService<IBaseRealtimeFeedSource>()
            .OpenAsync(Request(join));
        noKey.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CapabilityUnavailable);

        await using var noJournalProvider = await TestServices.CreateAsync(
            configureRealtime: options => options.CursorSigningKey = CursorKey);
        var noJournal = await noJournalProvider.GetRequiredService<IBaseRealtimeFeedSource>()
            .OpenAsync(Request(join));
        noJournal.Error!.Code.Should().Be(BaseRealtimeErrorCodes.CapabilityUnavailable);

        var noCollection = await noJournalProvider.GetRequiredService<IBaseRealtimeFeedSource>()
            .OpenAsync(Request(join with { CollectionId = null }));
        noCollection.Error!.Code.Should().Be(BaseRealtimeErrorCodes.DurableCollectionRequired);
    }

    [Fact]
    public async Task DurableReplaySkipsOtherTenantsAndRedactsSnapshots()
    {
        await using var provider = await TestServices.CreateAsync(
            configureRealtime: options =>
            {
                options.CursorSigningKey = CursorKey;
                options.Limits = options.Limits with { DurablePollIntervalMilliseconds = 1 };
            },
            journalEntries: [TestServices.JournalEntry(1, "initial", "initial", "tenant-a")]);
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true,
            IncludeSnapshots = true
        };
        var initial = await feed.OpenAsync(Request(join, PrincipalAuthenticationState.Authenticated, "tenant-a"));
        var journal = (TestMutationJournalStore)provider
            .GetRequiredService<HPD.Base.Runtime.Stores.IRecordStoreRegistry>()
            .GetStoreForCollection("items")!;
        journal.Add(TestServices.JournalEntry(2, "hidden", "other-tenant", "tenant-b"));
        journal.Add(TestServices.JournalEntry(3, "visible", "after-safe", "tenant-a") with
        {
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("visible"),
                Payload = TestServices.Payload(("title", "after-safe"), ("writeOnly", "after-forbidden")),
                Metadata = new RecordMetadata()
            }
        });

        var resumed = await feed.OpenAsync(Request(
            join with { ResumeCursor = initial.Value!.Descriptor.Cursor },
            PrincipalAuthenticationState.Authenticated,
            "tenant-a"));
        var replayed = await ReadOneAsync(resumed.Value!.Items);

        replayed.EventId.Should().Be("event-3");
        replayed.Before.Should().BeNull();
        replayed.After!.Payload.Json.ToString().Should().Contain("after-safe");
        replayed.After.Payload.Json.ToString().Should().NotContain("after-forbidden");
        provider.GetRequiredService<BaseRealtimeStats>().PolicySkips.Should().Be(1);
    }

    [Theory]
    [InlineData(PrincipalAuthenticationState.Admin)]
    [InlineData(PrincipalAuthenticationState.System)]
    public async Task DurableReplayIncludesBeforeOnlyForPrivilegedPrincipals(
        PrincipalAuthenticationState state)
    {
        await using var provider = await TestServices.CreateAsync(
            configureRealtime: options =>
            {
                options.CursorSigningKey = CursorKey;
                options.Limits = options.Limits with { DurablePollIntervalMilliseconds = 1 };
            },
            journalEntries: [TestServices.JournalEntry(1, "initial", "initial")]);
        var feed = provider.GetRequiredService<IBaseRealtimeFeedSource>();
        var join = new BaseRealtimeChannelJoinRequest
        {
            Kind = BaseRealtimeChannelKinds.RecordChanges,
            CollectionId = "items",
            Durable = true,
            IncludeSnapshots = true,
            IncludeBefore = true
        };
        var initial = await feed.OpenAsync(Request(join, state));
        var journal = (TestMutationJournalStore)provider
            .GetRequiredService<HPD.Base.Runtime.Stores.IRecordStoreRegistry>()
            .GetStoreForCollection("items")!;
        journal.Add(TestServices.JournalEntry(2, "updated", "after-safe") with
        {
            Operation = BaseOperationKind.Patch,
            Type = BaseEventTypes.RecordPatched,
            Before = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("updated"),
                Payload = TestServices.Payload(
                    ("title", "before-safe"),
                    ("writeOnly", "before-forbidden")),
                Metadata = new RecordMetadata()
            },
            After = new RecordSnapshot
            {
                CollectionId = "items",
                Id = new RecordId("updated"),
                Payload = TestServices.Payload(
                    ("title", "after-safe"),
                    ("writeOnly", "after-forbidden")),
                Metadata = new RecordMetadata()
            }
        });

        var resumed = await feed.OpenAsync(Request(
            join with { ResumeCursor = initial.Value!.Descriptor.Cursor },
            state));
        var replayed = await ReadOneAsync(resumed.Value!.Items);

        replayed.Before!.Payload.Json.ToString().Should().Contain("before-safe");
        replayed.Before.Payload.Json.ToString().Should().NotContain("before-forbidden");
        replayed.After!.Payload.Json.ToString().Should().Contain("after-safe");
        replayed.After.Payload.Json.ToString().Should().NotContain("after-forbidden");
    }

    private static BaseRealtimeFeedRequest Request(
        BaseRealtimeChannelJoinRequest join,
        PrincipalAuthenticationState state = PrincipalAuthenticationState.Anonymous,
        string? tenantId = null) => new()
        {
            Channel = "durable-items",
            Principal = TestServices.Principal(tenantId, state),
            Operation = TestServices.Operation(tenantId: tenantId),
            Join = join
        };

    private static async Task<BaseRealtimeEvent> ReadOneAsync(IAsyncEnumerable<BaseRealtimeEvent> items)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var enumerator = items.GetAsyncEnumerator(cts.Token);
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        return enumerator.Current;
    }
}
