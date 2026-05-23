using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Latency simulation parameters.
/// </summary>
public sealed record LatencyParams(
    Duration EntryMean,       // Average order entry latency (local → exchange)
    Duration ResponseMean,    // Average response latency (exchange → local)
    double StdDevFraction = 0.0  // 0.0 = constant, 0.2 = 20% std dev
);
