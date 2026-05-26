namespace Helium.Finance.Options;

public readonly record struct BinomialTreeInput
{
    private readonly OptionRight _right;
    private readonly ExerciseStyle _exerciseStyle;
    private readonly double _spot;
    private readonly double _strike;
    private readonly double _timeToExpiry;
    private readonly double _volatility;
    private readonly double _riskFreeRate;
    private readonly double _dividendYield;
    private readonly int _steps;

    public BinomialTreeInput(
        OptionRight Right,
        ExerciseStyle ExerciseStyle,
        double Spot,
        double Strike,
        double TimeToExpiry,
        double Volatility,
        double RiskFreeRate,
        double DividendYield,
        int Steps)
    {
        _right = default;
        _exerciseStyle = default;
        _spot = default;
        _strike = default;
        _timeToExpiry = default;
        _volatility = default;
        _riskFreeRate = default;
        _dividendYield = default;
        _steps = default;

        this.Right = Right;
        this.ExerciseStyle = ExerciseStyle;
        this.Spot = Spot;
        this.Strike = Strike;
        this.TimeToExpiry = TimeToExpiry;
        this.Volatility = Volatility;
        this.RiskFreeRate = RiskFreeRate;
        this.DividendYield = DividendYield;
        this.Steps = Steps;
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

    public ExerciseStyle ExerciseStyle
    {
        get => _exerciseStyle;
        init
        {
            OptionInputValidation.ValidateExerciseStyle(value);
            _exerciseStyle = value;
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

    public int Steps
    {
        get => _steps;
        init
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Steps must be nonnegative.");

            _steps = value;
        }
    }

    public void Deconstruct(
        out OptionRight Right,
        out ExerciseStyle ExerciseStyle,
        out double Spot,
        out double Strike,
        out double TimeToExpiry,
        out double Volatility,
        out double RiskFreeRate,
        out double DividendYield,
        out int Steps)
    {
        Right = this.Right;
        ExerciseStyle = this.ExerciseStyle;
        Spot = this.Spot;
        Strike = this.Strike;
        TimeToExpiry = this.TimeToExpiry;
        Volatility = this.Volatility;
        RiskFreeRate = this.RiskFreeRate;
        DividendYield = this.DividendYield;
        Steps = this.Steps;
    }
}
