using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

public class FinanceReplayOrderingPolicyTests
{
    private static readonly Instrument Instrument = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public async Task ReplayTimeline_WithFinancePolicy_OrdersSameTimestampByFinanceSemantics()
    {
        var time = Instant.FromUnixSeconds(1);
        var replay = ReplayTimeline<FinanceEvent>.Create()
            .AddSource("bars", [Bar(time)])
            .AddSource("trades", [Trade(time)])
            .AddSource("quotes", [Quote(time)])
            .AddSource("books", [Book(time)])
            .AddSource("execution", [Accepted(time)])
            .AddSource("lifecycle", [new MarketOpened(Venue.NASDAQ) { Time = time }])
            .WithOrdering(FinanceReplayOrderingPolicy.Default);

        var result = await ReadAllAsync(replay.ReadAsync(ReplayReadOptions.All));

        Assert.Collection(
            result,
            evt => Assert.IsType<MarketOpened>(evt),
            evt => Assert.IsType<BookUpdated>(evt),
            evt => Assert.IsType<QuoteReceived>(evt),
            evt => Assert.IsType<TradeOccurred>(evt),
            evt => Assert.IsType<BarClosed>(evt),
            evt => Assert.IsType<OrderAccepted>(evt));
    }

    [Fact]
    public void GetKey_UsesExchangeTimestampBeforeTimestampFallback()
    {
        var timestamp = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var evt = new TestFinanceEvent { Timestamp = timestamp, ExchangeTimestampNs = 42 };
        var source = new ReplaySourceInfo("source", Priority: 0, SourceOrdinal: 0);

        var key = FinanceReplayOrderingPolicy.Default.GetKey(evt, source, sourceSequence: 0);

        Assert.Equal(42, key.TimestampNs);
    }

    [Fact]
    public void GetKey_PreservesSourceTieBreakersAndEventSequence()
    {
        var evt = new TestFinanceEvent { Time = Instant.FromUnixSeconds(1), SequenceNumber = 99 };
        var source = new ReplaySourceInfo("secondary", Priority: 7, SourceOrdinal: 3);

        var key = FinanceReplayOrderingPolicy.Default.GetKey(evt, source, sourceSequence: 11);

        Assert.Equal(1_000_000_000, key.TimestampNs);
        Assert.Equal(7, key.SourcePriority);
        Assert.Equal(100, key.EventPriority);
        Assert.Equal(3, key.SourceOrdinal);
        Assert.Equal(11, key.SourceSequence);
        Assert.Equal(99, key.EventSequenceNumber);
    }

    private static BookUpdated Book(Instant time)
    {
        var book = new Book
        {
            Instrument = Instrument,
            Time = time,
            Bids = [new Level(new Price(100m, Currency.USD), new Qty(10m), OrderCount: 2)],
            Asks = [new Level(new Price(101m, Currency.USD), new Qty(20m), OrderCount: 3)]
        };

        return new BookUpdated(Instrument, book) { Time = time };
    }

    private static QuoteReceived Quote(Instant time)
    {
        var quote = new Quote(
            new Price(100m, Currency.USD),
            new Price(101m, Currency.USD),
            new Qty(10m),
            new Qty(20m),
            DualTimestamp.Synchronized(time));

        return new QuoteReceived(Instrument, quote) { Time = time };
    }

    private static TradeOccurred Trade(Instant time)
    {
        var trade = new Trade(
            new Price(100.5m, Currency.USD),
            new Qty(5m),
            Side.Buy,
            DualTimestamp.Synchronized(time));

        return new TradeOccurred(Instrument, trade) { Time = time };
    }

    private static BarClosed Bar(Instant time)
    {
        var bar = Rhodium.Primitives.Bar.Create(
            new Price(100m, Currency.USD),
            new Qty(1000m),
            time,
            Duration.FromMinutes(1));

        return new BarClosed(Instrument, bar) { Time = time };
    }

    private static OrderAccepted Accepted(Instant time)
        => new(OrderId.New(), StrategyId.New(), VariantId: 0) { Time = time };

    private static async Task<List<TEvent>> ReadAllAsync<TEvent>(IAsyncEnumerable<TEvent> events)
    {
        var result = new List<TEvent>();
        await foreach (var evt in events)
            result.Add(evt);

        return result;
    }
}
