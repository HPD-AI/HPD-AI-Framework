namespace Helium.Hardware;

public static class NttPrimes
{
    public const ulong Goldilocks = 0xFFFFFFFF00000001UL;
    public const ulong Ntt998 = 998244353UL;
}

public static class Ntt
{
    public static void Forward(Span<ulong> a, ulong prime, ulong primitiveRoot)
    {
        ValidateTransformInput(a, prime);
        Transform(a, prime, primitiveRoot, inverse: false);
    }

    public static void Inverse(Span<ulong> a, ulong prime, ulong primitiveRoot)
    {
        ValidateTransformInput(a, prime);
        Transform(a, prime, primitiveRoot, inverse: true);
    }

    public static void PolyMul(
        ReadOnlySpan<ulong> a,
        ReadOnlySpan<ulong> b,
        Span<ulong> result,
        Span<ulong> work,
        ulong prime,
        ulong primitiveRoot)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Input polynomial spans must have the same length.");
        ValidateTransformInput(a, prime);
        if (result.Length < a.Length)
            throw new ArgumentException("Result span must be at least the input length.", nameof(result));
        if (work.Length < 2 * a.Length)
            throw new ArgumentException("Work span must have length at least 2 * input length.", nameof(work));

        var left = work[..a.Length];
        var right = work.Slice(a.Length, a.Length);
        a.CopyTo(left);
        b.CopyTo(right);

        Forward(left, prime, primitiveRoot);
        Forward(right, prime, primitiveRoot);

        for (var i = 0; i < left.Length; i++)
            left[i] = MulMod(left[i], right[i], prime);

        Inverse(left, prime, primitiveRoot);
        left.CopyTo(result);
    }

    public static ulong RootForLength(ulong primitiveGenerator, int length, ulong prime)
    {
        if (!IsPowerOfTwo(length))
            throw new ArgumentException("Length must be a power of two.", nameof(length));
        return PowMod(primitiveGenerator, (prime - 1) / (ulong)length, prime);
    }

    private static void Transform(Span<ulong> a, ulong prime, ulong primitiveRoot, bool inverse)
    {
        BitReverse(a);
        var n = a.Length;
        var root = inverse ? PowMod(primitiveRoot, prime - 2, prime) : primitiveRoot;

        for (var len = 2; len <= n; len <<= 1)
        {
            var wLen = PowMod(root, (ulong)(n / len), prime);
            for (var i = 0; i < n; i += len)
            {
                var w = 1UL;
                var half = len >> 1;
                for (var j = 0; j < half; j++)
                {
                    var u = a[i + j];
                    var v = MulMod(a[i + j + half], w, prime);
                    a[i + j] = AddMod(u, v, prime);
                    a[i + j + half] = SubMod(u, v, prime);
                    w = MulMod(w, wLen, prime);
                }
            }
        }

        if (inverse)
        {
            var nInv = PowMod((ulong)n, prime - 2, prime);
            for (var i = 0; i < n; i++)
                a[i] = MulMod(a[i], nInv, prime);
        }
    }

    private static void BitReverse(Span<ulong> a)
    {
        var j = 0;
        for (var i = 1; i < a.Length; i++)
        {
            var bit = a.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
                (a[i], a[j]) = (a[j], a[i]);
        }
    }

    private static ulong AddMod(ulong a, ulong b, ulong prime)
    {
        var sum = a + b;
        if (sum >= prime || sum < a)
            sum -= prime;
        return sum;
    }

    private static ulong SubMod(ulong a, ulong b, ulong prime) =>
        a >= b ? a - b : prime - (b - a);

    private static ulong MulMod(ulong a, ulong b, ulong prime) =>
        (ulong)(((System.UInt128)a * b) % prime);

    private static ulong PowMod(ulong value, ulong exponent, ulong prime)
    {
        var result = 1UL;
        var baseValue = value % prime;
        while (exponent != 0)
        {
            if ((exponent & 1UL) != 0)
                result = MulMod(result, baseValue, prime);
            baseValue = MulMod(baseValue, baseValue, prime);
            exponent >>= 1;
        }
        return result;
    }

    private static void ValidateTransformInput(ReadOnlySpan<ulong> a, ulong prime)
    {
        if (a.Length == 0)
            throw new ArgumentException("Transform length must be nonzero.");
        if (!IsPowerOfTwo(a.Length))
            throw new ArgumentException("Transform length must be a power of two.");
        if (prime < 2)
            throw new ArgumentOutOfRangeException(nameof(prime));
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
