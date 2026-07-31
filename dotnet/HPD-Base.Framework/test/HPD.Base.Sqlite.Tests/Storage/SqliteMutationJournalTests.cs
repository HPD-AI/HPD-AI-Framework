using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Base.Sqlite.Tests.Conformance;
using HPD.Base.StoreConformance;
using HPD.Base.StoreConformance.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteMutationJournalTests
{
    [Fact]
    public async Task MutationsAppendOrderedTransactionalJournalEntries()
    {
        await using var store = CreateStore();
        var collection = Collection();
        var id = new RecordId("one");

        var create = await store.CreateAsync(
            collection,
            new RecordCreateRequest { RequestedId = id, Payload = Payload("created") },
            Operation(BaseOperationKind.Create, 1));
        var patch = await store.PatchAsync(
            collection,
            id,
            new RecordPatchRequest { Patch = Payload("patched") },
            Operation(BaseOperationKind.Patch, 2));
        var replace = await store.ReplaceAsync(
            collection,
            id,
            new RecordReplaceRequest { Payload = Payload("replaced") },
            Operation(BaseOperationKind.Replace, 3));
        var delete = await store.DeleteAsync(
            collection,
            id,
            new RecordDeleteRequest { ReturnPrevious = true },
            Operation(BaseOperationKind.Delete, 4));

        create.Status.Should().Be(OperationStatus.Created);
        patch.Status.Should().Be(OperationStatus.Updated);
        replace.Status.Should().Be(OperationStatus.Updated);
        delete.Status.Should().Be(OperationStatus.Deleted);

        var page = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 10 });
        page.Entries.Select(entry => entry.Position.Value).Should().Equal(1, 2, 3, 4);
        page.Entries.Select(entry => entry.Operation).Should().Equal(
            BaseOperationKind.Create,
            BaseOperationKind.Patch,
            BaseOperationKind.Replace,
            BaseOperationKind.Delete);
        page.Entries.Should().OnlyContain(entry => entry.Visibility == VisibilityLevel.Public);
        page.Entries.Select(entry => entry.EventId)
            .Should().OnlyHaveUniqueItems()
            .And.OnlyContain(eventId => !string.IsNullOrWhiteSpace(eventId));
        page.Entries[0].Before.Should().BeNull();
        page.Entries[0].After.Should().NotBeNull();
        page.Entries[1].Before.Should().NotBeNull();
        page.Entries[1].After.Should().NotBeNull();
        page.Entries[2].Before.Should().NotBeNull();
        page.Entries[2].After.Should().NotBeNull();
        page.Entries[3].Before.Should().NotBeNull();
        page.Entries[3].After.Should().BeNull();
        page.HighWatermark.Value.Should().Be(4);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task FailedMutationDoesNotAppendJournalEntry()
    {
        await using var store = CreateStore();
        var collection = Collection();
        var id = new RecordId("one");
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { RequestedId = id, Payload = Payload("created") },
            Operation(BaseOperationKind.Create, 1));

        var duplicate = await store.CreateAsync(
            collection,
            new RecordCreateRequest { RequestedId = id, Payload = Payload("duplicate") },
            Operation(BaseOperationKind.Create, 2));

        duplicate.Status.Should().Be(OperationStatus.Conflict);
        var bounds = await store.GetMutationJournalBoundsAsync();
        bounds.HighWatermark.Value.Should().Be(1);
    }

    [Fact]
    public async Task BoundedReadUsesStableInclusiveHighWatermark()
    {
        await using var store = CreateStore();
        var collection = Collection();
        for (var index = 1; index <= 3; index++)
        {
            await store.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId($"record-{index}"),
                    Payload = Payload($"value-{index}")
                },
                Operation(BaseOperationKind.Create, index));
        }

        var first = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 2 });
        first.Entries.Select(entry => entry.Position.Value).Should().Equal(1, 2);
        first.HighWatermark.Value.Should().Be(3);
        first.HasMore.Should().BeTrue();

        var second = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest
        {
            After = first.Entries[^1].Position,
            Through = first.HighWatermark,
            Limit = 2
        });
        second.Entries.Select(entry => entry.Position.Value).Should().Equal(3);
        second.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task MaximumEntryRetentionPrunesOldestCommittedPositions()
    {
        await using var store = CreateStore(options => options.MutationJournalMaxEntries = 2);
        var collection = Collection();
        for (var index = 1; index <= 3; index++)
        {
            await store.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId($"record-{index}"),
                    Payload = Payload($"value-{index}")
                },
                Operation(BaseOperationKind.Create, index));
        }

        var page = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 10 });

        page.Earliest.Value.Should().Be(2);
        page.HighWatermark.Value.Should().Be(3);
        page.Entries.Select(entry => entry.Position.Value).Should().Equal(2, 3);
    }

    [Fact]
    public async Task AgeRetentionExpiresEntriesDuringAnIdleRead()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-l26-logical-retention-{Guid.NewGuid():N}.db");
        try
        {
            var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1));
            await using var store = CreateStore(
                options =>
                {
                    options.DataSource = path;
                    options.MutationJournalRetention = TimeSpan.FromSeconds(5);
                },
                time);
            var created = await store.CreateAsync(
                Collection(),
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("old"),
                    Payload = Payload("old")
                },
                Operation(BaseOperationKind.Create, 1));
            var committed = await store.ReadMutationJournalAsync(
                new BaseMutationJournalReadRequest { Limit = 10 });
            var eventId = committed.Entries.Should().ContainSingle().Subject.EventId;
            time.Advance(TimeSpan.FromSeconds(9));

            var page = await store.ReadMutationJournalAsync(
                new BaseMutationJournalReadRequest { Limit = 10 });
            var found = await store.FindMutationJournalEntryAsync(eventId);

            page.Earliest.Value.Should().Be(2);
            page.HighWatermark.Value.Should().Be(1);
            page.Entries.Should().BeEmpty();
            found.Should().BeNull();

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM hpd_base_mutation_journal;";
            (await count.ExecuteScalarAsync()).Should().Be(1L);
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    [Fact]
    public async Task JournalReadsRejectLimitsAboveTheConfiguredMaximum()
    {
        await using var store = CreateStore(options => options.MutationJournalMaxReadSize = 2);

        var act = () => store.ReadMutationJournalAsync(
            new BaseMutationJournalReadRequest { Limit = 3 }).AsTask();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RevisionConflictDoesNotAppendJournalEntry()
    {
        await using var store = CreateStore();
        var collection = Collection();
        var id = new RecordId("one");
        await store.CreateAsync(
            collection,
            new RecordCreateRequest { RequestedId = id, Payload = Payload("created") },
            Operation(BaseOperationKind.Create, 1));

        var conflict = await store.PatchAsync(
            collection,
            id,
            new RecordPatchRequest
            {
                Patch = Payload("conflict"),
                ExpectedRevision = new RevisionToken("99")
            },
            Operation(BaseOperationKind.Patch, 2));

        conflict.Status.Should().Be(OperationStatus.Conflict);
        (await store.GetMutationJournalBoundsAsync()).HighWatermark.Value.Should().Be(1);
    }

    [Fact]
    public async Task CancelledMutationDoesNotAppendJournalEntry()
    {
        await using var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await store.CreateAsync(
            Collection(),
            new RecordCreateRequest
            {
                RequestedId = new RecordId("cancelled"),
                Payload = Payload("cancelled")
            },
            Operation(BaseOperationKind.Create, 1),
            cancellation.Token);

        result.Status.Should().Be(OperationStatus.StoreError);
        (await store.GetMutationJournalBoundsAsync()).HighWatermark.Value.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentWritersReceiveDistinctOrderedJournalPositions()
    {
        await using var store = CreateStore();
        var collection = Collection();

        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(index =>
            store.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId($"concurrent-{index}"),
                    Payload = Payload($"value-{index}")
                },
                Operation(BaseOperationKind.Create, index)).AsTask()));

        results.Should().OnlyContain(result => result.Status == OperationStatus.Created);
        var page = await store.ReadMutationJournalAsync(new BaseMutationJournalReadRequest { Limit = 10 });
        page.Entries.Select(entry => entry.Position.Value).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        page.Entries.Select(entry => entry.EventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RuntimePublishesCommittedJournalIdentityAndPreservesTransactionalReference()
    {
        var fixture = new SqliteConformanceFixture();
        await fixture.ResetAsync();
        var publisher = new ConformanceCapturingEventPublisher();
        await using var services = (ServiceProvider)await fixture.CreateRuntimeServicesAsync(
            new RuntimeStoreConformanceOptions { EventPublisher = publisher });
        var runtime = services.GetRequiredService<IBaseRecordRuntime>();

        var result = await runtime.CreateAsync(
            fixture.Collection.Id,
            new RecordCreateRequest
            {
                Payload = RecordStoreConformanceData.Payload(("title", "journaled"))
            },
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.System,
                SubjectKind = AccessSubjectKind.System
            },
            fixture.Operation(BaseOperationKind.Create));

        result.Events.Should().ContainSingle();
        result.Events![0].Guarantee.Should().Be(EventDeliveryGuarantee.Transactional);
        publisher.LastEvent.Should().NotBeNull();
        publisher.LastEvent!.EventId.Should().Be(result.Events[0].EventId);
    }

    [Fact]
    public async Task RequireEnqueueAcceptsSQLiteTransactionalJournal()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-l26-require-enqueue-{Guid.NewGuid():N}.db");
        try
        {
            var collection = Collection() with
            {
                Operations = new CollectionOperationMatrix { Create = true }
            };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPolicyEvaluator, ConformanceAllowPolicyEvaluator>();
            services.AddHPDBaseRuntime(options =>
                    options.Events.PublishFailureMode = BaseEventPublishFailureMode.RequireEnqueue)
                .AddHPDBaseSqliteStore(options =>
                {
                    options.StoreId = "require-enqueue";
                    options.DataSource = path;
                    options.CollectionIds = [collection.Id];
                    options.Collections = [collection];
                });
            await using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseSqliteStore(provider);
            var descriptor = await provider.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();

            var result = await provider.GetRequiredService<IBaseRecordRuntime>().CreateAsync(
                collection.Id,
                new RecordCreateRequest { Payload = Payload("durable") },
                new PrincipalContext
                {
                    AuthenticationState = PrincipalAuthenticationState.System,
                    SubjectKind = AccessSubjectKind.System
                },
                Operation(BaseOperationKind.Create, 1));

            descriptor.Validation.Succeeded.Should().BeTrue();
            result.Status.Should().Be(OperationStatus.Created);
            result.Events.Should().ContainSingle()
                .Which.Guarantee.Should().Be(EventDeliveryGuarantee.Transactional);
            result.Warnings.Should().BeNullOrEmpty();
        }
        finally
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static SqliteRecordStore CreateStore(
        Action<HPDBaseSqliteOptions>? configure = null,
        TimeProvider? timeProvider = null)
    {
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"journal_{Guid.NewGuid():N}"
        };
        configure?.Invoke(options);
        return SqliteTestFactory.Create(
            options,
            timeProvider ?? new ManualTimeProvider(DateTimeOffset.UnixEpoch));
    }

    private static OperationContext Operation(BaseOperationKind operation, int second) => new()
    {
        Operation = operation,
        CollectionId = "items",
        TenantId = "tenant-a",
        Now = DateTimeOffset.UnixEpoch.AddSeconds(second)
    };

    private static RecordPayload Payload(string title)
    {
        using var document = JsonDocument.Parse($$"""{"title":"{{title}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan value) => utcNow += value;
    }

}
