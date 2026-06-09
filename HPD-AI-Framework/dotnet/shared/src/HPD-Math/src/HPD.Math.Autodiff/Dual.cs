using HPD.Math.Core;

namespace HPD.Math.Autodiff;

/// <summary>
/// Forward-mode dual value a + bε with ε² = 0.
/// </summary>
public readonly struct Dual<T>
{
    public Dual(T primal, T tangent)
    {
        Primal = primal;
        Tangent = tangent;
    }

    public T Primal { get; }

    public T Tangent { get; }
}

/// <summary>
/// Status-returning forward-mode field operations for bounded coefficient types.
/// </summary>
public readonly struct DualStatusFieldOps<T, TOps> : IStatusFieldOps<Dual<T>>
    where TOps : struct, IStatusFieldOps<T>
{
    public Dual<T> Zero
    {
        get
        {
            var ops = new TOps();
            return new Dual<T>(ops.Zero, ops.Zero);
        }
    }

    public Dual<T> One
    {
        get
        {
            var ops = new TOps();
            return new Dual<T>(ops.One, ops.Zero);
        }
    }

    public bool Eq(in Dual<T> left, in Dual<T> right)
    {
        var ops = new TOps();
        return ops.Eq(left.Primal, right.Primal) && ops.Eq(left.Tangent, right.Tangent);
    }

    public AlgebraStatus TryAdd(ref Dual<T> destination, in Dual<T> left, in Dual<T> right)
    {
        var ops = new TOps();
        var primal = ops.Zero;
        var tangent = ops.Zero;

        var status = ops.TryAdd(ref primal, left.Primal, right.Primal);
        if (status != AlgebraStatus.Ok)
            return status;

        status = ops.TryAdd(ref tangent, left.Tangent, right.Tangent);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = new Dual<T>(primal, tangent);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TrySub(ref Dual<T> destination, in Dual<T> left, in Dual<T> right)
    {
        var ops = new TOps();
        var primal = ops.Zero;
        var tangent = ops.Zero;

        var status = ops.TrySub(ref primal, left.Primal, right.Primal);
        if (status != AlgebraStatus.Ok)
            return status;

        status = ops.TrySub(ref tangent, left.Tangent, right.Tangent);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = new Dual<T>(primal, tangent);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryMul(ref Dual<T> destination, in Dual<T> left, in Dual<T> right)
    {
        var ops = new TOps();
        var primal = ops.Zero;
        var leftTerm = ops.Zero;
        var rightTerm = ops.Zero;
        var tangent = ops.Zero;

        var status = ops.TryMul(ref primal, left.Primal, right.Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref leftTerm, left.Primal, right.Tangent);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref rightTerm, left.Tangent, right.Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryAdd(ref tangent, leftTerm, rightTerm);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = new Dual<T>(primal, tangent);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryNeg(ref Dual<T> destination, in Dual<T> value)
    {
        var ops = new TOps();
        var primal = ops.Zero;
        var tangent = ops.Zero;
        var status = ops.TryNeg(ref primal, value.Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryNeg(ref tangent, value.Tangent);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = new Dual<T>(primal, tangent);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryInvert(ref Dual<T> destination, in Dual<T> value)
    {
        var ops = new TOps();
        var inv = ops.Zero;
        var invSquared = ops.Zero;
        var tangent = ops.Zero;

        var status = ops.TryInvert(ref inv, value.Primal);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref invSquared, inv, inv);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryMul(ref tangent, value.Tangent, invSquared);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ops.TryNeg(ref tangent, tangent);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = new Dual<T>(inv, tangent);
        return AlgebraStatus.Ok;
    }
}
