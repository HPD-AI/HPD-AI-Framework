using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Base.Realtime.AspNetCore.Observability.Logging;
using HPD.Base.Runtime;
using HPD.Base.Tests.Observability;
using Microsoft.Extensions.Logging;

namespace HPD.Base.Realtime.AspNetCore.Tests.Endpoints;

public sealed class RealtimeBackpressureTests
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
        var slowSendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var healthySent = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTerminated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTerminations = 0;
        var pumpFailures = 0;

        async Task SendSlowAsync(
            string channel,
            BaseRealtimeEvent item,
            CancellationToken token)
        {
            await sharedSend.WaitAsync(token);
            try
            {
                slowSendStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                sharedSend.Release();
            }
        }

        async Task SendHealthyAsync(
            string channel,
            BaseRealtimeEvent item,
            CancellationToken token)
        {
            await sharedSend.WaitAsync(token);
            try
            {
                healthySent.TrySetResult();
            }
            finally
            {
                sharedSend.Release();
            }
        }

        await using var slow = new BaseRealtimeChannelOwner(
            "slow-marker",
            ReadAsync(slowSource.Reader),
            1,
            cancellation.Token,
            SendSlowAsync,
            (_, _, _) =>
            {
                Interlocked.Increment(ref pumpFailures);
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                Interlocked.Increment(ref slowTerminations);
                slowTerminated.TrySetResult();
                return Task.CompletedTask;
            });
        await using var healthy = new BaseRealtimeChannelOwner(
            "healthy-marker",
            ReadAsync(healthySource.Reader),
            1,
            cancellation.Token,
            SendHealthyAsync,
            (_, _, _) =>
            {
                Interlocked.Increment(ref pumpFailures);
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);

        slow.Activate();
        healthy.Activate();
        slowSource.Writer.TryWrite(Event("slow-1")).Should().BeTrue();
        await slowSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        slowSource.Writer.TryWrite(Event("slow-2")).Should().BeTrue();
        slowSource.Writer.TryWrite(Event("slow-3")).Should().BeTrue();
        await slowTerminated.Task.WaitAsync(TimeSpan.FromSeconds(3));

        healthySource.Writer.TryWrite(Event("healthy")).Should().BeTrue();
        await healthySent.Task.WaitAsync(TimeSpan.FromSeconds(3));

        slowTerminations.Should().Be(1);
        pumpFailures.Should().Be(0);
    }

    [Fact]
    public async Task EventSendTimeoutTerminatesChannelWithoutGenericFailureLogs()
    {
        var time = new ManualTimerProvider();
        var feed = new TrackingRealtimeFeedSource();
        var socket = new ScriptedSlowWebSocket(JoinMessage());
        var stats = new BaseRealtimeStats();
        using var logs = new LogCollector();
        using var cancellation = new CancellationTokenSource();
        var session = new BaseRealtimeWebSocketSession(
            socket,
            feed,
            new JsonSerializerOptions(),
            new HPD.Base.Realtime.Configuration.BaseRealtimeOptions
            {
                Limits = new BaseRealtimeLimits
                {
                    SendTimeoutSeconds = 2
                }
            },
            stats,
            new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "subject-marker"
            },
            logs.CreateLogger<BaseRealtimeWebSocketSession>(),
            time);

        var run = session.RunAsync(cancellation.Token);
        await feed.EnumerationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        feed.Emit(Event("event-marker")).Should().BeTrue();
        await socket.EventSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        time.Advance(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => stats.SlowConsumerTerminations == 1);
        await cancellation.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(3));

        stats.SlowConsumerTerminations.Should().Be(1);
        logs.RecordsFor(5511).Should().ContainSingle();
        logs.RecordsFor(5501).Should().BeEmpty();
        logs.RecordsFor(5505).Should().BeEmpty();
        logs.Records.Should().NotContain(record =>
            record.RenderedMessage.Contains("subject-marker", StringComparison.Ordinal)
            || record.RenderedMessage.Contains("event-marker", StringComparison.Ordinal));
    }

    private static async IAsyncEnumerable<BaseRealtimeEvent> ReadAsync(
        ChannelReader<BaseRealtimeEvent> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    private static BaseRealtimeEvent Event(string id) => new()
    {
        EventId = id,
        Type = "record.created",
        SchemaVersion = "1",
        OccurredAt = DateTimeOffset.UnixEpoch,
        Resource = new BaseRealtimeRecordResource
        {
            CollectionId = "items",
            RecordId = new RecordId(id)
        },
        Operation = BaseOperationKind.Create
    };

    private static byte[] JoinMessage() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new BaseRealtimeClientMessage
            {
                Type = BaseRealtimeProtocolTypes.Join,
                Ref = "join-marker",
                Channel = "channel-marker",
                Config = new BaseRealtimeChannelJoinRequest
                {
                    Kind = BaseRealtimeChannelKinds.RecordChanges,
                    Private = false,
                    CollectionId = "items"
                }
            },
            HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class ManualTimestampProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public void Advance(TimeSpan elapsed) =>
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private sealed class ManualTimerProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UnixEpoch.AddTicks(Volatile.Read(ref _timestamp));

        public override long GetTimestamp() => Volatile.Read(ref _timestamp);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_sync)
            {
                _timers.Add(timer);
                timer.Change(dueTime, period);
            }

            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
            ManualTimer[] timers;
            lock (_sync)
                timers = _timers.ToArray();

            foreach (var timer in timers)
                timer.FireIfDue(_timestamp);
        }

        private sealed class ManualTimer(
            ManualTimerProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private long _dueAt = long.MaxValue;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private int _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return false;

                _period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : owner.GetTimestamp() + dueTime.Ticks;
                return true;
            }

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(long now)
            {
                if (Volatile.Read(ref _disposed) != 0 || now < Volatile.Read(ref _dueAt))
                    return;

                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : now + _period.Ticks;
                callback(state);
            }
        }
    }

    private sealed class ScriptedSlowWebSocket(byte[] firstMessage) : WebSocket
    {
        private int _receiveCount;
        private int _sendCount;
        private WebSocketState _state = WebSocketState.Open;

        public TaskCompletionSource EventSendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _receiveCount) == 1)
            {
                firstMessage.AsSpan().CopyTo(buffer.AsSpan());
                return Task.FromResult(new WebSocketReceiveResult(
                    firstMessage.Length,
                    WebSocketMessageType.Text,
                    true));
            }

            return WaitForCancellationAsync<WebSocketReceiveResult>(cancellationToken);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _sendCount) <= 2)
                return Task.CompletedTask;

            EventSendStarted.TrySetResult();
            return WaitForCancellationAsync(cancellationToken);
        }

        private static async Task WaitForCancellationAsync(CancellationToken cancellationToken) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        private static async Task<T> WaitForCancellationAsync<T>(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation wait completed unexpectedly.");
        }
    }
}
