namespace HPD.Math.Core;

/// <summary>
/// Static finite dimension witness.
/// </summary>
public interface IStaticDimension
{
    static abstract int Value { get; }
}

/// <summary>
/// Static finite truncation precision witness.
/// </summary>
public interface IStaticPrecision
{
    static abstract int Value { get; }
}

/// <summary>
/// Static exact prime modulus witness.
/// </summary>
public interface IPrimeModulus
{
    static abstract int Value { get; }
}
