namespace Helium.Finance.Curves;

public readonly record struct CurvePoint
{
    public CurvePoint(double Time, double Value)
    {
        if (!double.IsFinite(Time) || Time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(Time), "Curve point time must be finite and nonnegative.");

        if (!double.IsFinite(Value))
            throw new ArgumentOutOfRangeException(nameof(Value), "Curve point value must be finite.");

        this.Time = Time;
        this.Value = Value;
    }

    public double Time { get; }

    public double Value { get; }

    public void Deconstruct(out double Time, out double Value)
    {
        Time = this.Time;
        Value = this.Value;
    }
}
