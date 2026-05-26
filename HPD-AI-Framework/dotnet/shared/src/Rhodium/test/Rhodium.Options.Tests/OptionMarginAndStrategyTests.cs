using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Options.Tests;

public class OptionMarginAndStrategyTests
{
    [Fact]
    public void DefaultMarginModel_LongInitialMarginIsPremium()
    {
        var contract = Call("AAPL-250-C", 250m);
        var request = new OptionMarginRequest(
            contract,
            new Qty(2m),
            Market(contract, optionMark: 3m, underlyingMark: 255m),
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var initial = DefaultOptionMarginModel.Instance.InitialMargin(request);
        var maintenance = DefaultOptionMarginModel.Instance.MaintenanceMargin(request);

        Assert.Equal(600m, initial.Requirement.Amount);
        Assert.Equal(600m, initial.Premium.Amount);
        Assert.Equal(0m, maintenance.Requirement.Amount);
    }

    [Fact]
    public void DefaultMarginModel_ShortOptionAddsUnderlyingRisk()
    {
        var contract = Call("AAPL-250-C", 250m);
        var request = new OptionMarginRequest(
            contract,
            new Qty(-1m),
            Market(contract, optionMark: 3m, underlyingMark: 255m),
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var margin = DefaultOptionMarginModel.Instance.InitialMargin(request);

        Assert.Equal(300m, margin.Premium.Amount);
        Assert.Equal(-300m, margin.NetPremium.Amount);
        Assert.Equal(5_100m, margin.ShortOptionRisk.Amount);
        Assert.Equal(5_400m, margin.Requirement.Amount);
    }

    [Fact]
    public void DefaultMarginModel_FuturesStyleOptionDoesNotReserveUpfrontPremium()
    {
        var upfront = Call("AAPL-250-C", 250m);
        var contract = upfront with
        {
            Payoff = new PayoffTerms.Option(GetTerms(upfront).With(premiumStyle: OptionPremiumStyle.FuturesStyle))
        };
        var request = new OptionMarginRequest(
            contract,
            new Qty(2m),
            Market(contract, optionMark: 3m, underlyingMark: 255m),
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var margin = DefaultOptionMarginModel.Instance.InitialMargin(request);

        Assert.Equal(0m, margin.Premium.Amount);
        Assert.Equal(0m, margin.Requirement.Amount);
    }

    [Fact]
    public void DefaultStrategyRecognizer_RecognizesCoveredVerticalCallSpread()
    {
        var shortCall = Call("AAPL-250-C", 250m);
        var longCall = Call("AAPL-260-C", 260m);
        var contracts = ContractMap(shortCall, longCall);
        var positions = new[]
        {
            Position(shortCall, -2m),
            Position(longCall, 1m)
        };

        var package = Assert.Single(DefaultOptionStrategyRecognizer.Instance.Recognize(positions, contracts));

        Assert.Equal(OptionStrategyKind.VerticalCallSpread, package.Kind);
        Assert.Equal(2, package.OptionLegs.Count);
        Assert.Equal(new Qty(1m).Value, package.OptionLegs[0].Ratio);
        Assert.Equal(Side.Sell, package.OptionLegs[0].Side);
        Assert.Equal(Side.Buy, package.OptionLegs[1].Side);
    }

    [Fact]
    public void DefaultStrategyMarginModel_MarginsVerticalSpreadByWidthAndUnitOfTrade()
    {
        var shortCall = Call("AAPL-250-C", 250m);
        var longCall = Call("AAPL-260-C", 260m);
        var package = OptionStrategies.VerticalCallSpread(shortCall.Instrument, longCall.Instrument, 2m);
        var context = new OptionMarginContext(
            ContractMap(shortCall, longCall),
            new Dictionary<Instrument, OptionMarketState>
            {
                [shortCall.Instrument] = Market(shortCall, optionMark: 3m, underlyingMark: 255m),
                [longCall.Instrument] = Market(longCall, optionMark: 1m, underlyingMark: 255m)
            },
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var margin = DefaultOptionStrategyMarginModel.Instance.MarginForPackage(package, context);

        Assert.Equal(1_600m, margin.Requirement.Amount);
        Assert.Equal(-400m, margin.NetPremium.Amount);
        Assert.Equal(2_000m, margin.SpreadRisk.Amount);
    }

    [Fact]
    public void DefaultStrategyRecognizer_RecognizesLongStraddle()
    {
        var call = Call("AAPL-250-C", 250m);
        var put = Put("AAPL-250-P", 250m);
        var contracts = ContractMap(call, put);
        var positions = new[]
        {
            Position(call, 2m),
            Position(put, 1m)
        };

        var package = Assert.Single(DefaultOptionStrategyRecognizer.Instance.Recognize(positions, contracts));

        Assert.Equal(OptionStrategyKind.Straddle, package.Kind);
        Assert.All(package.OptionLegs, leg => Assert.Equal(Side.Buy, leg.Side));
        Assert.All(package.OptionLegs, leg => Assert.Equal(1m, leg.Ratio));
    }

    [Fact]
    public void DefaultStrategyMarginModel_MarginsLongStraddleAsCombinedPremium()
    {
        var call = Call("AAPL-250-C", 250m);
        var put = Put("AAPL-250-P", 250m);
        var package = OptionStrategies.LongStraddle(call.Instrument, put.Instrument, 1m);
        var context = new OptionMarginContext(
            ContractMap(call, put),
            new Dictionary<Instrument, OptionMarketState>
            {
                [call.Instrument] = Market(call, optionMark: 3m, underlyingMark: 250m),
                [put.Instrument] = Market(put, optionMark: 4m, underlyingMark: 250m)
            },
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var margin = DefaultOptionStrategyMarginModel.Instance.MarginForPackage(package, context);

        Assert.Equal(700m, margin.Requirement.Amount);
        Assert.Equal(700m, margin.Premium.Amount);
        Assert.Equal(0m, margin.ShortOptionRisk.Amount);
        Assert.Equal(0m, margin.SpreadRisk.Amount);
    }

    [Fact]
    public void DefaultStrategyMarginModel_RejectsMalformedNamedVerticalInsteadOfReturningZero()
    {
        var shortCall = Call("AAPL-260-C", 260m);
        var longCall = Call("AAPL-250-C", 250m);
        var package = OptionStrategies.VerticalCallSpread(shortCall.Instrument, longCall.Instrument, 1m);
        var context = new OptionMarginContext(
            ContractMap(shortCall, longCall),
            new Dictionary<Instrument, OptionMarketState>
            {
                [shortCall.Instrument] = Market(shortCall, optionMark: 1m, underlyingMark: 255m),
                [longCall.Instrument] = Market(longCall, optionMark: 3m, underlyingMark: 255m)
            },
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        var error = Assert.Throws<InvalidOperationException>(
            () => DefaultOptionStrategyMarginModel.Instance.MarginForPackage(package, context));

        Assert.Contains("vertical spread definition", error.Message);
    }

    private static InstrumentContract Call(string symbol, decimal strike)
        => Option(symbol, strike, OptionRight.Call);

    private static InstrumentContract Put(string symbol, decimal strike)
        => Option(symbol, strike, OptionRight.Put);

    private static InstrumentContract Option(string symbol, decimal strike, OptionRight right)
    {
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        return Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(strike, Currency.USD),
            expiry,
            right,
            ExerciseStyle.American);
    }

    private static OptionMarketState Market(
        InstrumentContract contract,
        decimal optionMark,
        decimal underlyingMark) =>
        new(
            contract.Instrument,
            Timestamp: Instant.FromUnixSeconds(1_700_000_000),
            Last: new Price(optionMark, Currency.USD),
            UnderlyingMark: new Price(underlyingMark, Currency.USD));

    private static OptionTerms GetTerms(InstrumentContract contract) =>
        Assert.IsType<PayoffTerms.Option>(contract.Payoff).Terms;

    private static PositionValuationInput Position(InstrumentContract contract, decimal quantity) =>
        new(
            contract.Instrument,
            new Qty(quantity),
            Price.Zero,
            Money.Zero(Currency.USD));

    private static IReadOnlyDictionary<Instrument, InstrumentContract> ContractMap(params InstrumentContract[] contracts) =>
        contracts.ToDictionary(static contract => contract.Instrument);
}
