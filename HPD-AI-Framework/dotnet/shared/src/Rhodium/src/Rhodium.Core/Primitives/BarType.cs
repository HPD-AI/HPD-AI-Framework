namespace Rhodium.Primitives;

/// <summary>
/// Identifies a specific bar series: instrument + period.
/// Used as key for bar storage and retrieval.
/// </summary>
public readonly record struct BarType(Instrument Instrument, BarPeriod Period)
{
    public override string ToString() => $"{Instrument}:{Period}";

    // Factory methods
    public static BarType Create(Instrument instrument, BarPeriod period) => new(instrument, period);
    public static BarType M1(Instrument instrument) => new(instrument, BarPeriod.M1);
    public static BarType M5(Instrument instrument) => new(instrument, BarPeriod.M5);
    public static BarType H1(Instrument instrument) => new(instrument, BarPeriod.H1);
    public static BarType D1(Instrument instrument) => new(instrument, BarPeriod.D1);
}
