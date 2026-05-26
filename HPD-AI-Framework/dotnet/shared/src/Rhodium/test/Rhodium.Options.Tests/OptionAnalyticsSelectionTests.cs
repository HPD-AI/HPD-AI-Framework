using Rhodium.Options;
using Rhodium.Primitives;

namespace Rhodium.Options.Tests;

public class OptionAnalyticsSelectionTests
{
    [Fact]
    public void DefaultSelector_SelectsExactModelWhenCapabilitiesMatch()
    {
        var contract = Option("AAPL-250-C", ExerciseStyle.European);

        var selection = DefaultOptionAnalyticsModelSelector.Instance.Select(
            contract,
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        Assert.Same(BlackScholesOptionAnalyticsModel.Instance, selection.Model);
        Assert.False(selection.IsApproximation);
    }

    [Fact]
    public void DefaultSelector_SelectsBinomialModelForAmericanOptions()
    {
        var contract = Option("AAPL-250-C", ExerciseStyle.American);

        var selection = DefaultOptionAnalyticsModelSelector.Instance.Select(
            contract,
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        Assert.Same(BinomialOptionAnalyticsModel.Instance, selection.Model);
        Assert.False(selection.IsApproximation);
    }

    [Fact]
    public void DefaultSelector_RejectsBermudanOptionsWithoutAnExplicitModel()
    {
        var now = Instant.Now;
        var contract = Option(
            "AAPL-250-C-BERM",
            ExerciseStyle.Bermudan,
            exerciseDates:
            [
                now + Duration.FromDays(15),
                now + Duration.FromDays(30),
                now + Duration.FromDays(60)
            ]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DefaultOptionAnalyticsModelSelector.Instance.Select(
                contract,
                new OptionPricingScenario(RiskFreeRate: 0.05m)));

        Assert.Contains("No option analytics model supports Bermudan", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultSelector_RejectsDiscreteDividendScenarioWhenNoModelSupportsIt()
    {
        var contract = Option("AAPL-250-C", ExerciseStyle.European);
        var scenario = new OptionPricingScenario(RiskFreeRate: 0.05m)
        {
            DiscreteDividends =
            [
                new DiscreteDividend(Instant.Now + Duration.FromDays(15), Money.USD(0.25m))
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DefaultOptionAnalyticsModelSelector.Instance.Select(contract, scenario));

        Assert.Contains("No option analytics model supports European", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlackScholesModel_RejectsDiscreteDividendScenarioDirectly()
    {
        var contract = Option("AAPL-250-C", ExerciseStyle.European);
        var market = new OptionMarketState(
            contract.Instrument,
            Timestamp: Instant.Now,
            UnderlyingMark: new Price(255m, Currency.USD),
            ObservedImpliedVolatility: 0.20m);
        var scenario = new OptionPricingScenario(RiskFreeRate: 0.05m)
        {
            DiscreteDividends =
            [
                new DiscreteDividend(Instant.Now + Duration.FromDays(15), Money.USD(0.25m))
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BlackScholesOptionAnalyticsModel.Instance.Price(contract, market, scenario));

        Assert.Contains("does not support discrete dividend scenarios", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BinomialModel_PricesAmericanPutWithEarlyExerciseValue()
    {
        var americanPut = Option("AAPL-250-P-AM", ExerciseStyle.American, OptionRight.Put);
        var europeanPut = Option("AAPL-250-P-EU", ExerciseStyle.European, OptionRight.Put);
        var market = new OptionMarketState(
            americanPut.Instrument,
            Timestamp: Instant.Now,
            UnderlyingMark: new Price(225m, Currency.USD),
            ObservedImpliedVolatility: 0.25m);
        var scenario = new OptionPricingScenario(RiskFreeRate: 0.05m);

        var american = BinomialOptionAnalyticsModel.Instance.Price(americanPut, market, scenario);
        var european = BinomialOptionAnalyticsModel.Instance.Price(europeanPut, market with { Instrument = europeanPut.Instrument }, scenario);

        Assert.True(american.TheoreticalValue.Amount >= european.TheoreticalValue.Amount);
        Assert.True(american.TheoreticalValue.Amount >= american.IntrinsicValue.Amount);
        Assert.Equal(0.25m, american.ImpliedVolatility);
    }

    [Fact]
    public void LazyAnalyticsResult_DoesNotEvaluateUntilRequested()
    {
        var model = new CountingOptionAnalyticsModel();
        var contract = Option("AAPL-250-C", ExerciseStyle.European);
        var market = new OptionMarketState(
            contract.Instrument,
            Timestamp: Instant.Now,
            Last: new Price(3m, Currency.USD),
            UnderlyingMark: new Price(255m, Currency.USD),
            ObservedImpliedVolatility: 0.20m);

        var result = LazyOptionAnalyticsResult.Create(
            model,
            contract,
            market,
            new OptionPricingScenario(RiskFreeRate: 0.05m));

        Assert.False(result.HasValuation);
        Assert.False(result.HasGreeks);
        Assert.False(result.HasImpliedVolatility);
        Assert.Equal(0, model.PriceCalls);
        Assert.Equal(0, model.GreeksCalls);
        Assert.Equal(0, model.ImpliedVolatilityCalls);

        _ = result.Valuation;
        _ = result.Valuation;
        _ = result.Greeks;
        _ = result.ImpliedVolatility;

        Assert.True(result.HasValuation);
        Assert.True(result.HasGreeks);
        Assert.True(result.HasImpliedVolatility);
        Assert.Equal(1, model.PriceCalls);
        Assert.Equal(1, model.GreeksCalls);
        Assert.Equal(1, model.ImpliedVolatilityCalls);
    }

    private static InstrumentContract Option(
        string symbol,
        ExerciseStyle exerciseStyle,
        OptionRight right = OptionRight.Call,
        IReadOnlyList<Instant>? exerciseDates = null)
    {
        var underlying = Rhodium.Primitives.Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD).Instrument;
        return Rhodium.Primitives.Contracts.OptionContract(
            symbol,
            "OPRA",
            underlying,
            Currency.USD,
            0.01m,
            1m,
            100m,
            new Price(250m, Currency.USD),
            Instant.Now + Duration.FromDays(60),
            right,
            exerciseStyle,
            exerciseDates: exerciseDates);
    }

    private sealed class CountingOptionAnalyticsModel : IOptionAnalyticsModel
    {
        public int PriceCalls { get; private set; }
        public int GreeksCalls { get; private set; }
        public int ImpliedVolatilityCalls { get; private set; }

        public OptionPricingModelCapabilities Capabilities { get; } = new(
            "counting",
            new HashSet<ExerciseStyle> { ExerciseStyle.European, ExerciseStyle.American },
            new HashSet<OptionSettlementStyle> { OptionSettlementStyle.Cash, OptionSettlementStyle.Physical },
            SupportsGreeks: true,
            SupportsImpliedVolatility: true,
            SupportsDiscreteDividends: false,
            SupportsEarlyExercise: true);

        public bool Supports(InstrumentContract contract) =>
            contract.Payoff is PayoffTerms.Option option &&
            Capabilities.SupportedExerciseStyles.Contains(option.Terms.ExerciseStyle) &&
            Capabilities.SupportedSettlementStyles.Contains(option.Terms.SettlementStyle);

        public OptionValuation Price(
            InstrumentContract contract,
            OptionMarketState market,
            OptionPricingScenario scenario)
        {
            PriceCalls++;
            return new OptionValuation(
                new Money(300m, Currency.USD),
                new Money(100m, Currency.USD),
                new Money(200m, Currency.USD),
                0.20m);
        }

        public OptionGreeks Greeks(
            InstrumentContract contract,
            OptionMarketState market,
            OptionPricingScenario scenario)
        {
            GreeksCalls++;
            return new OptionGreeks(0.5m, 0.01m, -0.02m, 0.10m, 0.03m);
        }

        public decimal ImpliedVolatility(
            InstrumentContract contract,
            OptionMarketState market,
            Money marketPremium,
            OptionPricingScenario scenario)
        {
            ImpliedVolatilityCalls++;
            return 0.20m;
        }
    }
}
