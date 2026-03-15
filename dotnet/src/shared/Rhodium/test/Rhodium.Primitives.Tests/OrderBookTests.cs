using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class OrderBookTests
{
    private static readonly Instrument BTC = new(new Asset("BTC", AssetClass.Crypto), Venue.Coinbase);
    private static readonly Instant Now = new(1000000000L);

    [Fact]
    public void Level_StoresPropertiesCorrectly()
    {
        var level = new Level(new Price(100m), new Qty(50m), 3);

        Assert.Equal(100m, level.Price.Value);
        Assert.Equal(50m, level.Size.Value);
        Assert.Equal(3, level.OrderCount);
    }

    [Fact]
    public void Level_DefaultOrderCountIsZero()
    {
        var level = new Level(new Price(100m), new Qty(50m));

        Assert.Equal(0, level.OrderCount);
    }

    [Fact]
    public void Book_EmptyReturnsEmptyBook()
    {
        var book = Book.Empty(BTC, Now);

        Assert.Equal(BTC, book.Instrument);
        Assert.Equal(Now, book.Time);
        Assert.Empty(book.Bids);
        Assert.Empty(book.Asks);
    }

    [Fact]
    public void Book_BestBidReturnsFirstBid()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(10m))],
            Asks = []
        };

        Assert.NotNull(book.BestBid);
        Assert.Equal(100m, book.BestBid.Value.Price.Value);
    }

    [Fact]
    public void Book_BestAskReturnsFirstAsk()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [],
            Asks = [new Level(new Price(101m), new Qty(10m))]
        };

        Assert.NotNull(book.BestAsk);
        Assert.Equal(101m, book.BestAsk.Value.Price.Value);
    }

    [Fact]
    public void Book_BidAskPropertiesExtractPrices()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(10m))],
            Asks = [new Level(new Price(101m), new Qty(10m))]
        };

        Assert.Equal(100m, book.Bid.Value.Value);
        Assert.Equal(101m, book.Ask.Value.Value);
    }

    [Fact]
    public void Book_MidCalculatesCorrectly()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(10m))],
            Asks = [new Level(new Price(102m), new Qty(10m))]
        };

        Assert.Equal(101m, book.Mid.Value.Value);
    }

    [Fact]
    public void Book_SpreadCalculatesCorrectly()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(10m))],
            Asks = [new Level(new Price(101m), new Qty(10m))]
        };

        Assert.Equal(1m, book.Spread.Value.Value);
    }

    [Fact]
    public void Book_BidDepthSumsLevels()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids =
            [
                new Level(new Price(100m), new Qty(10m)),
                new Level(new Price(99m), new Qty(20m)),
                new Level(new Price(98m), new Qty(30m))
            ],
            Asks = []
        };

        Assert.Equal(60m, book.BidDepth().Value);
        Assert.Equal(30m, book.BidDepth(2).Value);
    }

    [Fact]
    public void Book_AskDepthSumsLevels()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [],
            Asks =
            [
                new Level(new Price(101m), new Qty(15m)),
                new Level(new Price(102m), new Qty(25m)),
                new Level(new Price(103m), new Qty(35m))
            ]
        };

        Assert.Equal(75m, book.AskDepth().Value);
        Assert.Equal(40m, book.AskDepth(2).Value);
    }

    [Fact]
    public void Book_ImbalancePositiveWhenMoreBids()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(60m))],
            Asks = [new Level(new Price(101m), new Qty(40m))]
        };

        var imbalance = book.Imbalance();
        Assert.True(imbalance > 0);
        Assert.Equal(0.2m, imbalance); // (60 - 40) / 100 = 0.2
    }

    [Fact]
    public void Book_ImbalanceNegativeWhenMoreAsks()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(30m))],
            Asks = [new Level(new Price(101m), new Qty(70m))]
        };

        var imbalance = book.Imbalance();
        Assert.True(imbalance < 0);
        Assert.Equal(-0.4m, imbalance); // (30 - 70) / 100 = -0.4
    }

    [Fact]
    public void Book_ImbalanceZeroWhenBalanced()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [new Level(new Price(100m), new Qty(50m))],
            Asks = [new Level(new Price(101m), new Qty(50m))]
        };

        Assert.Equal(0m, book.Imbalance());
    }

    [Fact]
    public void Book_VwapToFillBuyCalculatesCorrectly()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [],
            Asks =
            [
                new Level(new Price(101m), new Qty(10m)),
                new Level(new Price(102m), new Qty(10m)),
                new Level(new Price(103m), new Qty(10m))
            ]
        };

        // Buying 25: 10@101 + 10@102 + 5@103 = 1010 + 1020 + 515 = 2545 / 25 = 101.8
        var vwap = book.VwapToFill(Side.Buy, new Qty(25m));
        Assert.NotNull(vwap);
        Assert.Equal(101.8m, vwap.Value.Value);
    }

    [Fact]
    public void Book_VwapToFillSellCalculatesCorrectly()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids =
            [
                new Level(new Price(100m), new Qty(10m)),
                new Level(new Price(99m), new Qty(10m)),
                new Level(new Price(98m), new Qty(10m))
            ],
            Asks = []
        };

        // Selling 15: 10@100 + 5@99 = 1495 / 15 = 99.666...
        var vwap = book.VwapToFill(Side.Sell, new Qty(15m));
        Assert.NotNull(vwap);
        Assert.Equal(99.666666666666666666666666667m, vwap.Value.Value);
    }

    [Fact]
    public void Book_VwapToFillReturnsNullWhenInsufficientLiquidity()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [],
            Asks = [new Level(new Price(101m), new Qty(10m))]
        };

        var vwap = book.VwapToFill(Side.Buy, new Qty(100m));
        Assert.Null(vwap);
    }

    [Fact]
    public void Book_VwapToFillExactLiquidity()
    {
        var book = new Book
        {
            Instrument = BTC,
            Time = Now,
            Bids = [],
            Asks = [new Level(new Price(100m), new Qty(50m))]
        };

        var vwap = book.VwapToFill(Side.Buy, new Qty(50m));
        Assert.NotNull(vwap);
        Assert.Equal(100m, vwap.Value.Value);
    }
}
