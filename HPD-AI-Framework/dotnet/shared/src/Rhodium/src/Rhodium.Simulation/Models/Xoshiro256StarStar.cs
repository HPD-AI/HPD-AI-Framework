namespace Rhodium.Simulation;

/// <summary>
/// Xoshiro256StarStar - Fast, high-quality pseudorandom number generator.
/// Period: 2^256 - 1
/// Reference: Blackman and Vigna (2018)
/// Thread-Safety: NOT thread-safe. Each thread must have its own instance.
/// </summary>
public ref struct Xoshiro256StarStar
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>
    /// Initialize from 4×64-bit state (from SeedExpansion.ExpandSeed).
    /// </summary>
    public Xoshiro256StarStar(ulong[] state)
    {
        if (state.Length != 4)
            throw new ArgumentException("State must have exactly 4 elements", nameof(state));

        _s0 = state[0];
        _s1 = state[1];
        _s2 = state[2];
        _s3 = state[3];

        // Ensure non-zero state (all zeros would produce all zeros forever)
        if (_s0 == 0 && _s1 == 0 && _s2 == 0 && _s3 == 0)
            _s0 = 1;
    }

    /// <summary>
    /// Generate next random ulong.
    /// </summary>
    public ulong NextULong()
    {
        ulong result = RotL(_s1 * 5, 7) * 9;

        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;

        _s3 = RotL(_s3, 45);

        return result;
    }

    /// <summary>
    /// Generate random double in [0.0, 1.0).
    /// </summary>
    public double NextDouble()
    {
        // Use upper 53 bits for IEEE 754 double mantissa
        return (NextULong() >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Generate random int in [0, maxValue).
    /// </summary>
    public int Next(int maxValue)
    {
        if (maxValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive");

        return (int)(NextDouble() * maxValue);
    }

    /// <summary>
    /// Generate random int in [minValue, maxValue).
    /// </summary>
    public int Next(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue");

        return (int)(NextDouble() * (maxValue - minValue)) + minValue;
    }

    private static ulong RotL(ulong x, int k)
    {
        return (x << k) | (x >> (64 - k));
    }
}
