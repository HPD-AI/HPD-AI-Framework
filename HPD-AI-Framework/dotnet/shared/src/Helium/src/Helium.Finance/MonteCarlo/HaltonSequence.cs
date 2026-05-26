namespace Helium.Finance.MonteCarlo;

public sealed class HaltonSequence
{
    private readonly int _base;
    private int _index;

    public HaltonSequence(int @base, int startIndex = 1)
    {
        if (@base < 2)
            throw new ArgumentOutOfRangeException(nameof(@base), "Halton base must be at least two.");

        if (startIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index must be positive.");

        _base = @base;
        _index = startIndex;
    }

    public double Next()
    {
        if (_index == int.MaxValue)
            throw new InvalidOperationException("Halton sequence index has reached the maximum supported value.");

        var value = RadicalInverse(_index, _base);
        if (!double.IsFinite(value) || value <= 0.0 || value >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(_index), "Halton sequence value must be finite and strictly inside (0, 1).");

        _index++;
        return value;
    }

    public static double RadicalInverse(int index, int @base)
    {
        if (index < 1)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be positive.");

        if (@base < 2)
            throw new ArgumentOutOfRangeException(nameof(@base), "Base must be at least two.");

        var result = 0.0;
        var inverseBase = 1.0 / @base;
        var fraction = inverseBase;
        var current = index;

        while (current > 0)
        {
            var digit = current % @base;
            result += digit * fraction;
            current /= @base;
            fraction *= inverseBase;
        }

        if (!double.IsFinite(result) || result <= 0.0 || result >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(index), "Halton radical inverse must be finite and strictly inside (0, 1).");

        return result;
    }
}
