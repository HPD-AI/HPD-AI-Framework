using Rhodium.Analytics;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics.Tests;

/// <summary>
/// Tests for RoundTripBuilder FIFO matching.
/// </summary>
public class RoundTripBuilderTests
{
    [Fact]
    public void FromFills_CreatesRoundTrip_ForSimpleLongTrade()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Buy 10 @ 50000
            new OrderFilled(
                new OrderId(1),
                inst,
                0,
                new StrategyId(1),
                Side.Buy,
                new Qty(10m),
                new Price(50000m, Currency.USD),
                new Money(50m, Currency.USD)
            )
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000)
            },
            // Sell 10 @ 55000
            new OrderFilled(
                new OrderId(2),
                inst,
                0,
                new StrategyId(1),
                Side.Sell,
                new Qty(10m),
                new Price(55000m, Currency.USD),
                new Money(50m, Currency.USD)
            )
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000)
            }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Single(roundTrips);
        var rt = roundTrips[0];
        Assert.Equal(Side.Buy, rt.Side);
        Assert.Equal(10m, rt.Quantity.Value);
        Assert.Equal(50000m, rt.EntryPrice.Value);
        Assert.Equal(55000m, rt.ExitPrice.Value);
        Assert.Equal(100m, rt.Commission.Amount); // 50 + 50
    }

    [Fact]
    public void FromFills_CreatesRoundTrip_ForSimpleShortTrade()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Sell 10 @ 55000
            new OrderFilled(
                new OrderId(1),
                inst,
                0,
                new StrategyId(1),
                Side.Sell,
                new Qty(10m),
                new Price(55000m, Currency.USD),
                new Money(50m, Currency.USD)
            )
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000)
            },
            // Buy 10 @ 50000
            new OrderFilled(
                new OrderId(2),
                inst,
                0,
                new StrategyId(1),
                Side.Buy,
                new Qty(10m),
                new Price(50000m, Currency.USD),
                new Money(50m, Currency.USD)
            )
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000)
            }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Single(roundTrips);
        var rt = roundTrips[0];
        Assert.Equal(Side.Sell, rt.Side);
        Assert.Equal(10m, rt.Quantity.Value);
        Assert.Equal(55000m, rt.EntryPrice.Value);
        Assert.Equal(50000m, rt.ExitPrice.Value);
    }

    [Fact]
    public void FromFills_CreatesMultipleRoundTrips_ForMultipleTrades()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Trade 1: Buy 10 @ 50000
            new OrderFilled(new OrderId(1), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) },
            // Trade 1 exit: Sell 10 @ 55000
            new OrderFilled(new OrderId(2), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(10m), new Price(55000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000) },
            // Trade 2: Buy 5 @ 51000
            new OrderFilled(new OrderId(3), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(5m), new Price(51000m, Currency.USD), new Money(25m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(3000) },
            // Trade 2 exit: Sell 5 @ 56000
            new OrderFilled(new OrderId(4), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(5m), new Price(56000m, Currency.USD), new Money(25m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(4000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Equal(2, roundTrips.Count);

        // First round trip
        Assert.Equal(10m, roundTrips[0].Quantity.Value);
        Assert.Equal(50000m, roundTrips[0].EntryPrice.Value);
        Assert.Equal(55000m, roundTrips[0].ExitPrice.Value);

        // Second round trip
        Assert.Equal(5m, roundTrips[1].Quantity.Value);
        Assert.Equal(51000m, roundTrips[1].EntryPrice.Value);
        Assert.Equal(56000m, roundTrips[1].ExitPrice.Value);
    }

    [Fact]
    public void FromFills_HandlesPartialFills_WithFIFO()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Buy 10 @ 50000
            new OrderFilled(new OrderId(1), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) },
            // Sell 5 @ 55000 (partial exit)
            new OrderFilled(new OrderId(2), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(5m), new Price(55000m, Currency.USD), new Money(25m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000) },
            // Sell 5 @ 56000 (remaining exit)
            new OrderFilled(new OrderId(3), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(5m), new Price(56000m, Currency.USD), new Money(25m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(3000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Equal(2, roundTrips.Count);

        // First partial exit at 55000
        Assert.Equal(5m, roundTrips[0].Quantity.Value);
        Assert.Equal(50000m, roundTrips[0].EntryPrice.Value);
        Assert.Equal(55000m, roundTrips[0].ExitPrice.Value);

        // Second partial exit at 56000
        Assert.Equal(5m, roundTrips[1].Quantity.Value);
        Assert.Equal(50000m, roundTrips[1].EntryPrice.Value);
        Assert.Equal(56000m, roundTrips[1].ExitPrice.Value);
    }

    [Fact]
    public void FromFills_DoesNotCreateRoundTrip_ForOpenPosition()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Buy 10 @ 50000 (no exit)
            new OrderFilled(new OrderId(1), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Empty(roundTrips);
    }

    [Fact]
    public void FromFills_HandlesMultipleInstruments()
    {
        var btc = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var eth = new Instrument(new Asset("ETH", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // BTC trade
            new OrderFilled(new OrderId(1), btc, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) },
            // ETH trade
            new OrderFilled(new OrderId(2), eth, 0, new StrategyId(1),
                Side.Buy, new Qty(100m), new Price(3000m, Currency.USD), new Money(30m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000) },
            // BTC exit
            new OrderFilled(new OrderId(3), btc, 0, new StrategyId(1),
                Side.Sell, new Qty(10m), new Price(55000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(3000) },
            // ETH exit
            new OrderFilled(new OrderId(4), eth, 0, new StrategyId(1),
                Side.Sell, new Qty(100m), new Price(3500m, Currency.USD), new Money(30m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(4000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Equal(2, roundTrips.Count);

        var btcTrade = roundTrips.FirstOrDefault(rt => rt.Instrument.Asset.Symbol == "BTC");
        var ethTrade = roundTrips.FirstOrDefault(rt => rt.Instrument.Asset.Symbol == "ETH");

        Assert.NotNull(btcTrade);
        Assert.NotNull(ethTrade);
        Assert.Equal(10m, btcTrade.Quantity.Value);
        Assert.Equal(100m, ethTrade.Quantity.Value);
    }

    [Fact]
    public void FromFills_AllocatesCommissionProportionally()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Buy 10 @ 50000 (commission 100)
            new OrderFilled(new OrderId(1), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(100m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) },
            // Sell 5 @ 55000 (commission 50) - should get 50% of entry commission + all exit commission
            new OrderFilled(new OrderId(2), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(5m), new Price(55000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        Assert.Single(roundTrips);
        // Commission = 50% of 100 (entry) + 100% of 50 (exit) = 50 + 50 = 100
        Assert.Equal(100m, roundTrips[0].Commission.Amount);
    }

    [Fact]
    public void FromFills_HandlesSameSideAddsToPosition()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);
        var fills = new List<OrderFilled>
        {
            // Buy 10 @ 50000
            new OrderFilled(new OrderId(1), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1000) },
            // Buy 5 @ 51000 (add to position)
            new OrderFilled(new OrderId(2), inst, 0, new StrategyId(1),
                Side.Buy, new Qty(5m), new Price(51000m, Currency.USD), new Money(25m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(2000) },
            // Sell 15 @ 56000 (exit entire position)
            new OrderFilled(new OrderId(3), inst, 0, new StrategyId(1),
                Side.Sell, new Qty(15m), new Price(56000m, Currency.USD), new Money(75m, Currency.USD))
            { Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(3000) }
        };

        var roundTrips = RoundTripBuilder.FromFills(fills).ToList();

        // Should create 2 round trips (FIFO):
        // 1. Buy 10 @ 50000, Sell 10 @ 56000
        // 2. Buy 5 @ 51000, Sell 5 @ 56000
        Assert.Equal(2, roundTrips.Count);

        Assert.Equal(10m, roundTrips[0].Quantity.Value);
        Assert.Equal(50000m, roundTrips[0].EntryPrice.Value);
        Assert.Equal(56000m, roundTrips[0].ExitPrice.Value);

        Assert.Equal(5m, roundTrips[1].Quantity.Value);
        Assert.Equal(51000m, roundTrips[1].EntryPrice.Value);
        Assert.Equal(56000m, roundTrips[1].ExitPrice.Value);
    }

    [Fact]
    public void FromOrders_CreatesRoundTripsFromOrderHistory()
    {
        var inst = new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance);

        // Create first order and fill it
        var order1 = new Order
        {
            Id = new OrderId(1),
            Instrument = inst,
            Side = Side.Buy,
            Quantity = new Qty(10m),
            Type = OrderType.Limit,
            VariantId = 0,
            ResponseTimestamp = Instant.FromUnixMillis(1000)
        };
        order1.Fill(new Qty(10m), new Price(50000m, Currency.USD), new Money(50m, Currency.USD), Instant.FromUnixMillis(1000));

        // Create second order and fill it
        var order2 = new Order
        {
            Id = new OrderId(2),
            Instrument = inst,
            Side = Side.Sell,
            Quantity = new Qty(10m),
            Type = OrderType.Limit,
            VariantId = 0,
            ResponseTimestamp = Instant.FromUnixMillis(2000)
        };
        order2.Fill(new Qty(10m), new Price(55000m, Currency.USD), new Money(50m, Currency.USD), Instant.FromUnixMillis(2000));

        var orders = new List<Order> { order1, order2 };

        var roundTrips = RoundTripBuilder.FromOrders(orders).ToList();

        Assert.Single(roundTrips);
        Assert.Equal(10m, roundTrips[0].Quantity.Value);
        Assert.Equal(50000m, roundTrips[0].EntryPrice.Value);
        Assert.Equal(55000m, roundTrips[0].ExitPrice.Value);
    }
}
