using HPD.Events;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class TradingHostTests
{
    [Fact]
    public void Constructor_InitializesState()
    {
        var connector = new TestConnector();
        var clock = new TestClock();
        var coordinator = new TestEventCoordinator();
        var engine = new TradingEngine();

        using var host = new TradingHost(connector, clock, coordinator, engine);

        // Host should be created without throwing
        Assert.NotNull(host);
    }

    [Fact]
    public async Task RunAsync_InitializesStrategy()
    {
        // Connector disconnects immediately so RunAsync completes
        var connector = new TestConnector();
        var clock = new TestClock();
        var coordinator = new TestEventCoordinator();
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        using var host = new TradingHost(connector, clock, coordinator, engine);
        await host.RunAsync(strategy);

        Assert.True(strategy.WasInitialized);
    }

    [Fact]
    public async Task RunAsync_StartsConnector()
    {
        var connector = new TestConnector();
        var clock = new TestClock();
        var coordinator = new TestEventCoordinator();
        var engine = new TradingEngine();
        var strategy = new TestStrategy();

        using var host = new TradingHost(connector, clock, coordinator, engine);
        await host.RunAsync(strategy);

        Assert.True(connector.WasStarted);
    }

    [Fact]
    public void Dispose_DisposesConnector()
    {
        var connector = new TestConnector();
        var clock = new TestClock();
        var coordinator = new TestEventCoordinator();
        var engine = new TradingEngine();

        var host = new TradingHost(connector, clock, coordinator, engine);
        host.Dispose();

        Assert.True(connector.WasDisposed);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var connector = new TestConnector();
        var clock = new TestClock();
        var coordinator = new TestEventCoordinator();
        var engine = new TradingEngine();

        var host = new TradingHost(connector, clock, coordinator, engine);
        host.Dispose();
        host.Dispose(); // Should not throw

        Assert.True(connector.WasDisposed);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public long UnixNanos => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

        public ITimerHandle SetAlert(string name, DateTimeOffset alertTime, Action<TimeEvent> callback) =>
            throw new NotImplementedException();

        public ITimerHandle SetAlert(string name, TimeSpan delay, Action<TimeEvent> callback) =>
            throw new NotImplementedException();

        public ITimerHandle SetTimer(string name, TimeSpan interval, Action<TimeEvent> callback,
            DateTimeOffset? startTime = null, DateTimeOffset? stopTime = null) =>
            throw new NotImplementedException();

        public void CancelTimer(string name) { }
        public void CancelAllTimers() { }
        public IEnumerable<string> TimerNames => [];
    }

    private sealed class TestConnector : IConnector
    {
        public ExchangeId Exchange => ExchangeId.Replay;
        public IRateLimiter RateLimiter => NoopRateLimiter.Instance;
        public bool IsConnected { get; private set; }
        public bool WasStarted { get; private set; }
        public bool WasDisposed { get; private set; }

        public Task StartAsync(IEnumerable<Subscription> subscriptions, IEventCoordinator coordinator, CancellationToken ct)
        {
            WasStarted = true;
            IsConnected = true;
            // Disconnect immediately so RunAsync exits
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task SubmitOrderAsync(SubmitOrder command, CancellationToken ct) => Task.CompletedTask;
        public Task CancelOrderAsync(CancelOrder command, CancellationToken ct) => Task.CompletedTask;
        public Task ModifyOrderAsync(ModifyOrder command, CancellationToken ct) => Task.CompletedTask;

        public void Dispose()
        {
            WasDisposed = true;
            IsConnected = false;
        }
    }

    private sealed class TestEventCoordinator : IEventCoordinator
    {
        public void Emit(Event evt) { }
        public ValueTask EmitAsync(Event evt, CancellationToken ct = default) => ValueTask.CompletedTask;
        public IEventCoordinator On<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : Event => this;
        public IEventCoordinator OnAny(Func<Event, ValueTask> handler) => this;
        public bool TryEmitStruct<TEvent>(in TEvent evt) where TEvent : struct, IStructEvent => false;
        public ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : struct, IStructEvent => ValueTask.CompletedTask;
        public IEventCoordinator OnStruct<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : struct, IStructEvent => this;
        public StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null) where TEvent : struct, IStructEvent => default;
        public StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null) where TEvent : struct, IStructEvent => default;
        public Task RunAsync(CancellationToken ct = default) => Task.CompletedTask;

        public EventCoordinatorStats GetStats() => default;
        public IAsyncEnumerable<Event> ReadStreamingAsync(CancellationToken ct = default) => EmptyAsync(ct);
        public IAsyncEnumerable<Event> ReadSynchronousAsync(CancellationToken ct = default) => EmptyAsync(ct);
        public IAsyncEnumerable<Event> ReadInteractiveAsync(CancellationToken ct = default) => EmptyAsync(ct);
        public IAsyncEnumerable<Event> ReadControlAsync(CancellationToken ct = default) => EmptyAsync(ct);

        public void SetParent(IEventCoordinator parent) { }

        public Task<TResponse> WaitForResponseAsync<TResponse>(
            string requestId, TimeSpan timeout, CancellationToken ct = default) where TResponse : Event =>
            throw new NotImplementedException();

        public void SendResponse(string requestId, Event response) { }
        public IStreamRegistry Streams => throw new NotImplementedException();

        private static async IAsyncEnumerable<Event> EmptyAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TestStrategy : StrategyBase
    {
        public bool WasInitialized { get; private set; }
        public int TickCount { get; private set; }

        protected override void OnInitialize()
        {
            WasInitialized = true;
        }

        public override void OnTick()
        {
            TickCount++;
        }
    }
}
