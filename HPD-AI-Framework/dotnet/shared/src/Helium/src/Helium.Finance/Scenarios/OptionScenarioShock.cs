using Helium.Finance.Options;

namespace Helium.Finance.Scenarios;

public readonly record struct OptionScenarioShock
{
    private readonly double _underlyingRelativeShift;
    private readonly double _underlyingAbsoluteShift;
    private readonly double _volatilityAbsoluteShift;
    private readonly double _riskFreeRateAbsoluteShift;
    private readonly double _dividendYieldAbsoluteShift;
    private readonly double _timeToExpiryAbsoluteShift;

    public OptionScenarioShock(
        double UnderlyingRelativeShift = 0.0,
        double UnderlyingAbsoluteShift = 0.0,
        double VolatilityAbsoluteShift = 0.0,
        double RiskFreeRateAbsoluteShift = 0.0,
        double DividendYieldAbsoluteShift = 0.0,
        double TimeToExpiryAbsoluteShift = 0.0)
    {
        _underlyingRelativeShift = default;
        _underlyingAbsoluteShift = default;
        _volatilityAbsoluteShift = default;
        _riskFreeRateAbsoluteShift = default;
        _dividendYieldAbsoluteShift = default;
        _timeToExpiryAbsoluteShift = default;

        this.UnderlyingRelativeShift = UnderlyingRelativeShift;
        this.UnderlyingAbsoluteShift = UnderlyingAbsoluteShift;
        this.VolatilityAbsoluteShift = VolatilityAbsoluteShift;
        this.RiskFreeRateAbsoluteShift = RiskFreeRateAbsoluteShift;
        this.DividendYieldAbsoluteShift = DividendYieldAbsoluteShift;
        this.TimeToExpiryAbsoluteShift = TimeToExpiryAbsoluteShift;
    }

    public double UnderlyingRelativeShift
    {
        get => _underlyingRelativeShift;
        init
        {
            if (!double.IsFinite(value) || value <= -1.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Underlying relative shift must be finite and greater than -100%.");

            _underlyingRelativeShift = value;
        }
    }

    public double UnderlyingAbsoluteShift
    {
        get => _underlyingAbsoluteShift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Underlying absolute shift must be finite.");

            _underlyingAbsoluteShift = value;
        }
    }

    public double VolatilityAbsoluteShift
    {
        get => _volatilityAbsoluteShift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Volatility shift must be finite.");

            _volatilityAbsoluteShift = value;
        }
    }

    public double RiskFreeRateAbsoluteShift
    {
        get => _riskFreeRateAbsoluteShift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Risk-free rate shift must be finite.");

            _riskFreeRateAbsoluteShift = value;
        }
    }

    public double DividendYieldAbsoluteShift
    {
        get => _dividendYieldAbsoluteShift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Dividend yield shift must be finite.");

            _dividendYieldAbsoluteShift = value;
        }
    }

    public double TimeToExpiryAbsoluteShift
    {
        get => _timeToExpiryAbsoluteShift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Time shift must be finite.");

            _timeToExpiryAbsoluteShift = value;
        }
    }

    public BlackScholesInput Apply(BlackScholesInput input)
    {
        Validate();

        var shockedSpot = ApplyUnderlying(input.Spot);
        var shockedTime = Math.Max(input.TimeToExpiry + TimeToExpiryAbsoluteShift, 0.0);
        var shockedVolatility = Math.Max(input.Volatility + VolatilityAbsoluteShift, 0.0);
        var shockedRiskFreeRate = input.RiskFreeRate + RiskFreeRateAbsoluteShift;
        var shockedDividendYield = input.DividendYield + DividendYieldAbsoluteShift;

        ValidateNonnegative(shockedSpot, nameof(UnderlyingAbsoluteShift), "Shocked spot must be finite and nonnegative.");
        ValidateNonnegative(shockedTime, nameof(TimeToExpiryAbsoluteShift), "Shocked time to expiry must be finite and nonnegative.");
        ValidateNonnegative(shockedVolatility, nameof(VolatilityAbsoluteShift), "Shocked volatility must be finite and nonnegative.");
        ValidateFinite(shockedRiskFreeRate, nameof(RiskFreeRateAbsoluteShift), "Shocked risk-free rate must be finite.");
        ValidateFinite(shockedDividendYield, nameof(DividendYieldAbsoluteShift), "Shocked dividend yield must be finite.");

        return input with
        {
            Spot = shockedSpot,
            TimeToExpiry = shockedTime,
            Volatility = shockedVolatility,
            RiskFreeRate = shockedRiskFreeRate,
            DividendYield = shockedDividendYield
        };
    }

    public Black76Input Apply(Black76Input input)
    {
        Validate();

        var shockedForward = ApplyUnderlying(input.Forward);
        var shockedTime = Math.Max(input.TimeToExpiry + TimeToExpiryAbsoluteShift, 0.0);
        var shockedVolatility = Math.Max(input.Volatility + VolatilityAbsoluteShift, 0.0);
        var shockedDiscount = RiskFreeRateAbsoluteShift == 0.0 || shockedTime == 0.0
            ? input.DiscountFactor
            : input.DiscountFactor * Math.Exp(-RiskFreeRateAbsoluteShift * shockedTime);

        ValidateNonnegative(shockedForward, nameof(UnderlyingAbsoluteShift), "Shocked forward must be finite and nonnegative.");
        ValidateNonnegative(shockedTime, nameof(TimeToExpiryAbsoluteShift), "Shocked time to expiry must be finite and nonnegative.");
        ValidateNonnegative(shockedVolatility, nameof(VolatilityAbsoluteShift), "Shocked volatility must be finite and nonnegative.");
        ValidatePositive(shockedDiscount, nameof(RiskFreeRateAbsoluteShift), "Shocked discount factor must be finite and positive.");

        return input with
        {
            Forward = shockedForward,
            TimeToExpiry = shockedTime,
            Volatility = shockedVolatility,
            DiscountFactor = shockedDiscount
        };
    }

    public BachelierInput Apply(BachelierInput input)
    {
        Validate();

        var shockedForward = ApplyUnderlying(input.Forward);
        var shockedTime = Math.Max(input.TimeToExpiry + TimeToExpiryAbsoluteShift, 0.0);
        var shockedVolatility = Math.Max(input.NormalVolatility + VolatilityAbsoluteShift, 0.0);
        var shockedDiscount = RiskFreeRateAbsoluteShift == 0.0 || shockedTime == 0.0
            ? input.DiscountFactor
            : input.DiscountFactor * Math.Exp(-RiskFreeRateAbsoluteShift * shockedTime);

        ValidateFinite(shockedForward, nameof(UnderlyingAbsoluteShift), "Shocked forward must be finite.");
        ValidateNonnegative(shockedTime, nameof(TimeToExpiryAbsoluteShift), "Shocked time to expiry must be finite and nonnegative.");
        ValidateNonnegative(shockedVolatility, nameof(VolatilityAbsoluteShift), "Shocked normal volatility must be finite and nonnegative.");
        ValidatePositive(shockedDiscount, nameof(RiskFreeRateAbsoluteShift), "Shocked discount factor must be finite and positive.");

        return input with
        {
            Forward = shockedForward,
            TimeToExpiry = shockedTime,
            NormalVolatility = shockedVolatility,
            DiscountFactor = shockedDiscount
        };
    }

    public void Deconstruct(
        out double UnderlyingRelativeShift,
        out double UnderlyingAbsoluteShift,
        out double VolatilityAbsoluteShift,
        out double RiskFreeRateAbsoluteShift,
        out double DividendYieldAbsoluteShift,
        out double TimeToExpiryAbsoluteShift)
    {
        UnderlyingRelativeShift = this.UnderlyingRelativeShift;
        UnderlyingAbsoluteShift = this.UnderlyingAbsoluteShift;
        VolatilityAbsoluteShift = this.VolatilityAbsoluteShift;
        RiskFreeRateAbsoluteShift = this.RiskFreeRateAbsoluteShift;
        DividendYieldAbsoluteShift = this.DividendYieldAbsoluteShift;
        TimeToExpiryAbsoluteShift = this.TimeToExpiryAbsoluteShift;
    }

    private double ApplyUnderlying(double value)
    {
        var multiplier = 1.0 + UnderlyingRelativeShift;
        if (!double.IsFinite(multiplier) || multiplier <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingRelativeShift), "Underlying shift multiplier must be finite and positive.");

        var relative = value * multiplier;
        if (!double.IsFinite(relative))
            throw new ArgumentOutOfRangeException(nameof(UnderlyingRelativeShift), "Relative underlying shock must be finite.");

        var shocked = relative + UnderlyingAbsoluteShift;
        if (!double.IsFinite(shocked))
            throw new ArgumentOutOfRangeException(nameof(UnderlyingAbsoluteShift), "Shocked underlying must be finite.");

        return shocked;
    }

    private void Validate()
    {
        if (!double.IsFinite(UnderlyingRelativeShift) || UnderlyingRelativeShift <= -1.0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingRelativeShift), "Underlying relative shift must be finite and greater than -100%.");

        if (!double.IsFinite(UnderlyingAbsoluteShift))
            throw new ArgumentOutOfRangeException(nameof(UnderlyingAbsoluteShift), "Underlying absolute shift must be finite.");

        if (!double.IsFinite(VolatilityAbsoluteShift))
            throw new ArgumentOutOfRangeException(nameof(VolatilityAbsoluteShift), "Volatility shift must be finite.");

        if (!double.IsFinite(RiskFreeRateAbsoluteShift))
            throw new ArgumentOutOfRangeException(nameof(RiskFreeRateAbsoluteShift), "Risk-free rate shift must be finite.");

        if (!double.IsFinite(DividendYieldAbsoluteShift))
            throw new ArgumentOutOfRangeException(nameof(DividendYieldAbsoluteShift), "Dividend yield shift must be finite.");

        if (!double.IsFinite(TimeToExpiryAbsoluteShift))
            throw new ArgumentOutOfRangeException(nameof(TimeToExpiryAbsoluteShift), "Time shift must be finite.");
    }

    private static void ValidateFinite(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, message);
    }

    private static void ValidateNonnegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);
    }

    private static void ValidatePositive(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);
    }
}
