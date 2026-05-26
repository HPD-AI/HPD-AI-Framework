namespace Rhodium.Simulation;

/// <summary>
/// Matching fidelity is an exchange or instrument policy, not a separate simulation architecture.
/// </summary>
public enum MatchingFidelity
{
    FastVectorApproximation = 1,
    QueueAccurate = 2,
    MarketByOrder = 3
}
