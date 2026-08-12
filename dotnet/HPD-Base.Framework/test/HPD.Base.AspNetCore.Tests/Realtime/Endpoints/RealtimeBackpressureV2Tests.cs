using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Base.AspNetCore.Tests.Realtime.Endpoints;

public sealed class RealtimeBackpressureV2Tests
{
    [Fact]
    public void JoinLimiterUsesAnExactFixedOneSecondWindow()
    {
        var time = new ManualTimestampProvider();
        var limiter = new BaseRealtimeJoinRateLimiter(time, 2);
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeTrue();
        limiter.TryAcquire().Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(999));
        limiter.TryAcquire().Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        limiter.TryAcquire().Should().BeTrue();
    }

    [Fact]
    public async Task FullOutboundQueueTerminatesOnlyTheSlowChannel()
    {
        var slowSource = Channel.CreateUnbounded<BaseRealtimeEvent>();
        var healthySource = Channel.CreateUnbounded<BaseRealtimeEvent>();
        using var cancellation = new CancellationTokenSource();
        var sharedSend = new SemaphoreSlim(1, 1);
        var slowSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthySent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTerminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminations = 0;

        async Task BlockedSend(string _, BaseRealtimeEvent __, CancellationToken token)
        {
            await sharedSend.WaitAsync(token);
            try { slowSendStarted.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { sharedSend.Release(); }
        }
        async Task HealthySend(string _, BaseRealtimeEvent __, CancellationToken token)
        {
            await sharedSend.WaitAsync(token); try { healthySent.TrySetResult(); } finally { sharedSend.Release(); }
        }

        await using var slow = new BaseRealtimeChannelOwner("slow", ReadAsync(slowSource.Reader), 1, cancellation.Token, BlockedSend, (_, _, _) => Task.CompletedTask, (_, _) => { Interlocked.Increment(ref terminations); slowTerminated.TrySetResult(); return Task.CompletedTask; });
        await using var healthy = new BaseRealtimeChannelOwner("healthy", ReadAsync(healthySource.Reader), 1, cancellation.Token, HealthySend, (_, _, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        slow.Activate(); healthy.Activate();
        slowSource.Writer.TryWrite(Event("slow-1")).Should().BeTrue();
        await slowSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        slowSource.Writer.TryWrite(Event("slow-2")).Should().BeTrue();
        slowSource.Writer.TryWrite(Event("slow-3")).Should().BeTrue();
        await slowTerminated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        healthySource.Writer.TryWrite(Event("healthy")).Should().BeTrue();
        await healthySent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        terminations.Should().Be(1);
    }

    private static async IAsyncEnumerable<BaseRealtimeEvent> ReadAsync(ChannelReader<BaseRealtimeEvent> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    { await foreach (BaseRealtimeEvent item in reader.ReadAllAsync(cancellationToken)) yield return item; }

    private static BaseRealtimeEvent Event(string id) => new() { EventId = id, Type = "record.created", SchemaVersion = "1", OccurredAt = DateTimeOffset.UnixEpoch, Resource = new BaseRealtimeRecordResource { CollectionId = "items", RecordId = new RecordId(id) }, Operation = BaseOperationKind.Create };

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
