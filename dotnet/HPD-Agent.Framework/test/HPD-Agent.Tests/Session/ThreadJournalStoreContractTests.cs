using Xunit;
using HPD.Events.Core;

namespace HPD.Agent.Tests.Session;

public sealed class ThreadJournalStoreContractTests
{
    [Fact]
    public async Task Publisher_CommitsBeforePublishingTheExactCanonicalValue()
    {
        var store = new InMemorySessionStore();
        using var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<AgentEvent>();
        var publisher = new ThreadEventPublisher(store, coordinator);
        var key = new ThreadKey("session-1", "main");
        var proposed = new TextDeltaEvent("hello", "message-1");

        var committed = await publisher.CommitAndPublishAsync(key, proposed);
        var live = await inbox.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, proposed.ThreadSequenceNumber);
        Assert.Same(committed, live);
        Assert.Equal(1, committed.ThreadSequenceNumber);
        Assert.False(string.IsNullOrWhiteSpace(committed.EventId));
        Assert.Equal(key.SessionId, committed.SessionId);
        Assert.Equal(key.ThreadId, committed.ThreadId);

        var replay = await store.CollectThreadEventsAsync(key);
        Assert.Equal(committed, Assert.Single(replay!));
    }

    [Fact]
    public async Task Publisher_DoesNotPublishWhenCanonicalAppendFails()
    {
        var store = new InMemorySessionStore();
        using var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<AgentEvent>();
        var publisher = new ThreadEventPublisher(store, coordinator);
        var key = new ThreadKey("session-1", "main");

        await Assert.ThrowsAsync<ThreadAppendConflictException>(async () =>
            await publisher.CommitAndPublishAsync(
                key,
                [new TextDeltaEvent("never-live", "message-1")],
                new ThreadAppendCondition(new ThreadJournalCursor(1, 1))));

        Assert.False(inbox.Reader.TryRead(out _));
        Assert.Null(await store.GetThreadEventHeadAsync(key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Append_ReturnsImmutableCommittedValues_AndRangeReadsAreBounded(bool fileStore)
    {
        await WithStoreAsync(fileStore, async store =>
        {
            var key = new ThreadKey("session-1", "main");
            var proposed = Enumerable.Range(0, 5)
                .Select(index => Scoped(key, new TextDeltaEvent(index.ToString(), "message-1")))
                .ToArray();

            var result = await store.AppendThreadEventsAsync(key, proposed);

            Assert.All(proposed, evt => Assert.Equal(0, evt.ThreadSequenceNumber));
            Assert.Equal([1L, 2, 3, 4, 5], result.CommittedEvents.Select(evt => evt.ThreadSequenceNumber));
            Assert.Equal(new ThreadJournalCursor(1, 0), result.PreviousCursor);
            Assert.Equal(new ThreadJournalCursor(1, 5), result.CurrentCursor);

            var batches = new List<ThreadEventBatch>();
            await foreach (var batch in store.ReadThreadEventsAsync(
                key,
                new ThreadEventReadRequest(new ThreadJournalCursor(1, 1), Through: 4, MaxBatchEventCount: 2)))
                batches.Add(batch);

            Assert.Equal(2, batches.Count);
            Assert.All(batches, batch => Assert.InRange(batch.Events.Count, 1, 2));
            Assert.Equal([2L, 3, 4], batches.SelectMany(batch => batch.Events).Select(evt => evt.ThreadSequenceNumber));
            Assert.Equal(5, (await store.GetThreadEventHeadAsync(key))!.ThreadSequenceNumber);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observe_CapturesWaiterWithoutLostCommitGap(bool fileStore)
    {
        await WithStoreAsync(fileStore, async store =>
        {
            var key = new ThreadKey("session-1", "main");
            await store.AppendThreadEventsAsync(key, [Scoped(key, new TextDeltaEvent("one", "message-1"))]);

            await using var observer = store.ObserveThreadEventsAsync(
                key,
                new ThreadJournalCursor(1, 1),
                new ThreadObservationOptions(MaxBatchEventCount: 4)).GetAsyncEnumerator();

            var pending = observer.MoveNextAsync().AsTask();
            await store.AppendThreadEventsAsync(key, [Scoped(key, new TextDeltaEvent("two", "message-1"))]);

            Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            var evt = Assert.Single(observer.Current.Events);
            Assert.Equal(2, evt.ThreadSequenceNumber);
            Assert.Equal("two", Assert.IsType<TextDeltaEvent>(evt).Text);
        });
    }

    [Fact]
    public async Task FileStore_IdleObserver_PerformsNoJournalReadsOrDecodes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            var key = new ThreadKey("session-1", "main");
            var store = new FileSessionStore(directory);
            await store.AppendThreadEventsAsync(key, [Scoped(key, new TextDeltaEvent("one", "message-1"))]);

            using var cancellation = new CancellationTokenSource();
            await using var observer = store.ObserveThreadEventsAsync(
                key,
                new ThreadJournalCursor(1, 1),
                new ThreadObservationOptions(),
                cancellation.Token).GetAsyncEnumerator();
            var pending = observer.MoveNextAsync().AsTask();

            await WaitUntilAsync(
                () => store.GetDiagnostics().ObservationWaitCount == 1,
                TimeSpan.FromSeconds(5));
            var before = store.GetDiagnostics();
            await Task.Delay(100);
            var after = store.GetDiagnostics();

            Assert.False(pending.IsCompleted);
            Assert.Equal(before.SegmentReadCount, after.SegmentReadCount);
            Assert.Equal(before.SegmentBytesRead, after.SegmentBytesRead);
            Assert.Equal(before.EventDecodeCount, after.EventDecodeCount);
            Assert.Equal(before.ObservationWaitCount, after.ObservationWaitCount);

            cancellation.Cancel();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_RejectsDuplicateIdentityAndImpossibleCursor(bool fileStore)
    {
        await WithStoreAsync(fileStore, async store =>
        {
            var key = new ThreadKey("session-1", "main");
            var evt = Scoped(key, new TextDeltaEvent("one", "message-1"));
            await store.AppendThreadEventsAsync(key, [evt]);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.AppendThreadEventsAsync(key, [evt]));

            await Assert.ThrowsAsync<ThreadCursorConflictException>(async () =>
            {
                await foreach (var _ in store.ReadThreadEventsAsync(
                    key,
                    new ThreadEventReadRequest(new ThreadJournalCursor(1, 2))))
                {
                }
            });
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_RemovesCompletePreviousJournalAndRestartsPositions(bool fileStore)
    {
        await WithStoreAsync(fileStore, async store =>
        {
            var key = new ThreadKey("session-1", "main");
            var original = await store.AppendThreadEventsAsync(key,
            [
                Scoped(key, new TextDeltaEvent("old-one", "message-1")),
                Scoped(key, new TextDeltaEvent("old-two", "message-1"))
            ]);

            var replacement = await store.ReplaceThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("replacement", "message-2"))],
                new ThreadJournalCursor(1, 2));

            Assert.Equal(new ThreadJournalCursor(1, 2), replacement.PreviousCursor);
            Assert.Equal(new ThreadJournalCursor(2, 1), replacement.CurrentCursor);
            var replay = await store.CollectThreadEventsAsync(key);
            var only = Assert.IsType<TextDeltaEvent>(Assert.Single(replay!));
            Assert.Equal("replacement", only.Text);
            Assert.Equal(1, only.ThreadSequenceNumber);
            Assert.DoesNotContain(only.EventId, original.CommittedEvents.Select(evt => evt.EventId));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_RebasesObserversAndRejectsThePreviousGeneration(bool fileStore)
    {
        await WithStoreAsync(fileStore, async store =>
        {
            var key = new ThreadKey("session-1", "main");
            await store.AppendThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("old", "message-1"))]);
            await using var observer = store.ObserveThreadEventsAsync(
                key,
                new ThreadJournalCursor(1, 1),
                new ThreadObservationOptions()).GetAsyncEnumerator();
            var pending = observer.MoveNextAsync().AsTask();

            var replacement = await store.ReplaceThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("new", "message-2"))],
                new ThreadJournalCursor(1, 1));

            var rebased = await Assert.ThrowsAsync<ThreadJournalReplacedException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(new ThreadJournalCursor(1, 1), rebased.PreviousCursor);
            Assert.Equal(new ThreadJournalCursor(2, 1), rebased.CurrentCursor);
            Assert.Equal(new ThreadJournalCursor(2, 1), replacement.CurrentCursor);

            await Assert.ThrowsAsync<ThreadCursorConflictException>(async () =>
            {
                await foreach (var _ in store.ReadThreadEventsAsync(
                    key,
                    new ThreadEventReadRequest(new ThreadJournalCursor(1, 0))))
                {
                }
            });
        });
    }

    [Fact]
    public async Task FileStore_ReopensNewSegmentedFormatWithoutLegacyMaterialization()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            var key = new ThreadKey("session-1", "main");
            var first = new FileSessionStore(directory, new FileSessionStoreOptions(SegmentEventCapacity: 2));
            await first.AppendThreadEventsAsync(key,
            [
                Scoped(key, new TextDeltaEvent("one", "message-1")),
                Scoped(key, new TextDeltaEvent("two", "message-1"))
            ]);
            await first.AppendThreadEventsAsync(key, [Scoped(key, new TextDeltaEvent("three", "message-1"))]);

            var reopened = new FileSessionStore(directory, new FileSessionStoreOptions(SegmentEventCapacity: 2));
            var events = new List<AgentEvent>();
            await foreach (var batch in reopened.ReadThreadEventsAsync(
                key,
                new ThreadEventReadRequest(new ThreadJournalCursor(1, 2))))
                events.AddRange(batch.Events);

            Assert.Equal("three", Assert.IsType<TextDeltaEvent>(Assert.Single(events)).Text);
            Assert.Equal(3, (await reopened.GetThreadEventHeadAsync(key))!.ThreadSequenceNumber);
            Assert.False(File.Exists(Path.Combine(directory, "session-1", "threads", "main", "thread.events.jsonl")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileStore_ReopenTruncatesOnlyAnIncompleteFinalAppendFrame()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            var key = new ThreadKey("session-1", "main");
            var store = new FileSessionStore(directory);
            await store.AppendThreadEventsAsync(key, [Scoped(key, new TextDeltaEvent("committed", "message-1"))]);

            var segment = Assert.Single(Directory.GetFiles(
                Path.Combine(directory, "sessions", "session-1", "threads", "main", "journal"),
                "segment-*.events"));
            await File.AppendAllTextAsync(segment, "[{\"type\":\"TEXT_DELTA\"");

            var reopened = new FileSessionStore(directory);
            Assert.Equal(1, (await reopened.GetThreadEventHeadAsync(key))!.ThreadSequenceNumber);
            Assert.EndsWith("\n", await File.ReadAllTextAsync(segment), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileStore_RebuildsAnIncompatibleDerivedDescriptorFromTheJournal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            var key = new ThreadKey("session-1", "main");
            var store = new FileSessionStore(directory);
            await store.AppendThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("committed", "message-1"))]);

            var descriptorPath = Path.Combine(
                directory, "sessions", key.SessionId, "threads", key.ThreadId, "thread.descriptor.json");
            var descriptor = await File.ReadAllTextAsync(descriptorPath);
            await File.WriteAllTextAsync(descriptorPath, descriptor.Replace("\"version\": 2", "\"version\": 1"));

            var reopened = new FileSessionStore(directory);
            var recovered = await reopened.GetThreadEventHeadAsync(key);

            Assert.Equal(new ThreadJournalCursor(1, 1), recovered!.Cursor);
            Assert.Contains("\"version\": 2", await File.ReadAllTextAsync(descriptorPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileStore_DescriptorRecoveryPreservesTheRebasedJournalGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            var key = new ThreadKey("session-1", "main");
            var store = new FileSessionStore(directory);
            await store.AppendThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("old", "message-1"))]);
            await store.ReplaceThreadEventsAsync(
                key,
                [Scoped(key, new TextDeltaEvent("replacement", "message-2"))],
                new ThreadJournalCursor(1, 1));

            var threadPath = Path.Combine(directory, "sessions", key.SessionId, "threads", key.ThreadId);
            File.Delete(Path.Combine(threadPath, "thread.descriptor.json"));
            File.Delete(Path.Combine(threadPath, "journal.index"));

            var reopened = new FileSessionStore(directory);
            var recovered = await reopened.GetThreadEventHeadAsync(key);

            Assert.Equal(new ThreadJournalCursor(2, 1), recovered!.Cursor);
            var events = await reopened.CollectThreadEventsAsync(key);
            Assert.Equal("replacement", Assert.IsType<TextDeltaEvent>(Assert.Single(events!)).Text);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static AgentEvent Scoped(ThreadKey key, AgentEvent evt)
        => evt with { SessionId = key.SessionId, ThreadId = key.ThreadId };

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not reached before the timeout.");
            await Task.Delay(10);
        }
    }

    private static async Task WithStoreAsync(bool fileStore, Func<ISessionStore, Task> test)
    {
        if (!fileStore)
        {
            await test(new InMemorySessionStore());
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"hpd-file-journal-{Guid.NewGuid():N}");
        try
        {
            await test(new FileSessionStore(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
