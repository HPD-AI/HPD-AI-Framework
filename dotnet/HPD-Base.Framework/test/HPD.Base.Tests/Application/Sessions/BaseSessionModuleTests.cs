using System.Text;
using FluentAssertions;
using HPD.Base;
using HPD.Base.Tests.Application.Generation;
using HPD.Base.Testing;
using Xunit;

namespace HPD.Base.Tests.Application.Sessions;

public sealed class BaseSessionModuleTests
{
    private static readonly byte[] DependencyKey =
        Enumerable.Repeat((byte)0x42, 32).ToArray();

    [Fact]
    public async Task FilesRemainSessionBoundAcrossUploadAndDownload()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddFiles(options => options.Buckets.Add(new FileBucketDescriptor
                {
                    BucketId = new FileBucketId("attachments"),
                    Capabilities = new FileBucketCapabilities
                    {
                        Upload = true,
                        Download = true,
                        Metadata = true,
                        Delete = true,
                        List = true,
                    },
                })));
        var bucket = host
            .Session(BaseTestPrincipal.User("file-user", "tenant-a"))
            .Files
            .Bucket("attachments");
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("session-bound"));

        FileObjectUploadResult uploaded =
            (await bucket.UploadAsync("docs/readme.txt", content))
            .RequireValue();

        await using FileObjectDownloadResult download =
            (await bucket.OpenReadAsync(uploaded.Metadata.ObjectId)).RequireValue();
        using var reader = new StreamReader(download.Content, Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("session-bound");

        host.Policy.DenyAll();
        (await bucket.GetMetadataAsync(uploaded.Metadata.ObjectId))
            .Should().BeOfType<
                HPD.Base.BaseFailure<FileObjectMetadata>>();
    }

    [Fact]
    public async Task DependenciesAreTypedAndCommittedMutationsAreCaptured()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection)
                .AddDependencies(options => options.ProtectionKey = DependencyKey));
        var session = host.Session(
            BaseTestPrincipal.User("dependency-user", "tenant-a"));
        BaseDependencyReference expected = session.Dependencies.Record(
            GeneratedProject.Collection,
            new RecordId("project_1"));

        await session.Collection(GeneratedProject.Collection).CreateAsync(
            new RecordId("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "dependency" });

        host.Probe.Invalidations.Should().ContainSingle();
        host.Probe.Invalidations[0].References.Should().Contain(expected);
    }

    [Fact]
    public async Task LiveQueryRerunsThroughSessionDependencies()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection)
                .AddDependencies(options => options.ProtectionKey = DependencyKey)
                .AddLiveQueries());
        var session = host.Session(
            BaseTestPrincipal.User("live-query-user", "tenant-a"));
        var evaluations = 0;
        await using BaseLiveQuerySubscription<int> subscription =
            await session.LiveQueries.SubscribeAsync(
                "project-count",
                _ => ValueTask.FromResult(
                    global::HPD.Base.LiveQuery.Result(
                    Interlocked.Increment(ref evaluations),
                    session.Dependencies.Set(
                        session.Dependencies.Record(
                            GeneratedProject.Collection,
                            new RecordId("project_1"))))));
        await using var transitions =
            subscription.Transitions.GetAsyncEnumerator();

        (await NextAsync(transitions)).Should()
            .BeOfType<BaseLiveQuerySnapshot<int>>()
            .Which.Value.Should().Be(1);

        await session.Collection(GeneratedProject.Collection).CreateAsync(
            new RecordId("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "rerun" });

        (await NextAsync(transitions)).Should()
            .BeOfType<BaseLiveQuerySnapshot<int>>()
            .Which.Value.Should().Be(2);
    }

    [Fact]
    public async Task LiveRealtimeFeedProjectsCommittedMutation()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder

                .AddCollection(GeneratedProject.Collection)
                .AddRealtime());
        var session = host.Session(
            BaseTestPrincipal.User("realtime-user"));
        await using BaseRealtimeFeed feed = await session.Realtime
            .Live(GeneratedProject.Collection)
            .IncludeSnapshots()
            .OpenAsync();
        await using var events = feed.Events.GetAsyncEnumerator();
        Task<bool> pendingEvent = events.MoveNextAsync().AsTask();

        await session.Collection(GeneratedProject.Collection).CreateAsync(
            new RecordId("project_1"),
            new GeneratedProject { OrganizationId = "org_1", Name = "live" });

        (await pendingEvent.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        var mutation = events.Current;
        mutation.Resource.RecordId.Should().Be(new RecordId("project_1"));
        mutation.After.Should().NotBeNull();
        mutation.Cursor.Should().BeNull();
    }

    [Fact]
    public async Task DurableAndResumeBuildersUseSqliteCursorCapability()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-realtime-{Guid.NewGuid():N}.db");
        try
        {
            await using BaseTestHost host = await BaseTestHost.CreateAsync(
                builder => builder
                    .UseSqlite(options => options.DataSource = database)
                    .AddCollection(GeneratedProject.Collection)
                    .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                    {
                        Id = 7,
                        Key = Enumerable.Repeat((byte)0x47, 32).ToArray(),
                    })
                    .AddRealtime());
            var session = host.Session(
                BaseTestPrincipal.User("realtime-user", "tenant-a"));

            await using BaseRealtimeFeed durable = await session.Realtime
                .Durable(GeneratedProject.Collection)
                .OpenAsync();
            durable.Metadata.Replayable.Should().BeTrue();
            durable.Metadata.Resumable.Should().BeTrue();
            durable.Metadata.Cursor.Should().NotBeNullOrWhiteSpace();

            await using BaseRealtimeFeed resumed = await session.Realtime
                .Resume(GeneratedProject.Collection, durable.Metadata.Cursor!)
                .OpenAsync();
            resumed.Metadata.Resumable.Should().BeTrue();
        }
        finally
        {
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [Fact]
    public async Task DurableBuilderRejectsProviderWithoutJournalCapability()
    {
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .AddCollection(GeneratedProject.Collection)
                .AddRealtime());
        var session = host.Session(
            BaseTestPrincipal.User("realtime-user", "tenant-a"));

        Func<Task> open = async () => await session.Realtime
            .Durable(GeneratedProject.Collection)
            .OpenAsync();

        BaseRealtimeOpenException failure =
            (await open.Should().ThrowAsync<BaseRealtimeOpenException>())
            .Which;
        failure.Code.Should().Be("base.realtime.capabilityUnavailable");
    }

    private static async ValueTask<T> NextAsync<T>(
        IAsyncEnumerator<T> enumerator)
    {
        bool moved = await enumerator.MoveNextAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        moved.Should().BeTrue();
        return enumerator.Current;
    }
}
