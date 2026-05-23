using Helium.Hardware;

namespace Helium.Hardware.Tests;

public class NttTests
{
    [Fact]
    public void ForwardThenInverse_RoundTrips()
    {
        Span<ulong> data = [1UL, 2UL, 3UL, 4UL];
        var root = Ntt.RootForLength(3UL, data.Length, NttPrimes.Ntt998);

        Ntt.Forward(data, NttPrimes.Ntt998, root);
        Ntt.Inverse(data, NttPrimes.Ntt998, root);

        Assert.Equal([1UL, 2UL, 3UL, 4UL], data.ToArray());
    }

    [Fact]
    public void PolyMul_MatchesCyclicConvolution()
    {
        ReadOnlySpan<ulong> a = [1UL, 2UL, 0UL, 0UL];
        ReadOnlySpan<ulong> b = [3UL, 4UL, 0UL, 0UL];
        Span<ulong> result = stackalloc ulong[4];
        Span<ulong> work = stackalloc ulong[8];
        var root = Ntt.RootForLength(3UL, a.Length, NttPrimes.Ntt998);

        Ntt.PolyMul(a, b, result, work, NttPrimes.Ntt998, root);

        Assert.Equal([3UL, 10UL, 8UL, 0UL], result.ToArray());
    }

    [Fact]
    public void PolyMul_RejectsMissingWork()
    {
        var a = new ulong[] { 1UL, 2UL, 0UL, 0UL };
        var b = new ulong[] { 3UL, 4UL, 0UL, 0UL };
        var root = Ntt.RootForLength(3UL, a.Length, NttPrimes.Ntt998);

        var ex = Record.Exception(() =>
        {
            var rr = new ulong[4];
            var ww = new ulong[7];
            Ntt.PolyMul(a, b, rr, ww, NttPrimes.Ntt998, root);
        });
        Assert.IsType<ArgumentException>(ex);
    }
}
