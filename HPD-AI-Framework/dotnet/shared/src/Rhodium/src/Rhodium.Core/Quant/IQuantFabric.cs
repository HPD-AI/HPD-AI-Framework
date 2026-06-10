namespace Rhodium.Quant;

/// <summary>
/// Background computation fabric for heavy portfolio math.
/// Operates on rented snapshots, re-enters system only through gated results.
/// </summary>
/// <remarks>
/// Contract:
/// - Snapshots are taken only at coarse boundaries (default: BarClosed)
/// - Background jobs are coalesced and may be dropped if stale
/// - Results MUST be accepted only if BatchMap.Version matches submission version
/// - Sequence gating ensures deterministic re-entry in backtest/live modes
/// </remarks>
public interface IQuantFabric
{
    /// <summary>
    /// Submit a computation request for background processing.
    /// Results re-enter via QuantResultReady event (see Rhodium.Events).
    /// </summary>
    /// <param name="request">Request with gating key (Sequence, BatchMapVersion)</param>
    void Submit(QuantRequest request);
}
