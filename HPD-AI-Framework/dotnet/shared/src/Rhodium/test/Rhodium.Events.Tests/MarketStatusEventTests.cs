using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

public class MarketStatusEventTests
{
    private static readonly Instrument Instrument = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void VenueStatusChanged_ShouldStoreVenueStatusAndReason()
    {
        var evt = new VenueStatusChanged(Venue.NASDAQ, MarketStatus.Halted, "circuit breaker");

        Assert.Equal(Venue.NASDAQ, evt.Venue);
        Assert.Equal(MarketStatus.Halted, evt.Status);
        Assert.Equal("circuit breaker", evt.Reason);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void InstrumentStatusChanged_ShouldBeMarketEvent()
    {
        var evt = new InstrumentStatusChanged(Instrument, MarketStatus.Open, "reopened");

        Assert.Equal(Instrument, evt.Instrument);
        Assert.Equal(MarketStatus.Open, evt.Status);
        Assert.Equal("reopened", evt.Reason);
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }

    [Fact]
    public void InstrumentClosed_ShouldStoreClosePrice()
    {
        var closePrice = new Price(123.45m, Currency.USD);

        var evt = new InstrumentClosed(Instrument, closePrice, "session close");

        Assert.Equal(Instrument, evt.Instrument);
        Assert.Equal(closePrice, evt.ClosePrice);
        Assert.Equal("session close", evt.Reason);
        Assert.IsAssignableFrom<MarketEvent>(evt);
    }
}
