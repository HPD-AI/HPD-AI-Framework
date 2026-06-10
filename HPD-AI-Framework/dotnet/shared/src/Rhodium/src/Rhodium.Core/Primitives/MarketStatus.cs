namespace Rhodium.Primitives;

/// <summary>
/// Market trading status.
/// Controls whether orders can be submitted and filled.
/// </summary>
public enum MarketStatus : byte
{
    /// <summary>
    /// Market is in pre-open phase.
    /// Simulated order submission is disabled until the market opens.
    /// </summary>
    PreOpen = 1,

    /// <summary>
    /// Market is open for trading.
    /// Orders can be submitted and filled.
    /// </summary>
    Open = 2,

    /// <summary>
    /// Market is closed.
    /// No order submission or fills.
    /// </summary>
    Closed = 3,

    /// <summary>
    /// Trading is temporarily halted.
    /// Simulated order submission and fills are disabled.
    /// Common during circuit breakers or exchange issues.
    /// </summary>
    Halted = 4
}
