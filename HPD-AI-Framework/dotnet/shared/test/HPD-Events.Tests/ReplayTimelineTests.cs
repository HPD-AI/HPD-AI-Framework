using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class ReplayTimelineTests
{
    private sealed record TestEvent(string Name) : Event;

    private sealed class PriorityPolicy : IReplayOrderingPolicy<TestEvent>
    {
        public ReplayKey GetKey(TestEvent evt, ReplaySourceInfo source, long sourceSequence) =>
            new(
                evt.ExchangeTimestampNs,
                source.Priority,
                evt.Name.StartsWith("priority", StringComparison.Ordinal) ? -10 : 0,
                source.SourceOrdinal,
                sourceSequence,
                evt.SequenceNumber);
    }

    [Fact]
    public async Task SingleSource_PreservesSourceOrder()
    {
        var events = new[]
        {
            At("third", 30),
            At("first", 10),
            At("second", 20)
        };

        var replay = ReplayTimeline<TestEvent>.Create().AddSource("fixture", events);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["third", "first", "second"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task EmptySources_Complete()
    {
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("empty-a", [])
            .AddSource("empty-b", []);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultipleSources_MergeByTimestamp()
    {
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [At("a-20", 20), At("a-40", 40)])
            .AddSource("b", [At("b-10", 10), At("b-30", 30)]);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["b-10", "a-20", "b-30", "a-40"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task SameTimestamp_UsesSourcePriorityThenSourceSequence()
    {
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("later", [At("later-1", 10), At("later-2", 10)], priority: 10)
            .AddSource("earlier", [At("earlier-1", 10), At("earlier-2", 10)], priority: 0);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["earlier-1", "earlier-2", "later-1", "later-2"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task SameTimestamp_UsesEventPriority()
    {
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [At("normal", 10)])
            .AddSource("b", [At("priority-event", 10)])
            .WithOrdering(new PriorityPolicy());

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["priority-event", "normal"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task SameFullKey_UsesSourceOrdinal()
    {
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [At("a", 10)])
            .AddSource("b", [At("b", 10)]);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["a", "b"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task ExchangeTimestampNs_BeatsTimestamp()
    {
        var lateTimestamp = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var earlyTimestamp = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [new TestEvent("exchange-first") { Timestamp = lateTimestamp, ExchangeTimestampNs = 5 }])
            .AddSource("b", [new TestEvent("timestamp-second") { Timestamp = earlyTimestamp, ExchangeTimestampNs = 0 }]);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["exchange-first", "timestamp-second"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task TimestampFallback_WorksWhenExchangeTimestampIsZero()
    {
        var earlier = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = earlier.AddSeconds(1);
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [new TestEvent("later") { Timestamp = later }])
            .AddSource("b", [new TestEvent("earlier") { Timestamp = earlier }]);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["earlier", "later"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task ReadOptions_FilterAndLimit()
    {
        var from = DateTimeOffset.UnixEpoch.AddSeconds(2);
        var to = DateTimeOffset.UnixEpoch.AddSeconds(5);
        var options = new ReplayReadOptions(from, to, "flow-a", Limit: 2);
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a",
            [
                At("too-early", 1_000_000_000, "flow-a"),
                At("included-2", 2_000_000_000, "flow-a"),
                At("wrong-flow", 3_000_000_000, "flow-b"),
                At("included-4", 4_000_000_000, "flow-a"),
                At("limited-out", 4_500_000_000, "flow-a"),
                At("too-late", 5_000_000_000, "flow-a")
            ]);

        var result = await ReadAllAsync(replay.ReadAsync(options));

        Assert.Equal(["included-2", "included-4"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task Cancellation_StopsReplay()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var replay = ReplayTimeline<TestEvent>.Create().AddSource("a", [At("a", 1)]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All, cts.Token)));
    }

    private static TestEvent At(string name, long exchangeTimestampNs, string? eventFlowId = null) =>
        new(name)
        {
            ExchangeTimestampNs = exchangeTimestampNs,
            EventFlowId = eventFlowId
        };

    private static async Task<List<TEvent>> ReadAllAsync<TEvent>(IAsyncEnumerable<TEvent> events)
    {
        var result = new List<TEvent>();
        await foreach (var evt in events)
            result.Add(evt);

        return result;
    }
}
