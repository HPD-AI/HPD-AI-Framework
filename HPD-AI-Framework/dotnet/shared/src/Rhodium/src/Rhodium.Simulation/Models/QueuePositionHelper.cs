namespace Rhodium.Simulation;

/// <summary>
/// Queue position calculation helpers for simulation.
/// </summary>
public static class QueuePositionHelper
{
    /// <summary>
    /// Initial queue position when an order is placed.
    /// Conservative assumption: join at tail (position = 1.0).
    /// With probabilistic entry, could join at random position.
    /// </summary>
    public static decimal GetInitialPosition(this QueueParams queue)
    {
        if (queue.InitialPositionOverride.HasValue)
            return queue.InitialPositionOverride.Value;

        if (queue.Model == QueueModelType.AlwaysFront)
            return 0.0m;

        if (queue.ProbabilisticEntry)
        {
            // Deterministic replay default: use median queue entry instead of sampling.
            return 0.5m;
        }

        // Conservative: join at tail
        return 1.0m;
    }

    /// <summary>
    /// Calculate how much the queue position advances based on trade volume.
    /// Returns the amount to subtract from current position (0 = tail, 1 = front).
    /// </summary>
    public static decimal CalculateAdvancement(this QueueParams queue, decimal currentPosition, decimal tradeVolume)
    {
        if (queue.AdvancementPerUnitOverride.HasValue)
            return Math.Min(currentPosition, tradeVolume * queue.AdvancementPerUnitOverride.Value);

        if (queue.Model == QueueModelType.AlwaysFront)
            return currentPosition; // Jump to front immediately

        // Simplified advancement logic
        // Real implementation would use queue model formulas from QueueAdvancementKernel
        var advancementFactor = queue.Model switch
        {
            QueueModelType.RiskAverse => 0.1m,          // Very conservative
            QueueModelType.PowerProbabilistic => 0.3m,  // Moderate
            QueueModelType.LogProbabilistic => 0.2m,    // Moderate-conservative
            _ => 0.2m
        };

        // Advance proportional to trade volume (simplified)
        // In reality, this depends on total queue size, our position, and cancellation probabilities
        var advancement = tradeVolume * advancementFactor * currentPosition;
        return Math.Min(advancement, currentPosition);
    }
}
