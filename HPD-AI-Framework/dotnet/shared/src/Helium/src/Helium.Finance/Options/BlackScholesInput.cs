namespace Helium.Finance.Options;

public readonly record struct BlackScholesInput
{
    private readonly OptionRight _right;
    private readonly double _spot;
    private readonly double _strike;
    private readonly double _timeToExpiry;
    private readonly double _volatility;
    private readonly double _riskFreeRate;
    private readonly double _dividendYield;

    public BlackScholesInput(
        OptionRight Right,
        double Spot,
        double Strike,
        double TimeToExpiry,
        double Volatility,
        double RiskFreeRate,
        double DividendYield = 0.0)
    {
        _right = default;
        _spot = default;
        _strike = default;
        _timeToExpiry = default;
        _volatility = default;
        _riskFreeRate = default;
        _dividendYield = default;

        this.Right = Right;
        this.Spot = Spot;
        this.Strike = Strike;
        this.TimeToExpiry = TimeToExpiry;
        this.Volatility = Volatility;
        this.RiskFreeRate = RiskFreeRate;
        this.DividendYield = DividendYield;
    }

    public OptionRight Right
    {
        get => _right;
        init
        {
            OptionInputValidation.ValidateRight(value);
            _right = value;
        }
    }

    public double Spot
    {
        get => _spot;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Spot must be finite and nonnegative.");

            _spot = value;
        }
    }

    public double Strike
    {
        get => _strike;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Strike must be finite and nonnegative.");

            _strike = value;
        }
    }

    public double TimeToExpiry
    {
        get => _timeToExpiry;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Time to expiry must be finite and nonnegative.");

            _timeToExpiry = value;
        }
    }

    public double Volatility
    {
        get => _volatility;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Volatility must be finite and nonnegative.");

            _volatility = value;
        }
    }

    public double RiskFreeRate
    {
        get => _riskFreeRate;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Risk-free rate must be finite.");

            _riskFreeRate = value;
        }
    }

    public double DividendYield
    {
        get => _dividendYield;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Dividend yield must be finite.");

            _dividendYield = value;
        }
    }

    public void Deconstruct(
        out OptionRight Right,
        out double Spot,
        out double Strike,
        out double TimeToExpiry,
        out double Volatility,
        out double RiskFreeRate,
        out double DividendYield)
    {
        Right = this.Right;
        Spot = this.Spot;
        Strike = this.Strike;
        TimeToExpiry = this.TimeToExpiry;
        Volatility = this.Volatility;
        RiskFreeRate = this.RiskFreeRate;
        DividendYield = this.DividendYield;
    }
}
