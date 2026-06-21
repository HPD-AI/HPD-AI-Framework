using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class ReplayPublisherTests
{
    private sealed record TestEvent(string Name) : Event;

    private sealed class PublisherOnly : IEventPublisher
    {
        public List<Event> Published { get; } = [];

        public void Emit(Event evt) => Published.Add(evt);

        public ValueTask EmitAsync(Event evt, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Published.Add(evt);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_PublishesIntoEventBusInboxInTimelineOrder()
    {
        using var bus = new EventBus();
        await using var inbox = bus.CreateInbox<TestEvent>();
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [At("second", 2)])
            .AddSource("b", [At("first", 1)]);

        await replay.PublishAsync(bus, ReplayReadOptions.All);

        var first = await inbox.Reader.ReadAsync();
        var second = await inbox.Reader.ReadAsync();

        Assert.Equal("first", first.Name);
        Assert.Equal("second", second.Name);
    }

    [Fact]
    public async Task PublishAsync_OnlyRequiresIEventPublisher()
    {
        var publisher = new PublisherOnly();
        var replay = ReplayTimeline<TestEvent>.Create()
            .AddSource("a", [At("a", 1), At("b", 2)]);

        await replay.PublishAsync(publisher, ReplayReadOptions.All);

        Assert.Equal(["a", "b"], publisher.Published.Cast<TestEvent>().Select(static evt => evt.Name));
    }

    [Fact]
    public async Task PublishAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var publisher = new PublisherOnly();
        var replay = ReplayTimeline<TestEvent>.Create().AddSource("a", [At("a", 1)]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => replay.PublishAsync(publisher, ReplayReadOptions.All, cts.Token));
    }

    private static TestEvent At(string name, long exchangeTimestampNs) =>
        new(name) { ExchangeTimestampNs = exchangeTimestampNs };
}
