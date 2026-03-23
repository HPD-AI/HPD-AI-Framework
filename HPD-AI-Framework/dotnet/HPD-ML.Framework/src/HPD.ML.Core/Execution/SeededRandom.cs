namespace HPD.ML.Core;

/// <summary>
/// Deterministic RNG helper. Given a seed, produces reproducible sequences.
/// </summary>
public static class SeededRandom
{
    /// <summary>
    /// Create a Random instance from an optional seed.
    /// If seed is null, returns Random.Shared (non-deterministic).
    /// </summary>
    public static Random Create(int? seed) => seed.HasValue ? new Random(seed.Value) : Random.Shared;

    /// <summary>
    /// Derive a child seed from a parent seed and an index.
    /// Ensures different components get different but reproducible sequences.
    /// </summary>
    public static int? Derive(int? parentSeed, int index)
        => parentSeed.HasValue ? unchecked(parentSeed.Value * 31 + index) : null;
}
