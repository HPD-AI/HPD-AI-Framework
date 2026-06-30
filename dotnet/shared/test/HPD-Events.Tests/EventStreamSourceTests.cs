using HPD.Events;
using HPD.Events.Core;

namespace HPD.Events.Tests;

public class EventStreamSourceTests
{
    private record TestEvent(string Message) : Event;
    private sealed record DerivedTestEvent(string Message) : TestEvent(Message);

    private sealed record StreamingEvent(string Message) : Event
    {
        public override EventChannel Channel { get; init; } = EventChannel.Streaming;
    }

    [Fact]
    public async Task OpenAsync_ReturnsValidationFailureForInvalidCapacity()
    {
        using var coordinator = new EventCoordinator();
        var source = new EventStreamSource<TestEvent>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>
        {
            Capacity = 0
        });

        Assert.Equal(AsyncStreamOpenStatus.ValidationFailed, result.Status);
        Assert.Equal("event.stream.capacity.invalid", result.Error!.Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task OpenAsync_ReturnsValidationFailureForUnsupportedBackpressure()
    {
        using var coordinator = new EventCoordinator();
        var source = new EventStreamSource<TestEvent>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>
        {
            Backpressure = AsyncStreamBackpressureMode.Unspecified
        });

        Assert.Equal(AsyncStreamOpenStatus.ValidationFailed, result.Status);
        Assert.Equal("event.stream.backpressure.invalid", result.Error!.Code);
    }

    [Fact]
    public async Task OpenAsync_ReturnsCancelledWhenTokenIsAlreadyCancelled()
    {
        using var coordinator = new EventCoordinator();
        var source = new EventStreamSource<TestEvent>(coordinator);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>(), cts.Token);

        Assert.Equal(AsyncStreamOpenStatus.Cancelled, result.Status);
        Assert.Equal(AsyncStreamErrorCategory.Cancellation, result.Error!.Category);
    }

    [Fact]
    public async Task OpenAsync_CreatesLiveEventStreamWithDescriptor()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var source = new EventStreamSource<TestEvent>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>
        {
            StreamId = "tests",
            Backpressure = AsyncStreamBackpressureMode.DropWrite
        }, cts.Token);

        Assert.True(result.Succeeded);
        Assert.Equal("tests", result.Value!.Descriptor.StreamId);
        Assert.False(result.Value.Descriptor.Replayable);
        Assert.False(result.Value.Descriptor.Resumable);
        Assert.Equal(AsyncStreamBackpressureMode.DropWrite, result.Value.Descriptor.Backpressure);
        Assert.Equal(AsyncStreamDeliveryGuarantee.AtMostOnce, result.Value.Descriptor.DeliveryGuarantee);

        await using var enumerator = result.Value.Items.GetAsyncEnumerator(cts.Token);
        coordinator.Emit(new TestEvent("live"));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("live", enumerator.Current.Message);
    }

    [Fact]
    public async Task Stream_HonorsChannelFilter()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var source = new EventStreamSource<Event>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<Event>
        {
            Channel = EventChannel.Streaming
        }, cts.Token);

        await using var enumerator = result.Value!.Items.GetAsyncEnumerator(cts.Token);
        coordinator.Emit(new TestEvent("sync"));
        coordinator.Emit(new StreamingEvent("stream"));

        Assert.True(await enumerator.MoveNextAsync());
        var evt = Assert.IsType<StreamingEvent>(enumerator.Current);
        Assert.Equal("stream", evt.Message);
    }

    [Fact]
    public async Task Stream_HonorsDerivedTypeOption()
    {
        using var coordinator = new EventCoordinator();
        var source = new EventStreamSource<TestEvent>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>
        {
            IncludeDerivedTypes = false
        });

        await using var enumerator = result.Value!.Items.GetAsyncEnumerator();
        coordinator.Emit(new DerivedTestEvent("derived"));
        coordinator.Emit(new TestEvent("base"));

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("base", enumerator.Current.Message);
    }

    [Fact]
    public async Task DisposingStreamEnumerator_DisposesUnderlyingInbox()
    {
        using var coordinator = new EventCoordinator();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var source = new EventStreamSource<TestEvent>(coordinator);

        var result = await source.OpenAsync(new EventStreamRequest<TestEvent>(), cts.Token);

        Assert.Equal(1, coordinator.GetStats().InboxCount);

        var enumerator = result.Value!.Items.GetAsyncEnumerator(cts.Token);
        coordinator.Emit(new TestEvent("one"));
        Assert.True(await enumerator.MoveNextAsync());

        await enumerator.DisposeAsync();

        Assert.Equal(0, coordinator.GetStats().InboxCount);
    }
}
