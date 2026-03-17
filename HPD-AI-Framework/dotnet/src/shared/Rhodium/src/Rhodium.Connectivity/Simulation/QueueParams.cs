namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Queue position model type.
/// </summary>
public enum QueueModelType : byte
{
    /// <summary>
    /// Always at front of queue (unrealistic, optimistic).
    /// Use for testing only.
    /// </summary>
    AlwaysFront = 0,

    /// <summary>
    /// Most conservative: queue advances only on trades, cancellations at tail.
    /// Slowest fills, safest assumptions.
    /// </summary>
    RiskAverse = 1,

    /// <summary>
    /// Probabilistic model with power-law profile: P(cancel before) = position^α
    /// Standard quadratic profile (α=2.0).
    /// Reference: Cont, Stoikov, Talreja (2010)
    /// </summary>
    PowerProbabilistic = 2,

    /// <summary>
    /// Power-law with modified profile for different market structures.
    /// Steeper near head (α1=3.0), gentler near tail (α2=1.5).
    /// Better for markets with informed/uninformed trader mix.
    /// </summary>
    PowerProbabilistic2 = 3,

    /// <summary>
    /// Power-law with asymmetric profile.
    /// High cancellation probability near middle, lower at extremes.
    /// Useful for volatile markets with frequent re-quoting.
    /// </summary>
    PowerProbabilistic3 = 4,

    /// <summary>
    /// Logarithmic profile: P(cancel before) = log(1 + scale × position) / log(1 + scale)
    /// Different behavior based on total quantity at level.
    /// Reference: Huang, Lehalle, Rosenbaum (2015)
    /// </summary>
    LogProbabilistic = 5,

    /// <summary>
    /// Modified logarithmic profile with steeper curve.
    /// More conservative than LogProbabilistic.
    /// </summary>
    LogProbabilistic2 = 6
}

/// <summary>
/// Queue model parameters.
/// </summary>
public sealed record QueueParams
{
    public required QueueModelType Model { get; init; }

    // Power-law parameters (PowerProbabilistic variants)
    public double Alpha { get; init; } = 2.0;        // PowerProbabilistic
    public double Alpha1 { get; init; } = 3.0;       // PowerProbabilistic2 (head)
    public double Alpha2 { get; init; } = 1.5;       // PowerProbabilistic2 (tail)
    public double Transition { get; init; } = 0.5;   // PowerProbabilistic2 (transition point)

    // Logarithmic parameters
    public double Scale { get; init; } = 10.0;       // LogProbabilistic variants

    /// <summary>
    /// Probabilistic queue entry (when order becomes active).
    /// If true, new orders join at random position based on inverse CDF.
    /// If false, new orders always join at tail (conservative).
    /// </summary>
    public bool ProbabilisticEntry { get; init; } = false;

    // ==================== PRESET FACTORY METHODS ====================

    public static QueueParams AlwaysFront() => new() { Model = QueueModelType.AlwaysFront };

    public static QueueParams RiskAverse() => new() { Model = QueueModelType.RiskAverse };

    public static QueueParams PowerQuadratic() => new()
    {
        Model = QueueModelType.PowerProbabilistic,
        Alpha = 2.0
    };

    public static QueueParams PowerCubic() => new()
    {
        Model = QueueModelType.PowerProbabilistic,
        Alpha = 3.0
    };

    public static QueueParams PowerAsymmetric() => new()
    {
        Model = QueueModelType.PowerProbabilistic2,
        Alpha1 = 3.0,
        Alpha2 = 1.5,
        Transition = 0.5
    };

    public static QueueParams Logarithmic(double scale = 10.0) => new()
    {
        Model = QueueModelType.LogProbabilistic,
        Scale = scale
    };

    /// <summary>
    /// Realistic preset for liquid markets (BTC, ETH, major pairs).
    /// Uses power quadratic with probabilistic entry.
    /// </summary>
    public static QueueParams RealisticLiquid() => new()
    {
        Model = QueueModelType.PowerProbabilistic,
        Alpha = 2.0,
        ProbabilisticEntry = true
    };

    /// <summary>
    /// Realistic preset for illiquid markets (altcoins, low-volume pairs).
    /// Uses more conservative cubic profile.
    /// </summary>
    public static QueueParams RealisticIlliquid() => new()
    {
        Model = QueueModelType.PowerProbabilistic,
        Alpha = 3.0,
        ProbabilisticEntry = false
    };
}
