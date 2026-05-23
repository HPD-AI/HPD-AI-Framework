namespace Rhodium.Simulation;

/// <summary>
/// Seed expansion for deterministic PRNG initialization.
/// Uses SplitMix64 to expand 32-bit config seed into 256-bit Xoshiro256** state.
/// </summary>
public static class SeedExpansion
{
    /// <summary>
    /// Expand config seed + batch map version + virtual index into 4×64-bit state for Xoshiro256**.
    /// </summary>
    public static ulong[] ExpandSeed(int configSeed, int batchMapVersion, int virtualIndex)
    {
        ulong state = unchecked((ulong)HashCode.Combine(configSeed, batchMapVersion, virtualIndex));

        return new[]
        {
            SplitMix64(ref state),
            SplitMix64(ref state),
            SplitMix64(ref state),
            SplitMix64(ref state)
        };
    }

    private static ulong SplitMix64(ref ulong state)
    {
        ulong result = (state += 0x9E3779B97f4A7C15);
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EB;
        return result ^ (result >> 31);
    }
}
