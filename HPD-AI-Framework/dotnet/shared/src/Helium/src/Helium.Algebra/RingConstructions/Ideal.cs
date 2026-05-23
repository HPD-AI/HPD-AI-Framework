using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Ideal of a commutative ring. Currently supports principal ideals (single generator)
/// and finitely generated ideals.
/// </summary>
public class Ideal<R>
    where R : ICommRing<R>
{
    private readonly R[] _generators;

    private Ideal(R[] generators) => _generators = generators;

    public static Ideal<R> Principal(R generator) => new([generator]);

    public static Ideal<R> Generated(params R[] generators) => new((R[])generators.Clone());

    public IReadOnlyList<R> Generators => _generators;

    public bool IsPrincipal => _generators.Length == 1;

    public R Generator => _generators[0];

    /// <summary>
    /// The zero ideal (0): contains only zero.
    /// </summary>
    public static Ideal<R> ZeroIdeal => Principal(R.AdditiveIdentity);

    /// <summary>
    /// The unit ideal (1): contains everything.
    /// </summary>
    public static Ideal<R> UnitIdeal => Principal(R.MultiplicativeIdentity);
}

public readonly struct ZModElement :
    ICommRing<ZModElement>,
    IEquatable<ZModElement>
{
    public Integer Value { get; }

    public Integer Modulus { get; }

    private ZModElement(Integer value, Integer modulus)
    {
        Modulus = modulus.IsZero ? Integer.Zero : modulus.Abs();
        Value = Modulus.IsZero ? value : Reduce(value, Modulus);
    }

    public static ZModElement Create(Integer value, Integer modulus) => new(value, modulus);

    public static ZModElement AdditiveIdentity => new(Integer.Zero, Integer.Zero);

    public static ZModElement MultiplicativeIdentity => new(Integer.One, Integer.Zero);

    static ZModElement IAdditiveIdentity<ZModElement, ZModElement>.AdditiveIdentity => AdditiveIdentity;

    static ZModElement IMultiplicativeIdentity<ZModElement, ZModElement>.MultiplicativeIdentity => MultiplicativeIdentity;

    public static ZModElement operator +(ZModElement left, ZModElement right)
    {
        var modulus = ResolveModulus(left, right);
        return new(left.Value + right.Value, modulus);
    }

    public static ZModElement operator -(ZModElement left, ZModElement right)
    {
        var modulus = ResolveModulus(left, right);
        return new(left.Value - right.Value, modulus);
    }

    public static ZModElement operator *(ZModElement left, ZModElement right)
    {
        var modulus = ResolveModulus(left, right);
        return new(left.Value * right.Value, modulus);
    }

    public static ZModElement operator -(ZModElement value) =>
        new(-value.Value, value.Modulus);

    public static bool operator ==(ZModElement left, ZModElement right) => left.Equals(right);

    public static bool operator !=(ZModElement left, ZModElement right) => !left.Equals(right);

    public bool Equals(ZModElement other)
    {
        if (Modulus.IsZero || other.Modulus.IsZero)
            return Value.Equals(other.Value);
        return Modulus.Equals(other.Modulus) && Value.Equals(other.Value);
    }

    public override bool Equals(object? obj) => obj is ZModElement other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, Modulus);

    public override string ToString() =>
        Modulus.IsZero ? Value.ToString() : $"{Value} (mod {Modulus})";

    private static Integer ResolveModulus(ZModElement left, ZModElement right)
    {
        if (left.Modulus.IsZero) return right.Modulus;
        if (right.Modulus.IsZero) return left.Modulus;
        if (left.Modulus.Equals(right.Modulus)) return left.Modulus;
        throw new InvalidOperationException(
            $"Cannot mix ZMod elements with moduli {left.Modulus} and {right.Modulus}.");
    }

    private static Integer Reduce(Integer value, Integer modulus)
    {
        var (_, r) = Integer.DivMod(value, modulus);
        if (r < Integer.Zero)
            r += modulus;
        return r;
    }
}

/// <summary>
/// Factory for integer residue classes Z/nZ.
/// </summary>
public static class ZMod
{
    public static ZModElement Create(Integer value, Integer modulus) =>
        ZModElement.Create(value, modulus);

    public static Func<Integer, Integer> Reducer(Integer modulus)
    {
        if (modulus.IsZero)
            return x => x;

        var abs = modulus.Abs();
        return x =>
        {
            var (_, r) = Integer.DivMod(x, abs);
            if (r < Integer.Zero)
                r = r + abs;
            return r;
        };
    }
}
