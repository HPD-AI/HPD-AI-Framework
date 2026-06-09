using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Integer residue modulo the static prime witness <typeparamref name="P"/>.
/// </summary>
public readonly struct ModInt<P> : IEquatable<ModInt<P>>
    where P : IPrimeModulus
{
    private readonly int _value;

    public ModInt(int value)
    {
        _value = Mod(value, P.Value);
    }

    public int Value => _value;

    public static int Modulus => P.Value;

    public static ModInt<P> Zero => new(0);

    public static ModInt<P> One => new(1);

    public bool Equals(ModInt<P> other) => _value == other._value;

    public override bool Equals(object? obj) => obj is ModInt<P> other && Equals(other);

    public override int GetHashCode() => _value;

    public override string ToString() => _value.ToString();

    public static bool operator ==(ModInt<P> left, ModInt<P> right) => left.Equals(right);

    public static bool operator !=(ModInt<P> left, ModInt<P> right) => !left.Equals(right);

    internal static int Mod(long value, int modulus)
    {
        var result = (int)(value % modulus);
        return result < 0 ? result + modulus : result;
    }

    internal static int Mod(int value, int modulus) => Mod((long)value, modulus);
}
