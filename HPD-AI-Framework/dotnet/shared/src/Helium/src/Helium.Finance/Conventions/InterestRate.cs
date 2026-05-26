namespace Helium.Finance.Conventions;

public readonly record struct InterestRate
{
    private readonly double _rate;
    private readonly CompoundingConvention _compounding;
    private readonly int _frequency;

    public InterestRate(
        double Rate,
        CompoundingConvention Compounding,
        int Frequency = 1)
    {
        _rate = default;
        _compounding = default;
        _frequency = default;

        this.Rate = Rate;
        this.Compounding = Compounding;
        this.Frequency = Frequency;
    }

    public double Rate
    {
        get => _rate;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Rate must be finite.");

            _rate = value;
        }
    }

    public CompoundingConvention Compounding
    {
        get => _compounding;
        init
        {
            ValidateCompounding(value, nameof(value));
            _compounding = value;
        }
    }

    public int Frequency
    {
        get => _frequency;
        init
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Frequency must be positive.");

            _frequency = value;
        }
    }

    public InterestRate EquivalentRate(
        CompoundingConvention targetCompounding,
        double time,
        int targetFrequency = 1)
    {
        var discountFactor = DiscountFactor(time);
        return FromDiscountFactor(discountFactor, time, targetCompounding, targetFrequency);
    }

    public static InterestRate FromDiscountFactor(
        double discountFactor,
        double time,
        CompoundingConvention compounding,
        int frequency = 1)
    {
        ValidateDiscountExtractionInputs(discountFactor, time, compounding, frequency);

        var rate = compounding switch
        {
            CompoundingConvention.Simple => (1.0 / discountFactor - 1.0) / time,
            CompoundingConvention.Continuous => -Math.Log(discountFactor) / time,
            CompoundingConvention.Compounded => frequency * (Math.Pow(discountFactor, -1.0 / (frequency * time)) - 1.0),
            CompoundingConvention.SimpleThenCompounded => time <= 1.0 / frequency
                ? (1.0 / discountFactor - 1.0) / time
                : frequency * (Math.Pow(discountFactor, -1.0 / (frequency * time)) - 1.0),
            CompoundingConvention.CompoundedThenSimple => time > 1.0 / frequency
                ? (1.0 / discountFactor - 1.0) / time
                : frequency * (Math.Pow(discountFactor, -1.0 / (frequency * time)) - 1.0),
            _ => throw new ArgumentOutOfRangeException(nameof(compounding), compounding, "Unsupported compounding convention.")
        };

        return new InterestRate(rate, compounding, frequency);
    }

    public double DiscountFactor(double time)
    {
        return 1.0 / CompoundFactor(time);
    }

    public double CompoundFactor(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");

        if (!double.IsFinite(Rate))
            throw new ArgumentOutOfRangeException(nameof(Rate), "Rate must be finite.");

        var compoundFactor = Compounding switch
        {
            CompoundingConvention.Simple => 1.0 + Rate * time,
            CompoundingConvention.Continuous => Math.Exp(Rate * time),
            CompoundingConvention.Compounded => Math.Pow(1.0 + Rate / Frequency, Frequency * time),
            CompoundingConvention.SimpleThenCompounded => time <= 1.0 / Frequency
                ? 1.0 + Rate * time
                : Math.Pow(1.0 + Rate / Frequency, Frequency * time),
            CompoundingConvention.CompoundedThenSimple => time > 1.0 / Frequency
                ? 1.0 + Rate * time
                : Math.Pow(1.0 + Rate / Frequency, Frequency * time),
            _ => throw new ArgumentOutOfRangeException(nameof(Compounding), Compounding, "Unsupported compounding convention.")
        };

        if (!double.IsFinite(compoundFactor) || compoundFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(Rate), "Rate and time imply a nonpositive or nonfinite compound factor.");

        return compoundFactor;
    }

    public double ZeroRate(double time) =>
        EquivalentRate(CompoundingConvention.Continuous, time).Rate;

    public void Deconstruct(
        out double Rate,
        out CompoundingConvention Compounding,
        out int Frequency)
    {
        Rate = this.Rate;
        Compounding = this.Compounding;
        Frequency = this.Frequency;
    }

    private static void ValidateDiscountExtractionInputs(
        double discountFactor,
        double time,
        CompoundingConvention compounding,
        int frequency)
    {
        if (!double.IsFinite(discountFactor) || discountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(discountFactor), "Discount factor must be finite and positive.");

        if (!double.IsFinite(time) || time <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and positive.");

        ValidateCompounding(compounding, nameof(compounding));

        if (frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency must be positive.");
    }

    private static void ValidateCompounding(CompoundingConvention compounding, string parameterName)
    {
        if (compounding is not (CompoundingConvention.Simple
            or CompoundingConvention.Continuous
            or CompoundingConvention.Compounded
            or CompoundingConvention.SimpleThenCompounded
            or CompoundingConvention.CompoundedThenSimple))
        {
            throw new ArgumentOutOfRangeException(parameterName, compounding, "Unsupported compounding convention.");
        }
    }
}
