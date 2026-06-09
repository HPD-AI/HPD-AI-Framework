using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Allocation-free kernels for p-typical Witt vectors. Add/sub/mul currently support
/// truncation length 1 or 2.
/// </summary>
public static class WittVectorKernels
{
    public static AlgebraStatus ValidateLength<P, N, T>(WittVectorView<T> value)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        var status = ValidateContext<P, N>();
        if (status != AlgebraStatus.Ok)
            return status;

        return value.Length == N.Value ? AlgebraStatus.Ok : AlgebraStatus.DimensionMismatch;
    }

    public static AlgebraStatus TryZero<P, N, T, TOps>(ref WittVectorBuilder<T> destination, TOps ops)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
    {
        var status = ValidateDestination<P, N, T>(destination);
        if (status != AlgebraStatus.Ok)
            return status;

        for (var i = 0; i < N.Value; i++)
            destination.ComponentAt(i) = ops.Zero;
        destination.Commit(N.Value);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryOne<P, N, T, TOps>(ref WittVectorBuilder<T> destination, TOps ops)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
    {
        var status = TryZero<P, N, T, TOps>(ref destination, ops);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.ComponentAt(0) = ops.One;
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryGhostComponent<P, N, T, TOps, TIntegerOps>(
        WittVectorView<T> value,
        int index,
        ref T destination,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        var status = ValidateLength<P, N, T>(value);
        if (status != AlgebraStatus.Ok)
            return status;
        if (index < 0 || index >= N.Value)
            return AlgebraStatus.InvalidInput;

        if (index == 0)
        {
            destination = value[0];
            return AlgebraStatus.Ok;
        }

        if (!HasSupportedGhostComponent(index))
            return AlgebraStatus.InvalidInput;

        status = Pow(value[0], P.Value, ref destination, ops);
        if (status != AlgebraStatus.Ok)
            return status;

        status = integerOps.TryFromInt(P.Value, out var scaledSecond);
        if (status != AlgebraStatus.Ok)
            return status;

        ops.Mul(ref scaledSecond, scaledSecond, value[1]);
        ops.Add(ref destination, destination, scaledSecond);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryAdd<P, N, T, TOps, TIntegerOps>(
        WittVectorView<T> left,
        WittVectorView<T> right,
        ref WittVectorBuilder<T> destination,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        var status = ValidateSame<P, N, T>(left, right, destination);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateArithmetic<N>();
        if (status != AlgebraStatus.Ok)
            return status;

        ops.Add(ref destination.ComponentAt(0), left[0], right[0]);
        if (N.Value == 2)
        {
            ops.Add(ref destination.ComponentAt(1), left[1], right[1]);
            status = WittAdditionCorrection<P, T, TOps, TIntegerOps>(left[0], right[0], out var correction, ops, integerOps);
            if (status != AlgebraStatus.Ok)
                return status;
            ops.Sub(ref destination.ComponentAt(1), destination.ComponentAt(1), correction);
        }

        destination.Commit(N.Value);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryNeg<P, N, T, TOps, TIntegerOps>(
        WittVectorView<T> value,
        ref WittVectorBuilder<T> destination,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        var status = ValidateLength<P, N, T>(value);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateDestination<P, N, T>(destination);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateArithmetic<N>();
        if (status != AlgebraStatus.Ok)
            return status;

        ops.Neg(ref destination.ComponentAt(0), value[0]);
        if (N.Value == 2)
        {
            ops.Neg(ref destination.ComponentAt(1), value[1]);
            status = WittAdditionCorrection<P, T, TOps, TIntegerOps>(
                value[0],
                destination.ComponentAt(0),
                out var correction,
                ops,
                integerOps);
            if (status != AlgebraStatus.Ok)
                return status;
            ops.Add(ref destination.ComponentAt(1), destination.ComponentAt(1), correction);
        }

        destination.Commit(N.Value);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TrySub<P, N, T, TOps, TIntegerOps>(
        WittVectorView<T> left,
        WittVectorView<T> right,
        ref WittVectorBuilder<T> destination,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        var status = ValidateSame<P, N, T>(left, right, destination);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateArithmetic<N>();
        if (status != AlgebraStatus.Ok)
            return status;

        ops.Neg(ref destination.ComponentAt(0), right[0]);
        var negRight0 = destination.ComponentAt(0);

        if (N.Value == 2)
        {
            ops.Neg(ref destination.ComponentAt(1), right[1]);
            status = WittAdditionCorrection<P, T, TOps, TIntegerOps>(
                right[0],
                negRight0,
                out var negCorrection,
                ops,
                integerOps);
            if (status != AlgebraStatus.Ok)
                return status;
            ops.Add(ref destination.ComponentAt(1), destination.ComponentAt(1), negCorrection);
            var negRight1 = destination.ComponentAt(1);

            ops.Sub(ref destination.ComponentAt(0), left[0], right[0]);
            ops.Add(ref destination.ComponentAt(1), left[1], negRight1);
            status = WittAdditionCorrection<P, T, TOps, TIntegerOps>(
                left[0],
                negRight0,
                out var addCorrection,
                ops,
                integerOps);
            if (status != AlgebraStatus.Ok)
                return status;
            ops.Sub(ref destination.ComponentAt(1), destination.ComponentAt(1), addCorrection);
        }
        else
        {
            ops.Sub(ref destination.ComponentAt(0), left[0], right[0]);
        }

        destination.Commit(N.Value);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul<P, N, T, TOps, TIntegerOps>(
        WittVectorView<T> left,
        WittVectorView<T> right,
        ref WittVectorBuilder<T> destination,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where N : IStaticPrecision
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        var status = ValidateSame<P, N, T>(left, right, destination);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateArithmetic<N>();
        if (status != AlgebraStatus.Ok)
            return status;

        ops.Mul(ref destination.ComponentAt(0), left[0], right[0]);
        if (N.Value == 2)
        {
            status = Pow(left[0], P.Value, ref destination.ComponentAt(1), ops);
            if (status != AlgebraStatus.Ok)
                return status;

            ops.Mul(ref destination.ComponentAt(1), destination.ComponentAt(1), right[1]);

            var rightPow = ops.One;
            status = Pow(right[0], P.Value, ref rightPow, ops);
            if (status != AlgebraStatus.Ok)
                return status;

            ops.Mul(ref rightPow, rightPow, left[1]);
            ops.Add(ref destination.ComponentAt(1), destination.ComponentAt(1), rightPow);

            status = integerOps.TryFromInt(P.Value, out var cross);
            if (status != AlgebraStatus.Ok)
                return status;

            ops.Mul(ref cross, cross, left[1]);
            ops.Mul(ref cross, cross, right[1]);
            ops.Add(ref destination.ComponentAt(1), destination.ComponentAt(1), cross);
        }

        destination.Commit(N.Value);
        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus WittAdditionCorrection<P, T, TOps, TIntegerOps>(
        in T x,
        in T y,
        out T correction,
        TOps ops,
        TIntegerOps integerOps)
        where P : IPrimeModulus
        where TOps : struct, ICommutativeRingOps<T>
        where TIntegerOps : struct, IIntegerEmbeddingOps<T>
    {
        correction = ops.Zero;
        for (var i = 1; i < P.Value; i++)
        {
            var status = integerOps.TryFromInt(Binomial(P.Value, i) / P.Value, out var term);
            if (status != AlgebraStatus.Ok)
                return status;

            var xPower = ops.One;
            status = Pow(x, i, ref xPower, ops);
            if (status != AlgebraStatus.Ok)
                return status;
            var yPower = ops.One;
            status = Pow(y, P.Value - i, ref yPower, ops);
            if (status != AlgebraStatus.Ok)
                return status;

            ops.Mul(ref term, term, xPower);
            ops.Mul(ref term, term, yPower);
            ops.Add(ref correction, correction, term);
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus Pow<T, TOps>(in T value, int exponent, ref T destination, TOps ops)
        where TOps : struct, ICommutativeRingOps<T>
    {
        if (exponent < 0)
            return AlgebraStatus.InvalidInput;

        destination = ops.One;
        for (var i = 0; i < exponent; i++)
            ops.Mul(ref destination, destination, value);
        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus ValidateSame<P, N, T>(
        WittVectorView<T> left,
        WittVectorView<T> right,
        WittVectorBuilder<T> destination)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        var status = ValidateLength<P, N, T>(left);
        if (status != AlgebraStatus.Ok)
            return status;
        status = ValidateLength<P, N, T>(right);
        if (status != AlgebraStatus.Ok)
            return status;
        return ValidateDestination<P, N, T>(destination);
    }

    private static AlgebraStatus ValidateDestination<P, N, T>(WittVectorBuilder<T> destination)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        var status = ValidateContext<P, N>();
        if (status != AlgebraStatus.Ok)
            return status;
        return destination.Capacity < N.Value ? AlgebraStatus.InsufficientDestination : AlgebraStatus.Ok;
    }

    private static AlgebraStatus ValidateContext<P, N>()
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        if (P.Value <= 1 || N.Value <= 0)
            return AlgebraStatus.InvalidInput;
        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus ValidateArithmetic<N>()
        where N : IStaticPrecision
    {
        return N.Value <= 2 ? AlgebraStatus.Ok : AlgebraStatus.InvalidInput;
    }

    private static bool HasSupportedGhostComponent(int index) => index <= 1;

    private static int Binomial(int n, int k)
    {
        var result = 1;
        for (var i = 1; i <= k; i++)
            result = checked(result * (n - (k - i)) / i);
        return result;
    }
}
