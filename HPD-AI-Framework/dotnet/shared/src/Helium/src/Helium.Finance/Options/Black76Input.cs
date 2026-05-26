namespace Helium.Finance.Options;

public readonly record struct Black76Input
{
    private readonly OptionRight _right;
    private readonly double _forward;
    private readonly double _strike;
    private readonly double _timeToExpiry;
    private readonly double _volatility;
    private readonly double _discountFactor;

    public Black76Input(
        OptionRight Right,
        double Forward,
        double Strike,
        double TimeToExpiry,
        double Volatility,
        double DiscountFactor = 1.0)
    {
        _right = default;
        _forward = default;
        _strike = default;
        _timeToExpiry = default;
        _volatility = default;
        _discountFactor = default;

        this.Right = Right;
        this.Forward = Forward;
        this.Strike = Strike;
        this.TimeToExpiry = TimeToExpiry;
        this.Volatility = Volatility;
        this.DiscountFactor = DiscountFactor;
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

    public double Forward
    {
        get => _forward;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Forward must be finite and nonnegative.");

            _forward = value;
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

    public double DiscountFactor
    {
        get => _discountFactor;
        init
        {
            if (!double.IsFinite(value) || value <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Discount factor must be finite and positive.");

            _discountFactor = value;
        }
    }

    public double StandardDeviation
    {
        get
        {
            var standardDeviation = Volatility * Math.Sqrt(TimeToExpiry);
            if (!double.IsFinite(standardDeviation))
                throw new ArgumentOutOfRangeException(nameof(TimeToExpiry), "Standard deviation must be finite.");

            return standardDeviation;
        }
    }

    public void Deconstruct(
        out OptionRight Right,
        out double Forward,
        out double Strike,
        out double TimeToExpiry,
        out double Volatility,
        out double DiscountFactor)
    {
        Right = this.Right;
        Forward = this.Forward;
        Strike = this.Strike;
        TimeToExpiry = this.TimeToExpiry;
        Volatility = this.Volatility;
        DiscountFactor = this.DiscountFactor;
    }
}
