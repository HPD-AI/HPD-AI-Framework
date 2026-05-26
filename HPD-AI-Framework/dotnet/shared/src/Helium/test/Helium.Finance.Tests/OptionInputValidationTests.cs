using Helium.Finance.Options;

namespace Helium.Finance.Tests;

public class OptionInputValidationTests
{
    private const OptionRight InvalidRight = (OptionRight)999;
    private const ExerciseStyle InvalidExerciseStyle = (ExerciseStyle)999;

    [Fact]
    public void ClosedFormModelsRejectInvalidOptionRight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Black76.Price(new Black76Input(
            InvalidRight,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            DiscountFactor: 0.95)));

        Assert.Throws<ArgumentOutOfRangeException>(() => Bachelier.Price(new BachelierInput(
            InvalidRight,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            NormalVolatility: 20.0,
            DiscountFactor: 0.95)));

        Assert.Throws<ArgumentOutOfRangeException>(() => BlackScholes.Price(new BlackScholesInput(
            InvalidRight,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05)));
    }

    [Fact]
    public void ClosedFormInputsRejectInvalidConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Black76Input(
            OptionRight.Call,
            Forward: double.NaN,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            DiscountFactor: 0.95));

        Assert.Throws<ArgumentOutOfRangeException>(() => new Black76Input(
            OptionRight.Call,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            DiscountFactor: 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BachelierInput(
            OptionRight.Call,
            Forward: 100.0,
            Strike: double.PositiveInfinity,
            TimeToExpiry: 1.0,
            NormalVolatility: 20.0,
            DiscountFactor: 0.95));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlackScholesInput(
            OptionRight.Call,
            Spot: -1.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05));
    }

    [Fact]
    public void ClosedFormInputsRejectInvalidWithMutation()
    {
        var black76 = new Black76Input(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.95);
        var bachelier = new BachelierInput(OptionRight.Call, 100.0, 100.0, 1.0, 20.0, 0.95);
        var blackScholes = new BlackScholesInput(OptionRight.Call, 100.0, 100.0, 1.0, 0.20, 0.05);

        Assert.Throws<ArgumentOutOfRangeException>(() => black76 with { Right = InvalidRight });
        Assert.Throws<ArgumentOutOfRangeException>(() => black76 with { Volatility = -0.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => black76 with { DiscountFactor = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => bachelier with { NormalVolatility = -0.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => bachelier with { Forward = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => blackScholes with { Spot = -0.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => blackScholes with { DividendYield = double.PositiveInfinity });
    }

    [Fact]
    public void ClosedFormInputsRejectNonfiniteStandardDeviationProjection()
    {
        var black76 = new Black76Input(
            OptionRight.Call,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: double.MaxValue,
            Volatility: double.MaxValue,
            DiscountFactor: 0.95);
        var bachelier = new BachelierInput(
            OptionRight.Call,
            Forward: 100.0,
            Strike: 100.0,
            TimeToExpiry: double.MaxValue,
            NormalVolatility: double.MaxValue,
            DiscountFactor: 0.95);

        Assert.Throws<ArgumentOutOfRangeException>(() => black76.StandardDeviation);
        Assert.Throws<ArgumentOutOfRangeException>(() => bachelier.StandardDeviation);
    }

    [Fact]
    public void TreeModelsRejectInvalidOptionRightAndExerciseStyle()
    {
        var binomial = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);
        var trinomial = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(binomial with { Right = InvalidRight }));
        Assert.Throws<ArgumentOutOfRangeException>(() => BinomialTree.Price(binomial with { ExerciseStyle = InvalidExerciseStyle }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(trinomial with { Right = InvalidRight }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrinomialTree.Price(trinomial with { ExerciseStyle = InvalidExerciseStyle }));
    }

    [Fact]
    public void TreeInputsRejectInvalidConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: double.NaN,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: double.PositiveInfinity,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10));
    }

    [Fact]
    public void TreeInputsRejectInvalidWithMutation()
    {
        var binomial = new BinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);
        var trinomial = new TrinomialTreeInput(
            OptionRight.Call,
            ExerciseStyle.European,
            Spot: 100.0,
            Strike: 100.0,
            TimeToExpiry: 1.0,
            Volatility: 0.20,
            RiskFreeRate: 0.05,
            DividendYield: 0.0,
            Steps: 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => binomial with { Right = InvalidRight });
        Assert.Throws<ArgumentOutOfRangeException>(() => binomial with { ExerciseStyle = InvalidExerciseStyle });
        Assert.Throws<ArgumentOutOfRangeException>(() => binomial with { Steps = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => trinomial with { Strike = -0.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => trinomial with { RiskFreeRate = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => trinomial with { DividendYield = double.PositiveInfinity });
    }

    [Fact]
    public void PriceValidationReturnsDiagnosticForInvalidOptionRight()
    {
        var black = OptionPriceValidation.ValidateBlack76Price(
            new Black76InputWithoutVolatility(InvalidRight, Forward: 100.0, Strike: 100.0, TimeToExpiry: 1.0, DiscountFactor: 0.95),
            marketPrice: 10.0);
        var bachelier = OptionPriceValidation.ValidateBachelierPrice(
            new BachelierInputWithoutVolatility(InvalidRight, Forward: 100.0, Strike: 100.0, TimeToExpiry: 1.0, DiscountFactor: 0.95),
            marketPrice: 10.0);

        Assert.False(black.IsValid);
        Assert.False(bachelier.IsValid);
        Assert.Contains(black.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidInput);
        Assert.Contains(bachelier.Diagnostics, diagnostic => diagnostic.Code == OptionPriceDiagnosticCode.InvalidInput);
    }
}
