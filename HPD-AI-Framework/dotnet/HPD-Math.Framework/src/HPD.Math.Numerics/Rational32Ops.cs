using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Decidable equality and total order for canonical <see cref="Rational32"/> values.
/// </summary>
public readonly struct Rational32OrderOps : ITotalOrderOps<Rational32>
{
    public bool Eq(in Rational32 left, in Rational32 right) => Rational32.Eq(left, right);

    public bool LessEqual(in Rational32 left, in Rational32 right) => Rational32.LessEqual(left, right);

    public Ordering Compare(in Rational32 left, in Rational32 right) => Rational32.Compare(left, right);
}

/// <summary>
/// Status-returning field operations for bounded exact <see cref="Rational32"/> values.
/// </summary>
public readonly struct Rational32StatusFieldOps : IStatusFieldOps<Rational32>, ITotalOrderOps<Rational32>
{
    public Rational32 Zero => Rational32.Zero;

    public Rational32 One => Rational32.One;

    public bool Eq(in Rational32 left, in Rational32 right) => Rational32.Eq(left, right);

    public bool LessEqual(in Rational32 left, in Rational32 right) => Rational32.LessEqual(left, right);

    public Ordering Compare(in Rational32 left, in Rational32 right) => Rational32.Compare(left, right);

    public AlgebraStatus TryAdd(ref Rational32 destination, in Rational32 left, in Rational32 right) =>
        Rational32Kernels.TryAdd(left, right, out destination);

    public AlgebraStatus TrySub(ref Rational32 destination, in Rational32 left, in Rational32 right) =>
        Rational32Kernels.TrySub(left, right, out destination);

    public AlgebraStatus TryMul(ref Rational32 destination, in Rational32 left, in Rational32 right) =>
        Rational32Kernels.TryMul(left, right, out destination);

    public AlgebraStatus TryNeg(ref Rational32 destination, in Rational32 value) =>
        Rational32Kernels.TryNeg(value, out destination);

    public AlgebraStatus TryInvert(ref Rational32 destination, in Rational32 value) =>
        Rational32Kernels.TryInvert(value, out destination);

    public AlgebraStatus TryFromInt(int value, out Rational32 result) =>
        Rational32Kernels.TryCreate(value, 1, out result);
}
