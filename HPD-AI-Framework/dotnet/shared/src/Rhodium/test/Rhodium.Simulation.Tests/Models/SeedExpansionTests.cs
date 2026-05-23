using Rhodium.Simulation;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for SeedExpansion (SplitMix64).
/// Critical for deterministic simulation.
/// </summary>
public class SeedExpansionTests
{
    [Fact]
    public void ExpandSeed_ProducesFourValues()
    {
        var state = SeedExpansion.ExpandSeed(configSeed: 12345, batchMapVersion: 1, virtualIndex: 0);

        Assert.Equal(4, state.Length);
        Assert.All(state, value => Assert.NotEqual(0UL, value));
    }

    [Fact]
    public void ExpandSeed_DeterministicForSameInputs()
    {
        var state1 = SeedExpansion.ExpandSeed(123, 1, 0);
        var state2 = SeedExpansion.ExpandSeed(123, 1, 0);

        Assert.Equal(state1, state2);
    }

    [Fact]
    public void ExpandSeed_DifferentForDifferentConfigSeed()
    {
        var state1 = SeedExpansion.ExpandSeed(123, 1, 0);
        var state2 = SeedExpansion.ExpandSeed(456, 1, 0);

        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void ExpandSeed_DifferentForDifferentBatchMapVersion()
    {
        var state1 = SeedExpansion.ExpandSeed(123, 1, 0);
        var state2 = SeedExpansion.ExpandSeed(123, 2, 0);

        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void ExpandSeed_DifferentForDifferentVirtualIndex()
    {
        var state1 = SeedExpansion.ExpandSeed(123, 1, 0);
        var state2 = SeedExpansion.ExpandSeed(123, 1, 1);

        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void ExpandSeed_HandlesNegativeValues()
    {
        var state = SeedExpansion.ExpandSeed(-12345, -1, -100);

        Assert.Equal(4, state.Length);
        Assert.All(state, value => Assert.NotEqual(0UL, value));
    }

    [Fact]
    public void ExpandSeed_HandlesZeroValues()
    {
        var state = SeedExpansion.ExpandSeed(0, 0, 0);

        Assert.Equal(4, state.Length);
        // Even with all zeros, should produce non-zero state
        Assert.Contains(state, value => value != 0UL);
    }

    [Fact]
    public void ExpandSeed_ProducesUniqueStatesForManyVIs()
    {
        const int configSeed = 42;
        const int batchMapVersion = 1;
        var states = new HashSet<string>();

        for (int vi = 0; vi < 1000; vi++)
        {
            var state = SeedExpansion.ExpandSeed(configSeed, batchMapVersion, vi);
            var key = string.Join(",", state);
            Assert.DoesNotContain(key, states);
            states.Add(key);
        }

        Assert.Equal(1000, states.Count);
    }

    [Fact]
    public void ExpandSeed_ConsistentAcrossRuns()
    {
        // Run multiple times to ensure no hidden state
        var results = new List<ulong[]>();

        for (int run = 0; run < 10; run++)
        {
            var state = SeedExpansion.ExpandSeed(999, 5, 123);
            results.Add(state);
        }

        // All runs should produce identical results
        for (int i = 1; i < results.Count; i++)
        {
            Assert.Equal(results[0], results[i]);
        }
    }

    [Fact]
    public void ExpandSeed_Integration_WithXoshiro()
    {
        // Verify expanded seed works with Xoshiro256**
        var state = SeedExpansion.ExpandSeed(12345, 1, 0);
        var rng = new Xoshiro256StarStar(state);

        // Should produce valid random numbers
        var value1 = rng.NextDouble();
        var value2 = rng.NextDouble();

        Assert.InRange(value1, 0.0, 1.0);
        Assert.InRange(value2, 0.0, 1.0);
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void ExpandSeed_Integration_DeterministicSequences()
    {
        // Same inputs should produce identical random sequences
        var state1 = SeedExpansion.ExpandSeed(777, 2, 50);
        var state2 = SeedExpansion.ExpandSeed(777, 2, 50);

        var rng1 = new Xoshiro256StarStar(state1);
        var rng2 = new Xoshiro256StarStar(state2);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rng1.NextDouble(), rng2.NextDouble());
        }
    }
}
