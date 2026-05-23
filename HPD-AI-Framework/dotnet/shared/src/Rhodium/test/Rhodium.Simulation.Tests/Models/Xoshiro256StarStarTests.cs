using Rhodium.Simulation;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for Xoshiro256** PRNG.
/// Critical for deterministic backtesting.
/// </summary>
public class Xoshiro256StarStarTests
{
    [Fact]
    public void Constructor_InitializesWithState()
    {
        var state = new ulong[] { 1, 2, 3, 4 };
        var rng = new Xoshiro256StarStar(state);

        // Should not throw
        var value = rng.NextULong();
        Assert.NotEqual(0UL, value);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidStateLength()
    {
        var state = new ulong[] { 1, 2, 3 }; // Only 3 elements

        Assert.Throws<ArgumentException>(() => new Xoshiro256StarStar(state));
    }

    [Fact]
    public void Constructor_HandlesAllZeroState()
    {
        var state = new ulong[] { 0, 0, 0, 0 };
        var rng = new Xoshiro256StarStar(state);

        // Should initialize to non-zero state and produce valid sequence
        // (not get stuck outputting all zeros)
        var values = new List<ulong>();
        for (int i = 0; i < 10; i++)
        {
            values.Add(rng.NextULong());
        }

        // At least some values should be non-zero (not stuck in zero loop)
        Assert.Contains(values, v => v != 0UL);
    }

    [Fact]
    public void NextULong_ProducesDifferentValues()
    {
        var state = new ulong[] { 1, 2, 3, 4 };
        var rng = new Xoshiro256StarStar(state);

        var value1 = rng.NextULong();
        var value2 = rng.NextULong();
        var value3 = rng.NextULong();

        Assert.NotEqual(value1, value2);
        Assert.NotEqual(value2, value3);
        Assert.NotEqual(value1, value3);
    }

    [Fact]
    public void NextDouble_ReturnsValueInRange()
    {
        var state = new ulong[] { 12345, 67890, 11111, 22222 };
        var rng = new Xoshiro256StarStar(state);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble();
            Assert.InRange(value, 0.0, 1.0);
            Assert.True(value < 1.0); // Should be [0, 1) not [0, 1]
        }
    }

    [Fact]
    public void NextInt_ReturnsValueInRange()
    {
        var state = new ulong[] { 99999, 88888, 77777, 66666 };
        var rng = new Xoshiro256StarStar(state);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.Next(100);
            Assert.InRange(value, 0, 99);
        }
    }

    [Fact]
    public void NextInt_ThrowsOnInvalidMaxValue()
    {
        var state = new ulong[] { 1, 2, 3, 4 };
        var rng = new Xoshiro256StarStar(state);

        bool threw1 = false;
        try { rng.Next(0); } catch (ArgumentOutOfRangeException) { threw1 = true; }
        Assert.True(threw1);

        bool threw2 = false;
        try { rng.Next(-10); } catch (ArgumentOutOfRangeException) { threw2 = true; }
        Assert.True(threw2);
    }

    [Fact]
    public void NextIntRange_ReturnsValueInRange()
    {
        var state = new ulong[] { 11111, 22222, 33333, 44444 };
        var rng = new Xoshiro256StarStar(state);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.Next(50, 150);
            Assert.InRange(value, 50, 149);
        }
    }

    [Fact]
    public void NextIntRange_ThrowsOnInvalidRange()
    {
        var state = new ulong[] { 1, 2, 3, 4 };
        var rng = new Xoshiro256StarStar(state);

        bool threw1 = false;
        try { rng.Next(100, 50); } catch (ArgumentOutOfRangeException) { threw1 = true; }
        Assert.True(threw1); // min > max

        bool threw2 = false;
        try { rng.Next(50, 50); } catch (ArgumentOutOfRangeException) { threw2 = true; }
        Assert.True(threw2); // min == max
    }

    [Fact]
    public void Determinism_SameSeedProducesSameSequence()
    {
        var state1 = new ulong[] { 12345, 67890, 11111, 22222 };
        var state2 = new ulong[] { 12345, 67890, 11111, 22222 };

        var rng1 = new Xoshiro256StarStar(state1);
        var rng2 = new Xoshiro256StarStar(state2);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rng1.NextULong(), rng2.NextULong());
        }
    }

    [Fact]
    public void Determinism_DifferentSeedsProduceDifferentSequences()
    {
        var state1 = new ulong[] { 12345, 67890, 11111, 22222 };
        var state2 = new ulong[] { 99999, 88888, 77777, 66666 };

        var rng1 = new Xoshiro256StarStar(state1);
        var rng2 = new Xoshiro256StarStar(state2);

        var values1 = new List<ulong>();
        var values2 = new List<ulong>();

        for (int i = 0; i < 100; i++)
        {
            values1.Add(rng1.NextULong());
            values2.Add(rng2.NextULong());
        }

        // Sequences should be different
        Assert.NotEqual(values1, values2);
    }

    [Fact]
    public void Quality_DoubleDistribution()
    {
        var state = new ulong[] { 314159, 265358, 979323, 846264 };
        var rng = new Xoshiro256StarStar(state);

        var buckets = new int[10];
        const int samples = 100000;

        for (int i = 0; i < samples; i++)
        {
            var value = rng.NextDouble();
            var bucket = (int)(value * 10);
            if (bucket == 10) bucket = 9; // Handle edge case
            buckets[bucket]++;
        }

        // Each bucket should have roughly 10% of samples (within 20% tolerance)
        var expected = samples / 10.0;
        var tolerance = expected * 0.2;

        foreach (var count in buckets)
        {
            Assert.InRange(count, expected - tolerance, expected + tolerance);
        }
    }

    [Fact]
    public void Quality_NoShortCycles()
    {
        var state = new ulong[] { 1, 2, 3, 4 };
        var rng = new Xoshiro256StarStar(state);

        var seen = new HashSet<ulong>();
        const int iterations = 100000;

        for (int i = 0; i < iterations; i++)
        {
            var value = rng.NextULong();
            Assert.DoesNotContain(value, seen); // No immediate repeats
            seen.Add(value);
        }

        // Should have generated 100k unique values
        Assert.Equal(iterations, seen.Count);
    }

    [Fact]
    public void NextDouble_Precision()
    {
        var state = new ulong[] { 271828, 182845, 904523, 536028 };
        var rng = new Xoshiro256StarStar(state);

        // Verify we're using full 53-bit precision
        var values = new HashSet<double>();
        for (int i = 0; i < 10000; i++)
        {
            values.Add(rng.NextDouble());
        }

        // Should have 10k unique double values (no premature collisions)
        Assert.Equal(10000, values.Count);
    }
}
