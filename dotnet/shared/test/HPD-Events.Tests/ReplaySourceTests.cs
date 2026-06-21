using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class ReplaySourceTests
{
    private sealed record TestEvent(string Name) : Event;

    [Fact]
    public async Task EnumerableReplaySource_FiltersEvents()
    {
        var source = new EnumerableReplaySource<TestEvent>(
        [
            At("a", 1, "flow-a"),
            At("b", 2, "flow-b"),
            At("c", 3, "flow-a")
        ]);

        var result = await ReadAllAsync(source.ReadAsync(new ReplayReadOptions(null, null, "flow-a", 1)));

        Assert.Equal(["a"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task AsyncEnumerableReplaySource_FiltersEvents()
    {
        var source = new AsyncEnumerableReplaySource<TestEvent>(ReadAsync());

        var result = await ReadAllAsync(source.ReadAsync(new ReplayReadOptions(null, null, null, 2)));

        Assert.Equal(["a", "b"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task InMemoryEventStore_AppendedEventsAreReadableInAppendOrder()
    {
        var store = new InMemoryEventStore<TestEvent>();

        await store.AppendAsync(At("first", 10));
        await store.AppendAsync(At("second", 10));

        var result = await ReadAllAsync(store.ReadAsync(ReplayReadOptions.All));

        Assert.Equal(["first", "second"], result.Select(static evt => evt.Name));
    }

    [Fact]
    public async Task InMemoryEventStore_RespectsCancellation()
    {
        var store = new InMemoryEventStore<TestEvent>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await store.AppendAsync(At("canceled", 1), cts.Token));
    }

    private static async IAsyncEnumerable<TestEvent> ReadAsync()
    {
        yield return At("a", 1);
        await Task.Yield();
        yield return At("b", 2);
        yield return At("c", 3);
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
