using HPD.Events;
using Rhodium.Connectivity.Simulation;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class ReplayConnectorTests
{
    private static Instrument TestInstrument => new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    private static async IAsyncEnumerable<FinanceEvent> CreateEmptyHistory()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<FinanceEvent> CreateHistoryWithQuotes(int count)
    {
        var time = DualTimestamp.Synchronized(Instant.Now);
        for (int i = 0; i < count; i++)
        {
            yield return new QuoteReceived(
                TestInstrument,
                new Quote(
                    new Price(100m + i * 0.01m, Currency.USD),
                    new Price(100.05m + i * 0.01m, Currency.USD),
                    new Qty(100m),
                    new Qty(100m),
                    time));
            await Task.Yield();
        }
    }

    [Fact]
    public void Constructor_SetsDefaultConfig()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
        Assert.IsType<NoopRateLimiter>(connector.RateLimiter);
        Assert.False(connector.IsConnected);
    }

    [Fact]
    public void Constructor_AcceptsCustomConfig()
    {
        var config = SimulationConfig.Instant();
        var fillModel = new DefaultFillModel();
        var riskGuard = new DefaultRiskGuard { MaxOrderSize = 500m };

        var connector = new ReplayConnector(
            CreateEmptyHistory(),
            config,
            fillModel,
            riskGuard);

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
    }

    [Fact]
    public async Task StartAsync_CompletesWithEmptyHistory()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var coordinator = new TestEventCoordinator();

        await connector.StartAsync([], coordinator, CancellationToken.None);

        Assert.False(connector.IsConnected); // Should be false after completion
        Assert.Empty(coordinator.EmittedEvents);
    }

    [Fact]
    public async Task StartAsync_EmitsAllHistoryEvents()
    {
        var connector = new ReplayConnector(CreateHistoryWithQuotes(5));
        var coordinator = new TestEventCoordinator();
        var subscriptions = new[] { new Subscription(TestInstrument, SubscriptionType.Quotes) };

        await connector.StartAsync(subscriptions, coordinator, CancellationToken.None);

        Assert.Equal(5, coordinator.EmittedEvents.Count);
        Assert.All(coordinator.EmittedEvents, e => Assert.IsType<QuoteReceived>(e));
    }

    [Fact]
    public async Task StartAsync_CanBeCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var connector = new ReplayConnector(CreateHistoryWithQuotes(100));
        var coordinator = new TestEventCoordinator();

        // Should either throw OperationCanceledException or complete quickly
        // (behavior depends on how the async enumerable handles cancellation)
        try
        {
            await connector.StartAsync([], coordinator, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        Assert.False(connector.IsConnected);
    }

    [Fact]
    public async Task SubmitOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var order = CreateSubmitOrder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.SubmitOrderAsync(order, CancellationToken.None));
    }

    [Fact]
    public async Task CancelOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var cancel = new CancelOrder { OrderId = OrderId.New() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.CancelOrderAsync(cancel, CancellationToken.None));
    }

    [Fact]
    public async Task ModifyOrderAsync_ThrowsWhenNotStarted()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());
        var modify = new ModifyOrder { OrderId = OrderId.New() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.ModifyOrderAsync(modify, CancellationToken.None));
    }

    [Fact]
    public void Dispose_ClearsState()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        connector.Dispose();

        Assert.False(connector.IsConnected);
    }

    [Fact]
    public void Exchange_ReturnsReplay()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.Equal(ExchangeId.Replay, connector.Exchange);
    }

    [Fact]
    public void RateLimiter_ReturnsNoopRateLimiter()
    {
        var connector = new ReplayConnector(CreateEmptyHistory());

        Assert.IsType<NoopRateLimiter>(connector.RateLimiter);
        Assert.Same(NoopRateLimiter.Instance, connector.RateLimiter);
    }

    private static SubmitOrder CreateSubmitOrder(
        decimal qty = 100m,
        decimal limitPrice = 100m,
        OrderType type = OrderType.Limit)
    {
        return new SubmitOrder
        {
            OrderId = OrderId.New(),
            Instrument = TestInstrument,
            VariantId = 0,
            Side = Side.Buy,
            Quantity = new Qty(qty),
            Type = type,
            LimitPrice = new Price(limitPrice, Currency.USD)
        };
    }

    /// <summary>
    /// Simple test coordinator for capturing emitted events.
    /// </summary>
    private sealed class TestEventCoordinator : IEventCoordinator
    {
        public List<Event> EmittedEvents { get; } = [];

        public void Emit(Event evt) => EmittedEvents.Add(evt);
        public ValueTask EmitAsync(Event evt, CancellationToken ct = default) { Emit(evt); return ValueTask.CompletedTask; }
        public IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler, EventSubscriptionOptions? options = null) where TEvent : Event => new NoopSubscription();
        public IDisposable SubscribeAny(Func<Event, ValueTask> handler, EventSubscriptionOptions? options = null) => new NoopSubscription();
        public EventStreamSubscription<TEvent> SubscribeStream<TEvent>(EventSubscriptionOptions? options = null) where TEvent : Event => default;
        public EventStreamSubscription<Event> SubscribeChannel(EventChannel channel, EventSubscriptionOptions? options = null) => default;
        public bool TryEmitStruct<TEvent>(in TEvent evt) where TEvent : struct, IStructEvent => false;
        public ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : struct, IStructEvent => ValueTask.CompletedTask;
        public IDisposable SubscribeStruct<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : struct, IStructEvent => new NoopSubscription();
        public StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null) where TEvent : struct, IStructEvent => default;
        public StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null) where TEvent : struct, IStructEvent => default;

        public EventCoordinatorStats GetStats() => default;

        public void SetParent(IEventCoordinator parent) { }

        public Task<TResponse> WaitForResponseAsync<TResponse>(
            string requestId,
            TimeSpan timeout,
            CancellationToken ct = default) where TResponse : Event =>
            throw new NotImplementedException();

        public void SendResponse(string requestId, Event response) { }

        public IStreamRegistry Streams => throw new NotImplementedException();
    }

    private sealed class NoopSubscription : IDisposable
    {
        public void Dispose() { }
    }
}
