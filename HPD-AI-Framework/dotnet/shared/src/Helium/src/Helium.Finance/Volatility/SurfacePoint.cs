namespace Helium.Finance.Volatility;

public readonly record struct SurfacePoint
{
    public SurfacePoint(double Time, double Strike, double Value)
    {
        if (!double.IsFinite(Time) || Time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(Time), "Surface point time must be finite and nonnegative.");

        if (!double.IsFinite(Strike))
            throw new ArgumentOutOfRangeException(nameof(Strike), "Surface point strike must be finite.");

        if (!double.IsFinite(Value))
            throw new ArgumentOutOfRangeException(nameof(Value), "Surface point value must be finite.");

        this.Time = Time;
        this.Strike = Strike;
        this.Value = Value;
    }

    public double Time { get; }

    public double Strike { get; }

    public double Value { get; }

    public void Deconstruct(out double Time, out double Strike, out double Value)
    {
        Time = this.Time;
        Strike = this.Strike;
        Value = this.Value;
    }
}
