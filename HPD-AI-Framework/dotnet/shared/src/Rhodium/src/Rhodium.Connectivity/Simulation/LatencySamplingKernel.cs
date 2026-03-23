using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Kernel for pre-sampling latency values into tensor columns during initialization.
/// Uses deterministic PRNG based on virtual index for reproducible results.
/// </summary>
public readonly struct LatencySamplingKernel : IComputeKernel
{
    private readonly LatencyParams _latency;
    private readonly int _configSeed;
    private readonly int _batchMapVersion;

    public LatencySamplingKernel(LatencyParams latency, int configSeed, int batchMapVersion)
    {
        _latency = latency;
        _configSeed = configSeed;
        _batchMapVersion = batchMapVersion;
    }

    public void Execute(ITensorStore store, int pageIndex)
    {
        var entryLatencyNanos = store.GetPage(SimField.EntryLatencyNanos, pageIndex);
        var responseLatencyNanos = store.GetPage(SimField.ResponseLatencyNanos, pageIndex);

        for (int i = 0; i < entryLatencyNanos.Length; i++)
        {
            int virtualIndex = pageIndex * entryLatencyNanos.Length + i;

            // Deterministic PRNG for this virtual index
            var state = SeedExpansion.ExpandSeed(_configSeed, _batchMapVersion, virtualIndex);
            var rng = new Xoshiro256StarStar(state);

            // Sample entry latency
            long entryNanos = ApplyJitter(
                _latency.EntryMean.Nanos,
                _latency.StdDevFraction,
                ref rng);

            // Sample response latency
            long responseNanos = ApplyJitter(
                _latency.ResponseMean.Nanos,
                _latency.StdDevFraction,
                ref rng);

            entryLatencyNanos[i] = new FactorF64(entryNanos);
            responseLatencyNanos[i] = new FactorF64(responseNanos);
        }
    }

    /// <summary>
    /// Apply jitter to latency using uniform distribution (cross-platform safe).
    /// </summary>
    private static long ApplyJitter(long meanNanos, double stdDevFraction, ref Xoshiro256StarStar rng)
    {
        if (stdDevFraction <= 0.0)
            return meanNanos;

        long maxJitter = (long)(meanNanos * stdDevFraction);
        long jitter = (long)(rng.NextDouble() * 2 * maxJitter) - maxJitter;
        return Math.Max(0, meanNanos + jitter);
    }
}
