using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

public class QuoteReceivedTests
{
    [Fact]
    public void QuoteReceived_ShouldStoreInstrumentAndQuote()
    {
        // Arrange
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var quote = new Quote(
            new Price(100.50m, Currency.USD),
            new Price(100.51m, Currency.USD),
            new Qty(1000m),
            new Qty(500m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var evt = new QuoteReceived(instrument, quote);

        // Assert
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(quote, evt.Quote);
    }

    [Fact]
    public void QuoteReceived_ShouldBeMarketEvent()
    {
        // Arrange
        var instrument = new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NYSE);
        var quote = Quote.Empty(DualTimestamp.Synchronized(Instant.Now));

        // Act
        var evt = new QuoteReceived(instrument, quote);

        // Assert
        Assert.IsAssignableFrom<MarketEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }
}

public class TradeOccurredTests
{
    [Fact]
    public void TradeOccurred_ShouldStoreInstrumentAndTrade()
    {
        // Arrange
        var instrument = new Instrument(new Asset("TSLA", AssetClass.Equity), Venue.NASDAQ);
        var trade = new Trade(
            new Price(250.75m),
            new Qty(100m),
            Side.Buy,
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var evt = new TradeOccurred(instrument, trade);

        // Assert
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(trade, evt.Trade);
    }

    [Fact]
    public void TradeOccurred_ShouldBeMarketEvent()
    {
        // Arrange
        var instrument = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var trade = new Trade(
            new Price(50000m),
            new Qty(0.5m),
            Side.Sell,
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var evt = new TradeOccurred(instrument, trade);

        // Assert
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }
}

public class BarClosedTests
{
    [Fact]
    public void BarClosed_ShouldStoreInstrumentAndBar()
    {
        // Arrange
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var bar = new Bar(
            new Price(400m),
            new Price(402m),
            new Price(399m),
            new Price(401m),
            new Qty(50000m),
            Instant.Now,
            Duration.FromMinutes(5)
        );

        // Act
        var evt = new BarClosed(instrument, bar);

        // Assert
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(bar, evt.Bar);
    }

    [Fact]
    public void BarClosed_ShouldBeMarketEvent()
    {
        // Arrange
        var instrument = new Instrument(new Asset("QQQ", AssetClass.Equity), Venue.NASDAQ);
        var bar = Bar.Create(new Price(350m), new Qty(1000m), Instant.Now, Duration.FromMinutes(1));

        // Act
        var evt = new BarClosed(instrument, bar);

        // Assert
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }

    [Fact]
    public void BarClosed_ShouldHaveStreamingChannel()
    {
        // Arrange
        var instrument = new Instrument(new Asset("ETH", AssetClass.Crypto), Venue.Coinbase);
        var bar = Bar.Create(new Price(3000m), new Qty(10m), Instant.Now, Duration.FromHours(1));

        // Act
        var evt = new BarClosed(instrument, bar);

        // Assert
        Assert.Equal(HPD.Events.EventChannel.Streaming, evt.Channel);
    }
}

public class BookUpdatedTests
{
    [Fact]
    public void BookUpdated_ShouldStoreInstrumentAndBook()
    {
        // Arrange
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var book = new Book
        {
            Instrument = instrument,
            Time = Instant.Now,
            Bids =
            [
                new Level(new Price(150.00m), new Qty(1000m), 5),
                new Level(new Price(149.99m), new Qty(500m), 3)
            ],
            Asks =
            [
                new Level(new Price(150.01m), new Qty(800m), 4),
                new Level(new Price(150.02m), new Qty(300m), 2)
            ]
        };

        // Act
        var evt = new BookUpdated(instrument, book);

        // Assert
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(book, evt.Book);
    }

    [Fact]
    public void BookUpdated_ShouldBeMarketEvent()
    {
        // Arrange
        var instrument = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var book = Book.Empty(instrument, Instant.Now);

        // Act
        var evt = new BookUpdated(instrument, book);

        // Assert
        Assert.IsAssignableFrom<MarketEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void BookUpdated_ShouldHaveStreamingChannel()
    {
        // Arrange
        var instrument = new Instrument(new Asset("TSLA", AssetClass.Equity), Venue.NASDAQ);
        var book = Book.Empty(instrument, Instant.Now);

        // Act
        var evt = new BookUpdated(instrument, book);

        // Assert
        Assert.Equal(HPD.Events.EventChannel.Streaming, evt.Channel);
    }

    [Fact]
    public void BookUpdated_WithCompleteBook()
    {
        // Arrange
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var book = new Book
        {
            Instrument = instrument,
            Time = Instant.Now,
            Bids =
            [
                new Level(new Price(400.00m), new Qty(10000m), 10),
                new Level(new Price(399.99m), new Qty(5000m), 5),
                new Level(new Price(399.98m), new Qty(3000m), 3)
            ],
            Asks =
            [
                new Level(new Price(400.01m), new Qty(8000m), 8),
                new Level(new Price(400.02m), new Qty(4000m), 4),
                new Level(new Price(400.03m), new Qty(2000m), 2)
            ]
        };

        // Act
        var evt = new BookUpdated(instrument, book);

        // Assert
        Assert.Equal(3, evt.Book.Bids.Length);
        Assert.Equal(3, evt.Book.Asks.Length);
        Assert.Equal(new Price(400.00m), evt.Book.BestBid?.Price);
        Assert.Equal(new Price(400.01m), evt.Book.BestAsk?.Price);
    }
}

public class BookDeltaReceivedTests
{
    [Fact]
    public void BookDeltaReceived_ShouldStoreInstrumentAndDelta()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var delta = new BookDelta(Side.Buy, new Price(150m), new Qty(100m), BookAction.Add, Sequence: 7);

        var evt = new BookDeltaReceived(instrument, delta);

        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(delta, evt.Delta);
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }
}

public class BookDeltasReceivedTests
{
    [Fact]
    public void BookDeltasReceived_ShouldStoreOrderedDeltas()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var deltas = new[]
        {
            new BookDelta(Side.Buy, new Price(150m), new Qty(100m), BookAction.Add, Sequence: 1),
            new BookDelta(Side.Buy, new Price(150m), new Qty(125m), BookAction.Update, Sequence: 2)
        };

        var evt = new BookDeltasReceived(instrument, deltas);

        Assert.Equal(instrument, evt.Instrument);
        Assert.Same(deltas, evt.Deltas);
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }
}

public class BookDepthSnapshotReceivedTests
{
    [Fact]
    public void BookDepthSnapshotReceived_ShouldStoreFixedDepthLevels()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var bids = new[] { new Level(new Price(150m), new Qty(100m), 3) };
        var asks = new[] { new Level(new Price(151m), new Qty(90m), 2) };

        var evt = new BookDepthSnapshotReceived(instrument, bids, asks, Depth: 10, VenueSequence: 99, Flags: 1);

        Assert.Equal(instrument, evt.Instrument);
        Assert.Same(bids, evt.Bids);
        Assert.Same(asks, evt.Asks);
        Assert.Equal(10, evt.Depth);
        Assert.Equal(99, evt.VenueSequence);
        Assert.Equal(1, evt.Flags);
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }
}
