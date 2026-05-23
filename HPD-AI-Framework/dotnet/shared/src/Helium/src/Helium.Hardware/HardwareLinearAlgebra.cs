namespace Helium.Hardware;

public static class HardwareLinearAlgebra
{
    public static void LuDecompose(Span<ulong> a, int n, ulong prime)
    {
        ValidateSquareMatrix(a.Length, n);
        ValidatePrime(prime);

        for (int k = 0; k < n; k++)
        {
            var pivot = a[k * n + k] % prime;
            if (pivot == 0)
                throw new ArgumentException("Matrix has a zero pivot; host must validate nonsingularity or reorder rows.", nameof(a));

            var pivotInv = ModInverse(pivot, prime);
            for (int i = k + 1; i < n; i++)
            {
                var factorIndex = i * n + k;
                a[factorIndex] = MulMod(a[factorIndex], pivotInv, prime);
                var factor = a[factorIndex];

                for (int j = k + 1; j < n; j++)
                {
                    var index = i * n + j;
                    a[index] = SubMod(a[index], MulMod(factor, a[k * n + j], prime), prime);
                }
            }
        }
    }

    public static void Solve(
        ReadOnlySpan<ulong> a,
        ReadOnlySpan<ulong> b,
        Span<ulong> x,
        int n,
        ulong prime,
        Span<ulong> work)
    {
        ValidateSquareMatrix(a.Length, n);
        ValidateVector(b.Length, n, nameof(b));
        ValidateVector(x.Length, n, nameof(x));
        ValidatePrime(prime);

        var requiredWork = RequiredSolveWorkLength(n);
        if (work.Length < requiredWork)
            throw new ArgumentException($"Work span must contain at least {requiredWork} elements.", nameof(work));

        var lu = work[..(n * n)];
        var y = work.Slice(n * n, n);
        a.CopyTo(lu);

        for (int i = 0; i < lu.Length; i++)
            lu[i] %= prime;

        LuDecompose(lu, n, prime);

        for (int i = 0; i < n; i++)
        {
            var sum = b[i] % prime;
            for (int j = 0; j < i; j++)
                sum = SubMod(sum, MulMod(lu[i * n + j], y[j], prime), prime);
            y[i] = sum;
        }

        for (int i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (int j = i + 1; j < n; j++)
                sum = SubMod(sum, MulMod(lu[i * n + j], x[j], prime), prime);

            var pivot = lu[i * n + i];
            if (pivot == 0)
                throw new ArgumentException("Matrix has a zero pivot; host must validate nonsingularity or reorder rows.", nameof(a));
            x[i] = MulMod(sum, ModInverse(pivot, prime), prime);
        }
    }

    public static int RequiredSolveWorkLength(int n)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));
        return checked(n * n + n);
    }

    internal static ulong AddMod(ulong left, ulong right, ulong prime) =>
        (ulong)(((System.UInt128)(left % prime) + (right % prime)) % prime);

    internal static ulong SubMod(ulong left, ulong right, ulong prime)
    {
        left %= prime;
        right %= prime;
        return left >= right ? left - right : prime - (right - left);
    }

    internal static ulong MulMod(ulong left, ulong right, ulong prime) =>
        (ulong)(((System.UInt128)(left % prime) * (right % prime)) % prime);

    internal static ulong ModInverse(ulong value, ulong prime)
    {
        value %= prime;
        if (value == 0)
            return 0;

        var result = 1UL;
        var factor = value;
        var exponent = prime - 2;

        while (exponent != 0)
        {
            if ((exponent & 1UL) != 0)
                result = MulMod(result, factor, prime);
            factor = MulMod(factor, factor, prime);
            exponent >>= 1;
        }

        return result;
    }

    private static void ValidateSquareMatrix(int length, int n)
    {
        if (n < 0)
            throw new ArgumentOutOfRangeException(nameof(n));
        if (length != n * n)
            throw new ArgumentException("Matrix span length must equal n * n.");
    }

    private static void ValidateVector(int length, int n, string parameterName)
    {
        if (length != n)
            throw new ArgumentException("Vector span length must equal n.", parameterName);
    }

    private static void ValidatePrime(ulong prime)
    {
        if (prime < 2)
            throw new ArgumentOutOfRangeException(nameof(prime), "Prime modulus must be at least 2.");
    }
}
