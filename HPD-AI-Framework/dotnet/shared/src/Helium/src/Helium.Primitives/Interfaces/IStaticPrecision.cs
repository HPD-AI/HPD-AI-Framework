namespace Helium.Primitives;

/// <summary>
/// Static witness for a positive finite truncation precision or length.
/// </summary>
public interface IStaticPrecision
{
    static abstract int Value { get; }
}
