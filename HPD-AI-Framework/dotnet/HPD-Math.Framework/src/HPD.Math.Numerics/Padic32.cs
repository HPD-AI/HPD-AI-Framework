using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Fixed-width residue modulo p^N, where p and N are static witnesses.
/// </summary>
public readonly struct Padic32<P, N> : IEquatable<Padic32<P, N>>
    where P : IPrimeModulus
    where N : IStaticPrecision
{
    private readonly int _value;

    public Padic32(int value)
    {
        _value = Padic32Kernels.TryReduce<P, N>(value, out var reduced) == AlgebraStatus.Ok
            ? reduced
            : 0;
    }

    public int Value => _value;

    public static int Prime => P.Value;

    public static int Precision => N.Value;

    public static Padic32<P, N> Zero => new(0, true);

    public static Padic32<P, N> One => new(1, true);

    internal Padic32(int value, bool _)
    {
        _value = value;
    }

    public bool IsZero => _value == 0;

    public bool IsUnit => !IsZero && _value % P.Value != 0;

    public AlgebraStatus TryValuation(out int valuation) =>
        Padic32Kernels.TryValuation<P, N>(_value, out valuation);

    public bool Equals(Padic32<P, N> other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Padic32<P, N> other && Equals(other);

    public override int GetHashCode() => _value;

    public override string ToString() => _value.ToString();

    public static bool operator ==(Padic32<P, N> left, Padic32<P, N> right) => left.Equals(right);

    public static bool operator !=(Padic32<P, N> left, Padic32<P, N> right) => !left.Equals(right);
}
