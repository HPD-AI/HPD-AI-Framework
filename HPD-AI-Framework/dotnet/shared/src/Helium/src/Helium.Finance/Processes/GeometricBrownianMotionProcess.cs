namespace Helium.Finance.Processes;

public readonly record struct GeometricBrownianMotionProcess
{
    private readonly double _initialValue;
    private readonly double _drift;
    private readonly double _volatility;

    public GeometricBrownianMotionProcess(
        double InitialValue,
        double Drift,
        double Volatility)
    {
        _initialValue = default;
        _drift = default;
        _volatility = default;

        this.InitialValue = InitialValue;
        this.Drift = Drift;
        this.Volatility = Volatility;
    }

    public double InitialValue
    {
        get => _initialValue;
        init
        {
            ValidateState(value);
            _initialValue = value;
        }
    }

    public double Drift
    {
        get => _drift;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Drift must be finite.");

            _drift = value;
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

    public double X0()
    {
        Validate();
        return InitialValue;
    }

    public double InstantaneousDrift(double time, double value)
    {
        ValidateTime(time);
        ValidateState(value);
        Validate();

        var drift = Drift * value;
        return EnsureFinite(drift, nameof(value), "Instantaneous drift must be finite.");
    }

    public double InstantaneousDiffusion(double time, double value)
    {
        ValidateTime(time);
        ValidateState(value);
        Validate();

        var diffusion = Volatility * value;
        return EnsureFiniteNonnegative(diffusion, nameof(value), "Instantaneous diffusion must be finite and nonnegative.");
    }

    public double Evolve(double currentValue, double timeStep, double standardNormal)
    {
        Validate();
        ValidateState(currentValue);
        ValidateTime(timeStep);

        if (!double.IsFinite(standardNormal))
            throw new ArgumentOutOfRangeException(nameof(standardNormal), "Shock must be finite.");

        if (timeStep == 0.0)
            return currentValue;

        var variance = Volatility * Volatility;
        var exponent = (Drift - 0.5 * variance) * timeStep + Volatility * Math.Sqrt(timeStep) * standardNormal;
        if (!double.IsFinite(exponent))
            throw new ArgumentOutOfRangeException(nameof(timeStep), "Process evolution exponent must be finite.");

        var evolved = currentValue * Math.Exp(exponent);
        if (!double.IsFinite(evolved))
            throw new ArgumentOutOfRangeException(nameof(currentValue), "Process evolution produced a nonfinite value.");

        return evolved;
    }

    public double ExpectedValue(double time)
    {
        ValidateTime(time);
        Validate();

        var expectedValue = InitialValue * Math.Exp(Drift * time);
        if (!double.IsFinite(expectedValue))
            throw new ArgumentOutOfRangeException(nameof(time), "Process expected value must be finite.");

        return expectedValue;
    }

    public double Variance(double time)
    {
        ValidateTime(time);
        Validate();

        if (time == 0.0)
            return 0.0;

        var volatilitySquared = Volatility * Volatility;
        var variance = InitialValue * InitialValue *
            Math.Exp(2.0 * Drift * time) *
            (Math.Exp(volatilitySquared * time) - 1.0);

        if (!double.IsFinite(variance) || variance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Process variance must be finite and nonnegative.");

        return variance;
    }

    public double StandardDeviation(double time)
    {
        var standardDeviation = Math.Sqrt(Variance(time));
        return EnsureFiniteNonnegative(standardDeviation, nameof(time), "Process standard deviation must be finite and nonnegative.");
    }

    public void Validate()
    {
        ValidateState(InitialValue);

        if (!double.IsFinite(Drift))
            throw new ArgumentOutOfRangeException(nameof(Drift), "Drift must be finite.");

        if (!double.IsFinite(Volatility) || Volatility < 0.0)
            throw new ArgumentOutOfRangeException(nameof(Volatility), "Volatility must be finite and nonnegative.");
    }

    public void Deconstruct(
        out double InitialValue,
        out double Drift,
        out double Volatility)
    {
        InitialValue = this.InitialValue;
        Drift = this.Drift;
        Volatility = this.Volatility;
    }

    private static void ValidateState(double value)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(nameof(value), "Process value must be finite and nonnegative.");
    }

    private static void ValidateTime(double time)
    {
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite and nonnegative.");
    }

    private static double EnsureFinite(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }

    private static double EnsureFiniteNonnegative(double value, string parameterName, string message)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, message);

        return value;
    }
}
