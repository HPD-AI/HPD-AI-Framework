using System.Runtime.CompilerServices;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public sealed class ReplayFrameTests
{
    private static readonly ReplayFrameSourceContract Finite = new(
        ReplayTimestampOrder.Nondecreasing,
        ReplayTimestampFinality.Completion,
        ReplaySourceCardinality.Repeatable);

    private static readonly ReplayFrameSourceContract Watermarked = new(
        ReplayTimestampOrder.Nondecreasing,
        ReplayTimestampFinality.ExclusiveWatermark,
        ReplaySourceCardinality.Repeatable);

    private sealed record TestEvent(string Name) : Event, IReplayContentDigest
    {
        public string ReplayContentDigest => Name;
    }

    private sealed record UndigestedEvent(string Name) : Event;

    [Fact]
    public async Task Finalized_handle_is_opaque_live_only_until_enumerator_advances()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("source", new EnumerableReplaySource<TestEvent>([At("one", 10), At("two", 20)]), Finite);
        await using IAsyncEnumerator<FinalizedReplayFrameHandle<TestEvent>> frames = timeline
            .ReadFinalizedFramesAsync(ReplayReadOptions.All).GetAsyncEnumerator();

        Assert.True(await frames.MoveNextAsync());
        FinalizedReplayFrameHandle<TestEvent> first = frames.Current;
        Assert.True(first.IsSpecified);
        Assert.True(first.TryGetFrame(out ReplayFrame<TestEvent>? borrowed));
        Assert.Equal("one", Assert.Single(borrowed!.Entries).Event.Name);
        Assert.False(default(FinalizedReplayFrameHandle<TestEvent>).TryGetFrame(out _));

        Assert.True(await frames.MoveNextAsync());
        Assert.False(first.TryGetFrame(out _));
        Assert.True(frames.Current.TryGetFrame(out _));
    }

    [Fact]
    public async Task Finalized_handle_refuses_events_without_canonical_content_digest()
    {
        var source = new EnumerableReplaySource<UndigestedEvent>(
        [
            new UndigestedEvent("event") { ExchangeTimestampNs = 10 }
        ]);
        ReplayTimeline<UndigestedEvent> timeline = ReplayTimeline<UndigestedEvent>.Create()
            .AddFrameSource("source", source, Finite);

        await Assert.ThrowsAsync<ReplayFrameContractException>(async () =>
            await ReadAllAsync(timeline.ReadFinalizedFramesAsync(ReplayReadOptions.All)));
    }

    [Fact]
    public async Task Entries_are_the_evidence_preserving_projection_of_ordinary_read()
    {
        TestEvent[] events = [At("a", 10), At("b", 20)];
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create().AddSource("source", events);

        List<ReplayEntry<TestEvent>> entries = await ReadAllAsync(timeline.ReadEntriesAsync(ReplayReadOptions.All));
        List<TestEvent> ordinary = await ReadAllAsync(timeline.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(ordinary, entries.Select(static entry => entry.Event));
        Assert.All(entries, entry => Assert.Equal("source", entry.Source.SourceId));
        Assert.Equal([0L, 1L], entries.Select(static entry => entry.Key.SourceSequence));
    }

    [Fact]
    public async Task Equal_timestamps_from_every_source_form_one_sorted_frame()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("later", new EnumerableReplaySource<TestEvent>([At("later", 10)]), Finite, priority: 10)
            .AddFrameSource("earlier", new EnumerableReplaySource<TestEvent>([At("earlier", 10)]), Finite, priority: 0);

        List<ReplayFrame<TestEvent>> frames = await ReadAllAsync(timeline.ReadFramesAsync(ReplayReadOptions.All));

        ReplayFrame<TestEvent> frame = Assert.Single(frames);
        Assert.Equal(10, frame.TimestampNs);
        Assert.Equal(["earlier", "later"], frame.Entries.Select(static entry => entry.Event.Name));
        Assert.Equal(2, frame.Boundary.EntryCount);
    }

    [Fact]
    public async Task Frame_path_rejects_regression_hidden_by_consumer_filter()
    {
        DateTimeOffset from = DateTimeOffset.UnixEpoch.AddTicks(1);
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("bad", new EnumerableReplaySource<TestEvent>([At("visible", 30), At("filtered", 10)]), Finite);

        ReplayFrameContractException failure = await Assert.ThrowsAsync<ReplayFrameContractException>(async () =>
            await ReadAllAsync(timeline.ReadFramesAsync(new ReplayReadOptions(from, null, null, null))));

        Assert.Equal("bad", failure.ReplaySource.SourceId);
        Assert.Equal(30, failure.PreviousKey?.TimestampNs);
        Assert.Equal(10, failure.OffendingKey?.TimestampNs);
    }

    [Fact]
    public async Task Complete_frame_limit_overruns_instead_of_splitting_timestamp()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("source", new EnumerableReplaySource<TestEvent>(
                [At("one", 10), At("two", 10), At("three", 10), At("later", 20)]), Finite);

        List<ReplayFrame<TestEvent>> frames = await ReadAllAsync(timeline.ReadFramesAsync(
            ReplayReadOptions.All with { Limit = 2 }));

        ReplayFrame<TestEvent> frame = Assert.Single(frames);
        Assert.Equal(3, frame.Entries.Count);
        Assert.Equal(2, frame.Boundary.RequestedEventLimit);
        Assert.Equal(3, frame.Boundary.ActualCumulativeEntryCount);
        Assert.True(frame.Boundary.CompletedRequestedLimit);
    }

    [Fact]
    public async Task Bounded_watermarked_read_drains_buffered_eligible_entry_before_terminating()
    {
        var source = new MessageSource(
            ReplaySourceMessage<TestEvent>.FromEvent(At("eligible", 99)),
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(100),
            ReplaySourceMessage<TestEvent>.FromEvent(At("tail", 101)));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("live", source, Watermarked);
        DateTimeOffset to = DateTimeOffset.UnixEpoch.AddTicks(1);

        List<ReplayFrame<TestEvent>> frames = await ReadAllAsync(timeline.ReadFramesAsync(
            new ReplayReadOptions(null, to, null, null)));

        Assert.Equal("eligible", Assert.Single(Assert.Single(frames).Entries).Event.Name);
        Assert.Equal(2, source.MessagesRead);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Empty_bounded_watermarked_read_completes_at_exclusive_boundary()
    {
        var source = new MessageSource(
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(100),
            ReplaySourceMessage<TestEvent>.FromEvent(At("tail", 101)));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("live", source, Watermarked);

        List<ReplayFrame<TestEvent>> frames = await ReadAllAsync(timeline.ReadFramesAsync(
            new ReplayReadOptions(null, DateTimeOffset.UnixEpoch.AddTicks(1), null, null)));

        Assert.Empty(frames);
        Assert.Equal(1, source.MessagesRead);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task Unbounded_watermark_releases_complete_frame_without_waiting_for_later_data()
    {
        var source = new MessageSource(
            ReplaySourceMessage<TestEvent>.FromEvent(At("complete", 10)),
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(11),
            ReplaySourceMessage<TestEvent>.FromEvent(At("later", 20)));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("live", source, Watermarked);

        await using IAsyncEnumerator<ReplayFrame<TestEvent>> frames = timeline
            .ReadFramesAsync(ReplayReadOptions.All).GetAsyncEnumerator();

        Assert.True(await frames.MoveNextAsync());
        Assert.Equal("complete", Assert.Single(frames.Current.Entries).Event.Name);
        Assert.Equal(2, source.MessagesRead);
    }

    [Fact]
    public async Task Data_below_previous_watermark_fails()
    {
        var source = new MessageSource(
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(10),
            ReplaySourceMessage<TestEvent>.FromEvent(At("late", 9)));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("live", source, Watermarked);

        ReplayFrameContractException failure = await Assert.ThrowsAsync<ReplayFrameContractException>(async () =>
            await ReadAllAsync(timeline.ReadFramesAsync(ReplayReadOptions.All)));

        Assert.Equal(10, failure.LastExclusiveWatermarkTimestampNs);
        Assert.Equal(9, failure.OffendingKey?.TimestampNs);
    }

    [Fact]
    public async Task Regressing_watermark_fails()
    {
        var source = new MessageSource(
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(20),
            ReplaySourceMessage<TestEvent>.FromExclusiveWatermark(10));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("live", source, Watermarked);

        await Assert.ThrowsAsync<ReplayFrameContractException>(async () =>
            await ReadAllAsync(timeline.ReadFramesAsync(ReplayReadOptions.All)));
    }

    [Fact]
    public async Task Custom_key_timestamp_controls_filter_and_frame_timestamp()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("source", new EnumerableReplaySource<TestEvent>([At("event", 999)]), Finite)
            .WithOrdering(new FixedTimestampPolicy(100));

        ReplayFrame<TestEvent> frame = Assert.Single(await ReadAllAsync(timeline.ReadFramesAsync(
            new ReplayReadOptions(DateTimeOffset.UnixEpoch.AddTicks(1), null, null, null))));

        Assert.Equal(100, frame.TimestampNs);
        Assert.Equal(100, Assert.Single(frame.Entries).Key.TimestampNs);
    }

    [Fact]
    public async Task Ordinary_source_cannot_silently_claim_complete_frames()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create().AddSource("ordinary", [At("event", 1)]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReadAllAsync(timeline.ReadFramesAsync(ReplayReadOptions.All)));
    }

    [Fact]
    public void Read_call_seals_timeline_configuration_before_enumeration()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create().AddSource("source", [At("event", 1)]);
        IAsyncEnumerable<TestEvent> read = timeline.ReadAsync(ReplayReadOptions.All);

        Assert.Throws<InvalidOperationException>(() => timeline.AddSource("later", [At("later", 2)]));
        Assert.Throws<InvalidOperationException>(() => timeline.WithOrdering(new FixedTimestampPolicy(1)));
        Assert.NotNull(read);
    }

    [Fact]
    public async Task Single_use_cardinality_applies_to_ordinary_reads_too()
    {
        var singleUse = Finite with { Cardinality = ReplaySourceCardinality.SingleUse };
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("single", new EnumerableReplaySource<TestEvent>([At("event", 1)]), singleUse);
        await ReadAllAsync(timeline.ReadAsync(ReplayReadOptions.All));

        Assert.Throws<InvalidOperationException>(() => timeline.ReadEntriesAsync(ReplayReadOptions.All));
    }

    [Fact]
    public async Task Published_frame_entries_cannot_be_mutated_through_collection_casts()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddFrameSource("source", new EnumerableReplaySource<TestEvent>([At("event", 1)]), Finite);
        ReplayFrame<TestEvent> frame = Assert.Single(await ReadAllAsync(timeline.ReadFramesAsync(ReplayReadOptions.All)));

        Assert.False(frame.Entries is ReplayEntry<TestEvent>[]);
        IList<ReplayEntry<TestEvent>> list = Assert.IsAssignableFrom<IList<ReplayEntry<TestEvent>>>(frame.Entries);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
    }

    [Fact]
    public async Task Every_acquired_source_is_disposed_even_when_disposal_throws()
    {
        var first = new ThrowingDisposeSource(At("first", 1));
        var second = new ThrowingDisposeSource(At("second", 2));
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create()
            .AddSource("first", first)
            .AddSource("second", second);

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await ReadAllAsync(timeline.ReadAsync(ReplayReadOptions.All with { Limit = 1 })));

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
    }

    [Fact]
    public void Duplicate_source_ID_is_rejected()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create().AddSource("duplicate", Array.Empty<TestEvent>());
        Assert.Throws<ArgumentException>(() => timeline.AddSource("duplicate", Array.Empty<TestEvent>()));
    }

    [Fact]
    public async Task Nonpositive_limit_is_rejected_before_enumeration()
    {
        ReplayTimeline<TestEvent> timeline = ReplayTimeline<TestEvent>.Create().AddSource("source", [At("event", 1)]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ReadAllAsync(timeline.ReadAsync(ReplayReadOptions.All with { Limit = 0 })));
    }

    private static TestEvent At(string name, long timestampNs) => new(name) { ExchangeTimestampNs = timestampNs };

    private static async Task<List<T>> ReadAllAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (T item in source) result.Add(item);
        return result;
    }

    private sealed class FixedTimestampPolicy(long timestamp) : IReplayOrderingPolicy<TestEvent>
    {
        public ReplayKey GetKey(TestEvent evt, ReplaySourceInfo source, long sourceSequence) =>
            new(timestamp, source.Priority, 0, source.SourceOrdinal, sourceSequence);
    }

    private sealed class MessageSource(params ReplaySourceMessage<TestEvent>[] messages) : IWatermarkedReplaySource<TestEvent>
    {
        public int MessagesRead { get; private set; }
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<ReplaySourceMessage<TestEvent>> ReadMessagesAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                foreach (ReplaySourceMessage<TestEvent> message in messages)
                {
                    ct.ThrowIfCancellationRequested();
                    MessagesRead++;
                    yield return message;
                    await Task.Yield();
                }
            }
            finally
            {
                DisposeCount++;
            }
        }
    }

    private sealed class ThrowingDisposeSource(TestEvent item) : IReplaySource<TestEvent>
    {
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<TestEvent> ReadAsync(
            ReplayReadOptions options,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
            finally
            {
                DisposeCount++;
                throw new InvalidOperationException("Expected disposal failure.");
            }
        }
    }
}
